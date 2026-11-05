using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace TechZone.Data;

/// <summary>
/// Chuyển đổi DB cũ thời <c>EnsureCreated</c> sang schema migrations (Sprint 1, P0-2).
///
/// Vấn đề: <c>EnsureCreatedAsync</c> chỉ tạo DB khi file chưa tồn tại, không thêm
/// bảng/cột mới, và không tạo bảng <c>__EFMigrationsHistory</c>. Áp mù migration
/// đầu (<c>MigrateAsync</c>) lên DB đó sẽ lỗi "table already exists" vì các bảng
/// cũ đã tồn tại nhưng history trống. Converter này KHÔNG áp mù:
///   1. Backup file .db trước.
///   2. So schema thật (sqlite_master + PRAGMA table_info) với model EF hiện tại.
///   3. Chỉ tạo PHẦN THIẾU: CREATE TABLE cho bảng thiếu (lấy từ script mà
///      chính migration sinh ra) + ALTER TABLE ADD COLUMN cho cột thiếu
///      (kiểu/DEFAULT suy từ metadata của model).
///   4. Chỉ khi schema đã khớp model mới chèn dòng baseline vào
///      <c>__EFMigrationsHistory</c> cho TAT CA migration — để <c>MigrateAsync</c>
///      sau này bỏ qua chúng thay vì tạo lại bảng. (Sprint 2: truoc day chi
///      baseline migration dau; tu khi co migration thu 2 tro di, schema
///      sau convert == model hien tai == ket qua ap TAT CA migration, nen
///      chi baseline 1 cai se khien MigrateAsync ap lai migration sau va loi
///      "duplicate column" voi cot converter vua them.)
///   5. Xác minh lại toàn bộ schema; thiếu gì là throw, không im lặng.
/// Rollback = copy file backup đè lên file DB (xem <see cref="RestoreBackup"/>).
/// Converter KHÔNG được app gọi tự động — chỉ chạy thủ công theo
/// DATABASE-MIGRATION.md hoặc từ test. KHÔNG bao giờ xóa DB thật.
/// </summary>
public static class LegacyDbConverter
{
    public sealed record ConversionReport(
        string DbPath,
        string BackupPath,
        List<string> MissingTables,
        List<string> MissingColumns,
        bool BaselineInserted,
        string MigrationId);

    /// <summary>Chuyển đổi file DB tại <paramref name="dbPath"/> theo quy trình trên.</summary>
    public static async Task<ConversionReport> ConvertAsync(
        string dbPath, string? backupPath = null, CancellationToken ct = default)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Khong tim thay file DB: " + dbPath, dbPath);

        // 1. Backup truoc khi dung vao file that. DB chay WAL mode (journal_mode
        // = wal) nen phai checkpoint TRUNCATE truoc: neu khong, du lieu moi
        // nhat nam trong file -wal chu khong phai .db, file .bak copy ra se
        // thieu du lieu. Checkpoint day het -wal vao .db roi moi copy.
        // Clear pool TRUOC: connection pooled dang mo co the giu snapshot cu
        // (chan TRUNCATE) hoac checkpoint frame cu de len file sau nay.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        CheckpointWal(dbPath);
        backupPath ??= UniqueBackupPath(dbPath);
        File.Copy(dbPath, backupPath);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var context = new AppDbContext(options);

        var migrationIds = context.Database.GetMigrations().ToList();
        var migrationId = migrationIds.FirstOrDefault()
            ?? throw new InvalidOperationException("Khong tim thay migration nao trong assembly.");
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.0";

        // 2. Schema that hien co.
        var (actualTables, actualColumns, actualIndexes) = await InspectAsync(context, ct);

        // 3a. Bang thieu: lay CREATE TABLE tu script do chinh migration sinh ra.
        var expectedTables = ExpectedTables(context);
        var missingTables = expectedTables.Keys
            .Where(t => !actualTables.Contains(t))
            .ToList();
        var createScript = context.Database.GenerateCreateScript();
        var createTableSql = ParseCreateTables(createScript);
        foreach (var table in missingTables)
        {
            if (!createTableSql.TryGetValue(table, out var sql))
                throw new InvalidOperationException(
                    $"Khong sinh duoc CREATE TABLE cho bang thieu '{table}' tu migration script.");
            await context.Database.ExecuteSqlRawAsync(sql, ct);
        }

        // 3b. Cot thieu tren bang da co: ALTER TABLE ADD COLUMN, kieu + DEFAULT
        // suy tu metadata cua model (cot NOT NULL bat buoc phai co DEFAULT
        // vi SQLite cam ADD COLUMN NOT NULL khong co gia tri mac dinh).
        var missingColumns = new List<string>();
        foreach (var (table, columns) in expectedTables)
        {
            if (missingTables.Contains(table)) continue; // bang moi da du cot
            foreach (var (column, ddl) in columns)
            {
                if (actualColumns.TryGetValue(table, out var existing) && existing.Contains(column))
                    continue;
                missingColumns.Add($"{table}.{column}");
                // Ten bang/cot lay tu metadata cua model (khong phai input
                // nguoi dung) nen noi chuoi o day la an toan.
                var alterSql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + ddl;
                await context.Database.ExecuteSqlRawAsync(alterSql, ct);
            }
        }

        // 3c. Index thieu (vd IX_Orders_IdempotencyKey tren bang Orders da co):
        // lay CREATE INDEX tu migration script, chi ap cai nao chua co.
        var createIndexSql = ParseCreateIndexes(createScript);
        foreach (var (indexName, sql) in createIndexSql)
        {
            if (!actualIndexes.Contains(indexName))
                await context.Database.ExecuteSqlRawAsync(sql, ct);
        }

        // 4. Baseline: chi chen khi schema da khop — chen cho TAT CA
        // migration (xem ghi chu o doc comment class): schema sau convert
        // == model hien tai nen moi migration deu coi nhu da ap.
        var baselineInserted = false;
        foreach (var id in migrationIds)
            baselineInserted |= await InsertBaselineAsync(context, id, productVersion, ct);

        // 5. Xac minh lai: schema phai khop model + dong baseline phai ton tai.
        var (tablesAfter, columnsAfter, _) = await InspectAsync(context, ct);
        var stillMissing = expectedTables
            .Where(kv => !tablesAfter.Contains(kv.Key)
                || kv.Value.Any(c => !columnsAfter[kv.Key].Contains(c.Column)))
            .Select(kv => kv.Key)
            .ToList();
        if (stillMissing.Count > 0)
            throw new InvalidOperationException(
                "Chuyen doi chua du: van thieu " + string.Join(", ", stillMissing)
                + ". Khoi phuc file backup: " + backupPath);
        foreach (var id in migrationIds)
        {
            if (!await BaselineExistsAsync(context, id, ct))
                throw new InvalidOperationException(
                    "Khong chen duoc dong baseline cho " + id + ".");
        }

        return new ConversionReport(dbPath, backupPath, missingTables, missingColumns,
            baselineInserted, migrationId);
    }

    /// <summary>Rollback: copy file backup đè lên file DB. Chỉ dùng khi chuyển đổi lỗi.</summary>
    public static void RestoreBackup(string dbPath, string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Khong tim thay file backup: " + backupPath, backupPath);
        // THU TU BAT BUOC: (1) clear pool truoc — connection pooled dang mo
        // ma dong SAU khi copy se checkpoint frame WAL cu (trang thai sau
        // convert) de len file vua restore, pha hong rollback; (2) moi copy
        // backup de len; (3) xoa -wal/-shm cu (mo DB len se replay -wal va
        // tro lai trang thai sau convert neu khong xoa).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(backupPath, dbPath, overwrite: true);
        DeleteQuietly(dbPath + "-wal");
        DeleteQuietly(dbPath + "-shm");
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// Day toan bo WAL vao file .db chinh (TRUNCATE) de file .db tu dung.
    /// Throw neu checkpoint khong day du (vd con connection khac giu snapshot)
    /// — backup thieu du lieu thi tha bao loi con hon backup thieu am tham.
    /// </summary>
    private static void CheckpointWal(string dbPath)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("PRAGMA wal_checkpoint khong tra ket qua cho " + dbPath);
        var busy = reader.GetInt32(0);
        var logFrames = reader.GetInt32(1);
        var checkpointedFrames = reader.GetInt32(2);
        if (busy != 0 || checkpointedFrames < logFrames)
            throw new InvalidOperationException(
                $"Khong checkpoint het WAL cho {dbPath} (busy={busy}, log={logFrames}, checkpointed={checkpointedFrames}). "
                + "Hay tat app / dong moi ket noi toi DB roi chay lai.");
    }

    // --- Schema ky vong tu model EF (khong tinh __EFMigrationsHistory) ---
    private static Dictionary<string, List<(string Column, string Ddl)>> ExpectedTables(AppDbContext context)
    {
        var result = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null) continue;
            var columns = new List<(string, string)>();
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                if (column is null) continue;
                columns.Add((column, ColumnDdl(property)));
            }
            result[table] = columns;
        }
        return result;
    }

    private static string ColumnDdl(Microsoft.EntityFrameworkCore.Metadata.IProperty property)
    {
        var type = property.GetColumnType() ?? "TEXT";
        if (property.IsNullable) return type;
        var defaultSql = property.GetDefaultValueSql();
        if (defaultSql is not null) return $"{type} NOT NULL DEFAULT {defaultSql}";
        var defaultValue = property.GetDefaultValue();
        if (defaultValue is not null) return $"{type} NOT NULL DEFAULT {Literal(defaultValue)}";
        var fallback = property.ClrType switch
        {
            var t when t == typeof(string) => "''",
            var t when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "'0001-01-01 00:00:00'",
            var t when t == typeof(Guid) => "''",
            var t when t == typeof(byte[]) => "X''",
            _ => "0", // so / bool / enum
        };
        return $"{type} NOT NULL DEFAULT {fallback}";
    }

    private static string Literal(object value) => value switch
    {
        string s => "'" + s.Replace("'", "''") + "'",
        bool b => b ? "1" : "0",
        DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss") + "'",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0",
    };

    // --- Doc schema that tu SQLite ---
    private static async Task<(HashSet<string> Tables, Dictionary<string, HashSet<string>> Columns, HashSet<string> Indexes)>
        InspectAsync(AppDbContext context, CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var columns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        var conn = context.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    tables.Add(reader.GetString(0));

            foreach (var table in tables.ToList())
            {
                var cols = new HashSet<string>(StringComparer.Ordinal);
                await using var pragma = conn.CreateCommand();
                pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using (var reader = await pragma.ExecuteReaderAsync(ct))
                    while (await reader.ReadAsync(ct))
                        cols.Add(reader.GetString(1));
                columns[table] = cols;
            }

            await using var idx = conn.CreateCommand();
            idx.CommandText = "SELECT name FROM sqlite_master WHERE type='index'";
            await using (var reader = await idx.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    indexes.Add(reader.GetString(0));
        }
        finally
        {
            await conn.CloseAsync();
        }
        return (tables, columns, indexes);
    }

    // --- Baseline __EFMigrationsHistory ---
    private static async Task<bool> InsertBaselineAsync(
        AppDbContext context, string migrationId, string productVersion, CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            )
            """, ct);
        if (await BaselineExistsAsync(context, migrationId, ct)) return false;
        var id = new Microsoft.Data.Sqlite.SqliteParameter("@id", migrationId);
        var ver = new Microsoft.Data.Sqlite.SqliteParameter("@ver", productVersion);
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@id, @ver)",
            new[] { id, ver }, ct);
        return true;
    }

    private static async Task<bool> BaselineExistsAsync(
        AppDbContext context, string migrationId, CancellationToken ct)
    {
        var conn = context.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = migrationId;
            cmd.Parameters.Add(p);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            return false; // bang history chua ton tai
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // --- Parse migration script (chi CREATE TABLE / CREATE INDEX) ---
    private static Dictionary<string, string> ParseCreateTables(string script)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(script,
                     """CREATE TABLE "([^"]+)" \(.*?\n\);""",
                     RegexOptions.Singleline))
            result[m.Groups[1].Value] = m.Value;
        return result;
    }

    private static Dictionary<string, string> ParseCreateIndexes(string script)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(script,
                     """CREATE (?:UNIQUE )?INDEX "([^"]+)" ON "[^"]+" \([^)]*\);"""))
            result[m.Groups[1].Value] = m.Value;
        return result;
    }

    private static string UniqueBackupPath(string dbPath)
    {
        var candidate = dbPath + ".bak";
        if (!File.Exists(candidate)) return candidate;
        return dbPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }
}
