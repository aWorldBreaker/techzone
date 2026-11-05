using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Data;
using TechZone.Models;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TechZone.Tests;

/// <summary>
/// Test Sprint 1 — P0: Migrations + bảo toàn DB.
/// Chạy tuần tự (không song song) vì có test khởi động app thật trên 1 cổng.
/// Mọi DB test đều là file tạm/copy — KHÔNG bao giờ đụng techzone.db thật,
/// trừ test ConvertRealDevDatabase (chỉ khi set env TECHZONE_CONVERT_DB_PATH).
/// </summary>
[CollectionDefinition("DbTests")]
public class DbTestsCollection { }

[Collection("DbTests")]
public class MigrationTests
{
    private readonly ITestOutputHelper _out;

    // 11 bang moi (nhom A-D, 2026-11-05) — DB cu thoi EnsureCreated khong co.
    private static readonly string[] NewTables =
    {
        "Wishlists", "ProductQuestions", "PointTransactions", "FlashSaleSlots",
        "FlashSaleSlotItems", "OrderStatusHistories", "ReturnRequests",
        "WarrantyClaims", "StockIns", "Banners", "Notifications",
        "Refunds", // Sprint 2 — Mang 4 (B-9)
        "Combos", "ComboItems", // Sprint 3 — B-7
    };

    // 15 cot moi tren bang cu (AUDIT P0-2 + IdempotencyKey cua Sprint 1)
    // + 3 cot VAT Sprint 2 — B-10 + 3 cot snapshot Flash Sale Sprint 2 — B-5
    // + 1 cot SoLuong ReturnRequests Sprint 2 — Mang 4
    // (migration chung AddSprint2Financial + AddSprint2Rollback).
    private static readonly (string Table, string Column)[] NewColumns =
    {
        ("Orders", "InstallmentMonths"), ("Orders", "DiemDaDung"),
        ("Orders", "GiamGiaDiem"), ("Orders", "GiamGiaHang"),
        ("Orders", "DaThanhToan"), ("Orders", "VatTenCongTy"),
        ("Orders", "VatMaSoThue"), ("Orders", "VatDiaChiCongTy"),
        ("Orders", "IdempotencyKey"), ("Orders", "TongVat"),
        ("OrderDetails", "ThueSuatVat"), ("OrderDetails", "TienVat"),
        ("OrderDetails", "GiaGoc"), ("OrderDetails", "TenKhuyenMai"),
        ("OrderDetails", "FlashSaleId"),
        ("ReturnRequests", "SoLuong"),
        ("Reviews", "ImageUrl"), ("Reviews", "IsApproved"),
        ("Vouchers", "BrandId"), ("Vouchers", "CategoryId"),
        ("Vouchers", "MinTier"), ("Vouchers", "FreeShip"),
        // Sprint 3 — B-7 (combo): ComboId nam trong FK -> rebuild bang Orders
        // o StripToLegacySchema (giong OrderDetails.FlashSaleId Sprint 2),
        // khong DROP COLUMN truc tiep; 2 cot kia thi DROP COLUMN duoc.
        ("Orders", "ComboId"), ("Orders", "ComboSoBo"), ("Orders", "GiamGiaCombo"),
    };

    // 28 DbSet + 7 bang Identity = 35 bang vat ly (AUDIT P1-12 + Refunds Sprint 2 + Combos Sprint 3).
    private const int ExpectedTableCount = 35;

    public MigrationTests(ITestOutputHelper output) => _out = output;

    // ============ 1. DB moi: MigrateAsync + seed ============

    [Fact]
    public async Task FreshMigrateAndSeed_HasAllTablesAndSeedData()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                Assert.Empty(ctx.Database.GetPendingMigrations());
                Assert.Contains(ctx.Database.GetAppliedMigrations(),
                    m => m.EndsWith("_InitialCreate"));
            }

            Assert.Equal(ExpectedTableCount, CountTables(db));
            Assert.True(HasBaseline(db), "Thieu dong baseline __EFMigrationsHistory");
            Assert.True(ColumnExists(db, "Orders", "IdempotencyKey"));
            Assert.True(IndexExists(db, "IX_Orders_IdempotencyKey"));

            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);

            Assert.Equal(13, CountRows(db, "Products"));
            Assert.Equal(3, CountRows(db, "Orders"));
            Assert.Equal(337, SumColumn(db, "PointTransactions", "Diem"));
            Assert.Equal(8, CountRows(db, "Categories"));
            Assert.Equal(9, CountRows(db, "Brands"));
            Assert.Equal(2, CountRows(db, "AspNetUsers"));
            Assert.Equal(4, CountRows(db, "OrderDetails"));

            // Seed chay lai lan 2: guard tung nhom -> khong nhan doi du lieu.
            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);
            Assert.Equal(13, CountRows(db, "Products"));
            Assert.Equal(3, CountRows(db, "Orders"));
            Assert.Equal(3, CountRows(db, "PointTransactions"));
            Assert.Equal(337, SumColumn(db, "PointTransactions", "Diem"));

            // Unique index IdempotencyKey: trung key -> loi; NULL thi thoai mai.
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                ctx.Orders.Add(NewOrder("KEY-TEST-1"));
                await ctx.SaveChangesAsync();
                ctx.Orders.Add(NewOrder("KEY-TEST-1"));
                await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 2. DB cu (mo phong): strip -> convert -> du lieu nguyen + boot app ============

    [Fact]
    public async Task LegacyDbConversion_EndToEnd()
    {
        var db = TempDbPath();
        try
        {
            // Dung DB day du lam goc, roi strip ve schema cu (21 bang vat ly:
            // 14 bang app cu + 7 bang Identity; 14 bang moi + 25 cot moi bi go).
            await CreateSeededDbAsync(db);
            var before = SnapshotData(db);
            StripToLegacySchema(db);
            Assert.Equal(21, CountTables(db));
            Assert.False(TableExists(db, "Wishlists"));
            Assert.False(ColumnExists(db, "Orders", "IdempotencyKey"));
            Assert.False(TableExists(db, "__EFMigrationsHistory"));

            var report = await LegacyDbConverter.ConvertAsync(db);
            _out.WriteLine($"Thieu bang: {string.Join(", ", report.MissingTables)}");
            _out.WriteLine($"Thieu cot: {string.Join(", ", report.MissingColumns)}");
            Assert.Equal(14, report.MissingTables.Count);
            Assert.DoesNotContain(report.MissingTables, t => t == "__EFMigrationsHistory");
            Assert.Contains("Orders.IdempotencyKey", report.MissingColumns);
            Assert.Contains("Vouchers.FreeShip", report.MissingColumns);
            Assert.Contains("Reviews.IsApproved", report.MissingColumns);
            Assert.True(report.BaselineInserted);
            Assert.True(File.Exists(report.BackupPath));

            // Sau convert: du bang + du lieu cu nguyen ven.
            Assert.Equal(ExpectedTableCount, CountTables(db));
            Assert.True(HasBaseline(db));
            Assert.Equal(before, SnapshotData(db));

            // MigrateAsync sau convert la no-op; seed chi them nhom thieu.
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                Assert.Empty(ctx.Database.GetPendingMigrations());
            }
            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);
            Assert.Equal(13, CountRows(db, "Products"));
            Assert.Equal(3, CountRows(db, "Orders"));
            Assert.Equal(337, SumColumn(db, "PointTransactions", "Diem"));

            // App khoi dong duoc tren DB da chuyen doi.
            await AssertAppBootsAsync(db);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 3. Rollback: khoi phuc backup ============

    [Fact]
    public async Task LegacyDbConversion_RollbackRestoresOriginalFile()
    {
        var db = TempDbPath();
        try
        {
            await CreateSeededDbAsync(db);
            StripToLegacySchema(db);
            // Chup trang thai logic truoc convert (khong so byte tuyet doi:
            // SQLite co the nhich change-counter o header khi pooled connection
            // mo/dong file, du du lieu khong doi).
            var strippedTables = ListTables(db);
            var strippedData = SnapshotData(db);

            var report = await LegacyDbConverter.ConvertAsync(db);
            Assert.Equal(ExpectedTableCount, CountTables(db));
            Assert.True(File.Exists(report.BackupPath));

            LegacyDbConverter.RestoreBackup(db, report.BackupPath);
            SqliteConnection.ClearAllPools();
            Assert.Equal(21, CountTables(db));
            Assert.Equal(strippedTables, ListTables(db));
            Assert.Equal(strippedData, SnapshotData(db));
            Assert.False(TableExists(db, "Wishlists"));
            Assert.False(HasBaseline(db));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 4. DB da khop schema: chi baseline, khong tao lai ============

    [Fact]
    public async Task ConvertAlreadyMigratedDb_IsNoopExceptBaselineCheck()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
            }
            var report = await LegacyDbConverter.ConvertAsync(db);
            // Migration chung AddSprint2Financial da dua du 6 cot Sprint 2
            // vao schema: DB vua MigrateAsync khong thieu gi nua.
            Assert.Empty(report.MissingColumns);
            Assert.Empty(report.MissingTables);
            Assert.False(report.BaselineInserted); // da co dong baseline tu MigrateAsync
            Assert.Equal(ExpectedTableCount, CountTables(db));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 5. DB dev that (chi khi set env) ============

    [Fact]
    public async Task ConvertRealDevDatabase_WhenEnvVarSet()
    {
        var path = Environment.GetEnvironmentVariable("TECHZONE_CONVERT_DB_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            _out.WriteLine("SKIPPED: chua set TECHZONE_CONVERT_DB_PATH.");
            return;
        }
        Assert.True(File.Exists(path), "Khong tim thay DB: " + path);
        var report = await LegacyDbConverter.ConvertAsync(path);
        _out.WriteLine($"Backup: {report.BackupPath}");
        _out.WriteLine($"Bang thieu: {report.MissingTables.Count}, cot thieu: {report.MissingColumns.Count}, baseline: {report.BaselineInserted}");
        Assert.Equal(ExpectedTableCount, CountTables(path));
        Assert.True(HasBaseline(path));
    }

    // ================= Helpers =================

    private static Order NewOrder(string key) => new()
    {
        TrangThai = OrderStatus.ChoXacNhan,
        DiaChiGiao = "Test, 0900000000, Test",
        PhuongThucTT = "COD",
        IdempotencyKey = key,
    };

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static async Task CreateSeededDbAsync(string dbPath)
    {
        var services = BuildServices(dbPath);
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
        }
        using (var scope = services.CreateScope())
            await SeedData.InitializeAsync(scope.ServiceProvider);
    }

    /// <summary>Mo phong DB cu: go 12 bang moi + 22 cot moi + lich su migration.</summary>
    private static void StripToLegacySchema(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=OFF";
        cmd.ExecuteNonQuery();

        // Go index nao tham chieu toi cot sap bi drop (SQLite cam DROP COLUMN
        // khi cot con nam trong index).
        var indexesToDrop = new List<string>();
        foreach (var (table, column) in NewColumns)
        {
            cmd.CommandText = $"SELECT name FROM pragma_index_list(\"{table}\")";
            var idxNames = new List<string>();
            using (var r = cmd.ExecuteReader())
                while (r.Read()) idxNames.Add(r.GetString(0)); // SELECT name -> chi 1 cot
            foreach (var idx in idxNames)
            {
                cmd.CommandText = $"SELECT name FROM pragma_index_info(\"{idx}\")";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (r.GetString(0) == column) indexesToDrop.Add(idx);
            }
        }
        foreach (var idx in indexesToDrop.Distinct())
        {
            cmd.CommandText = $"DROP INDEX IF EXISTS \"{idx}\"";
            cmd.ExecuteNonQuery();
        }
        foreach (var table in NewTables)
        {
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{table}\"";
            cmd.ExecuteNonQuery();
        }
        // SQLite khong cho DROP COLUMN cot nam trong FOREIGN KEY (Vouchers.BrandId
        // / CategoryId) -> rebuild bang Vouchers theo dung schema cu (8 cot,
        // khong FK), copy du lieu cu sang, roi tao lai unique index tren Ma.
        cmd.CommandText =
            """
            CREATE TABLE "Vouchers_legacy" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Vouchers" PRIMARY KEY AUTOINCREMENT,
                "Ma" TEXT NOT NULL,
                "LoaiGiam" TEXT NOT NULL,
                "GiaTri" decimal(18,0) NOT NULL,
                "DieuKien" decimal(18,0) NOT NULL,
                "NgayHetHan" TEXT NOT NULL,
                "SoLuong" INTEGER NOT NULL,
                "DaDung" INTEGER NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO \"Vouchers_legacy\" (\"Id\", \"Ma\", \"LoaiGiam\", \"GiaTri\", \"DieuKien\", \"NgayHetHan\", \"SoLuong\", \"DaDung\")"
            + " SELECT \"Id\", \"Ma\", \"LoaiGiam\", \"GiaTri\", \"DieuKien\", \"NgayHetHan\", \"SoLuong\", \"DaDung\" FROM \"Vouchers\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP TABLE \"Vouchers\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE \"Vouchers_legacy\" RENAME TO \"Vouchers\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE UNIQUE INDEX \"IX_Vouchers_Ma\" ON \"Vouchers\" (\"Ma\")";
        cmd.ExecuteNonQuery();
        // Tuong tu Vouchers: SQLite khong cho DROP COLUMN cot nam trong
        // FOREIGN KEY (OrderDetails.FlashSaleId -> FlashSales.Id, them o
        // Sprint 2 — B-5) -> rebuild bang OrderDetails theo dung schema cu
        // (6 cot, khong co FlashSaleId/GiaGoc/TenKhuyenMai/ThueSuatVat/
        // TienVat, khong FK FlashSale), copy du lieu cu sang.
        cmd.CommandText =
            """
            CREATE TABLE "OrderDetails_legacy" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderDetails" PRIMARY KEY AUTOINCREMENT,
                "OrderId" INTEGER NOT NULL,
                "ProductId" INTEGER NOT NULL,
                "VariantId" INTEGER NULL,
                "DonGia" decimal(18,0) NOT NULL,
                "SoLuong" INTEGER NOT NULL,
                CONSTRAINT "FK_OrderDetails_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OrderDetails_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OrderDetails_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id")
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO \"OrderDetails_legacy\" (\"Id\", \"OrderId\", \"ProductId\", \"VariantId\", \"DonGia\", \"SoLuong\")"
            + " SELECT \"Id\", \"OrderId\", \"ProductId\", \"VariantId\", \"DonGia\", \"SoLuong\" FROM \"OrderDetails\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP TABLE \"OrderDetails\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE \"OrderDetails_legacy\" RENAME TO \"OrderDetails\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX \"IX_OrderDetails_OrderId\" ON \"OrderDetails\" (\"OrderId\")";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX \"IX_OrderDetails_ProductId\" ON \"OrderDetails\" (\"ProductId\")";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX \"IX_OrderDetails_VariantId\" ON \"OrderDetails\" (\"VariantId\")";
        cmd.ExecuteNonQuery();
        // Tuong tu OrderDetails: SQLite khong cho DROP COLUMN cot nam trong
        // FOREIGN KEY (Orders.ComboId -> Combos.Id, them o Sprint 3 — B-7) ->
        // rebuild bang Orders theo dung schema cu (chi 10 cot goc thoi
        // EnsureCreated: khong co ComboId/ComboSoBo/GiamGiaCombo Sprint 3,
        // khong co IdempotencyKey/InstallmentMonths/DiemDaDung/GiamGiaDiem/
        // GiamGiaHang/DaThanhToan/Vat*/TongVat Sprint 1/2, khong FK Combos),
        // copy du lieu cu sang.
        // DDL lay tu sqlite_master cua DB da migrate (bo 13 cot moi + FK).
        cmd.CommandText =
            """
            CREATE TABLE "Orders_legacy" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY AUTOINCREMENT,
                "DiaChiGiao" TEXT NOT NULL,
                "GiamGia" decimal(18,0) NOT NULL,
                "MaVoucher" TEXT NULL,
                "NgayDat" TEXT NOT NULL,
                "PhiShip" decimal(18,0) NOT NULL,
                "PhuongThucTT" TEXT NOT NULL,
                "TongTien" decimal(18,0) NOT NULL,
                "TrangThai" TEXT NOT NULL,
                "UserId" TEXT NULL,
                CONSTRAINT "FK_Orders_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id")
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO \"Orders_legacy\" (\"Id\", \"DiaChiGiao\", \"GiamGia\", \"MaVoucher\", \"NgayDat\", \"PhiShip\", \"PhuongThucTT\", \"TongTien\", \"TrangThai\", \"UserId\")"
            + " SELECT \"Id\", \"DiaChiGiao\", \"GiamGia\", \"MaVoucher\", \"NgayDat\", \"PhiShip\", \"PhuongThucTT\", \"TongTien\", \"TrangThai\", \"UserId\" FROM \"Orders\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP TABLE \"Orders\"";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE \"Orders_legacy\" RENAME TO \"Orders\"";
        cmd.ExecuteNonQuery();
        // IX_Orders_IdempotencyKey khong tao lai: cot IdempotencyKey khong co
        // trong schema cu (giong cach vong DROP COLUMN cu xu ly).
        cmd.CommandText = "CREATE INDEX \"IX_Orders_UserId\" ON \"Orders\" (\"UserId\")";
        cmd.ExecuteNonQuery();
        // Cac cot Vouchers va OrderDetails da xu ly bang rebuild -> bo qua o vong DROP COLUMN.
        // Cot tren bang moi da bi DROP TABLE o tren (vd ReturnRequests.SoLuong)
        // cung bo qua — bang khong con ton tai.
        foreach (var (table, column) in NewColumns.Where(c => c.Table != "Vouchers" && c.Table != "OrderDetails" && c.Table != "Orders" && !NewTables.Contains(c.Table)))
        {
            cmd.CommandText = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Anh xa du lieu cu can bao toan: ten SP + ton kho, tong tien don, email user.</summary>
    private static string SnapshotData(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        string Query(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var parts = new List<string>();
            while (r.Read()) parts.Add(string.Join("|", Enumerable.Range(0, r.FieldCount)
                .Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i).ToString())));
            return string.Join(";", parts);
        }
        return "P:" + Query("SELECT Ten, TonKho FROM Products ORDER BY Id")
            + "|O:" + Query("SELECT TongTien, TrangThai FROM Orders ORDER BY Id")
            + "|U:" + Query("SELECT Email FROM AspNetUsers ORDER BY Id")
            + "|V:" + Query("SELECT Ma, DaDung FROM Vouchers ORDER BY Id");
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    private static int CountTables(string dbPath)
    {
        return ListTables(dbPath).Count;
    }

    private static List<string> ListTables(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'"
            + " AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name";
        var tables = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));
        return tables;
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @t";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (r.GetString(1) == column) return true;
        return false;
    }

    private static bool IndexExists(string dbPath, string index)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name = @i";
        cmd.Parameters.AddWithValue("@i", index);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool HasBaseline(string dbPath)
    {
        if (!TableExists(dbPath, "__EFMigrationsHistory")) return false;
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE MigrationId LIKE '%_InitialCreate'";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static int CountRows(string dbPath, string table)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SumColumn(string dbPath, string table, string column)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE(SUM(\"{column}\"), 0) FROM \"{table}\"";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string FindTechZoneProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "TechZone", "TechZone.csproj");
            if (File.Exists(candidate)) return candidate;
            var direct = Path.Combine(dir.FullName, "TechZone.csproj");
            if (File.Exists(direct)) return direct;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Khong tim thay TechZone.csproj tu " + AppContext.BaseDirectory);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Khoi dong app that (dotnet run) voi DB tam, doi trang chu + login tra 200.
    /// Dung curl (binary ngoai) de kiem tra HTTP: HttpClient cua .NET trong
    /// sandbox nay bi giu o proxy khi noi toi app local (giong kenh vstest),
    /// trong khi curl toi cung URL tra 200 binh thuong.
    /// </summary>
    private async Task AssertAppBootsAsync(string dbPath)
    {
        var port = FreePort();
        var project = FindTechZoneProject();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dotnet = Path.Combine(home, ".dotnet", "dotnet");
        if (!File.Exists(dotnet)) dotnet = "dotnet";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            ArgumentList = { "run", "--project", project, "--urls", $"http://127.0.0.1:{port}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={dbPath}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["DOTNET_ROOT"] = Path.Combine(home, ".dotnet");
        psi.Environment["PATH"] = psi.Environment["PATH"] + Path.PathSeparator + Path.Combine(home, ".dotnet");

        using var proc = Process.Start(psi)!;
        // Tieu thu stdout/stderr nen (khong await): neu khong doc, bo dem day
        // co the lam app dung. Khong await stderr o day — stderr chi EOF khi
        // app thoat, await truoc khi kill se deadlock (da gap 2026-11-05).
        _ = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(120);
            string? lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                var homeCode = await CurlStatusAsync(port, "/");
                var loginCode = homeCode == "200" ? await CurlStatusAsync(port, "/Account/Login") : null;
                _out.WriteLine($"boot poll: / -> {homeCode}, /Account/Login -> {loginCode ?? "-"}");
                if (homeCode == "200" && loginCode == "200") return;
                lastError = $"Trang chu {homeCode}, login {loginCode ?? "-"}";
                if (proc.HasExited) break;
                await Task.Delay(2000);
            }
            // Kill TRUOC khi doc stderr (tranh deadlock), roi moi lay log.
            KillProc(proc);
            var err = await WithTimeout(stderrTask, TimeSpan.FromSeconds(5)) ?? "";
            var logTail = err.Length > 2000 ? err[^2000..] : err;
            Assert.Fail("App khong khoi dong duoc tren DB da chuyen doi (port " + port + "). "
                + "Loi cuoi: " + lastError + "\nStderr:\n" + logTail);
        }
        finally
        {
            KillProc(proc);
            SqliteConnection.ClearAllPools();
        }
    }

    private static void KillProc(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        try { proc.WaitForExit(5000); } catch { /* best effort */ }
    }

    private static async Task<string?> WithTimeout(Task<string> task, TimeSpan timeout)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout));
        return done == task ? await task : null;
    }

    /// <summary>Tra HTTP status code qua curl; null = khong noi duoc.</summary>
    private static async Task<string?> CurlStatusAsync(int port, string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                ArgumentList = { "-s", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "10",
                    $"http://127.0.0.1:{port}{path}" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            var code = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 && code.Length == 3 ? code : null;
        }
        catch
        {
            return null;
        }
    }
}
