using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>
/// Nguong hang thanh vien (appsettings.json, muc "Loyalty"). Gia tri mac
/// dinh khop hang so Moc* ben duoi — doi appsettings la doi nguong ma
/// khong can sua code. PlatinumThreshold la muc >= de dat (bao gom).
/// </summary>
public class LoyaltyOptions
{
    public decimal SilverThreshold { get; set; } = 5_000_000m;
    public decimal GoldThreshold { get; set; } = 20_000_000m;
    public decimal PlatinumThreshold { get; set; } = 50_000_000m;
}

/// <summary>
/// Diem tich luy va hang thanh vien (nhom B).
/// Hang theo tong chi tieu hop le: Dong &lt; 5tr, Bac 5-20tr, Vang &gt; 20tr,
/// Bach kim &gt;= 50tr (B-6, Sprint 3). "Hop le" = don khong huy, tru so
/// tien da hoan (RefundRecord) theo tung don, kep >= 0 — hoan 1 phan chi
/// tru phan hoan, hoan het (ke ca phi ship) thi don dong gop 0.
/// Mua hang tich 1% gia tri don: Diem = TongTien / 100.000, lam tron xuong.
/// 1 diem = 1.000d khi doi.
/// </summary>
public class LoyaltyService
{
    public const decimal MocBac = 5_000_000m;
    public const decimal MocVang = 20_000_000m;

    /// <summary>Moc Bach kim (B-6): dat khi tong chi tieu hop le &gt;= 50tr (bao gom).</summary>
    public const decimal MocBachKim = 50_000_000m;

    private readonly AppDbContext _db;

    /// <summary>Nguong hieu luc (tu cau hinh, mac dinh = hang so Moc*).</summary>
    public decimal NguongBac { get; }
    public decimal NguongVang { get; }
    public decimal NguongBachKim { get; }

    /// <summary>
    /// options la optional: test / noi dung khong dang ky IOptions thi
    /// dung nguong mac dinh; app chinh dang ky qua Program.cs.
    /// </summary>
    public LoyaltyService(AppDbContext db, IOptions<LoyaltyOptions>? options = null)
    {
        _db = db;
        var o = options?.Value;
        NguongBac = o?.SilverThreshold ?? MocBac;
        NguongVang = o?.GoldThreshold ?? MocVang;
        NguongBachKim = o?.PlatinumThreshold ?? MocBachKim;
    }

    /// <summary>
    /// Hang tu tong chi tieu (nguong mac dinh): "Bạch kim" neu &gt;= 50tr,
    /// "Vàng" neu &gt; 20tr, "Bạc" neu &gt;= 5tr, con lai "Đồng".
    /// Giu bien 20tr strict (&gt;) nhu cu: dung 20.000.000d = Bac.
    /// </summary>
    public static string HangTuChiTieu(decimal tong) =>
        tong >= MocBachKim ? "Bạch kim"
        : tong > MocVang ? "Vàng"
        : tong >= MocBac ? "Bạc" : "Đồng";

    /// <summary>Hang theo nguong cau hinh hieu luc (cung thu tu / bien nhu ban static).</summary>
    public string HangTuChiTieuCauHinh(decimal tong) =>
        tong >= NguongBachKim ? "Bạch kim"
        : tong > NguongVang ? "Vàng"
        : tong >= NguongBac ? "Bạc" : "Đồng";

    /// <summary>
    /// Tong chi tieu HOP LE cua user = tong TongTien cac don khong huy,
    /// tru so tien da hoan (Refunds) theo tung don, kep moi don &gt;= 0.
    /// - Don DaHuy loai han (ke ca khi da co dong hoan do huy).
    /// - Don doi tra duoc duyet: trang thai van DaGiao nhung co RefundRecord
    ///   (hoan 1 phan theo ti le, hoan het + ca PhiShip) nen chi tru phan
    ///   hoan that, khong loai ca don.
    /// - Don ChoXacNhan van tinh (hanh vi cu, test demo 33.950.000d phu
    ///   thuoc vao day): don cho van la don dang co hieu luc, huy sau se
    ///   tu roi khoi tong; chi loai khi that su DaHuy / da hoan.
    /// - Spend diem khong anh huong: tong tinh tu don hang, khong suy tu
    ///   diem (diem da tieu/hoan thi sai).
    /// SQLite khong dich duoc Sum tren decimal (NotSupportedException) nen
    /// lay danh sach ve roi cong o client — ket qua decimal chinh xac.
    /// </summary>
    public async Task<decimal> TongChiTieuAsync(string userId)
    {
        var donHopLe = await _db.Orders
            .Where(o => o.UserId == userId && o.TrangThai != OrderStatus.DaHuy)
            .Select(o => new { o.Id, o.TongTien })
            .ToListAsync();
        if (donHopLe.Count == 0) return 0;
        var ids = donHopLe.Select(o => o.Id).ToList();
        var hoanTheoDon = (await _db.Refunds
                .Where(r => ids.Contains(r.OrderId))
                .Select(r => new { r.OrderId, r.SoTien })
                .ToListAsync())
            .GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.SoTien));
        decimal tong = 0;
        foreach (var don in donHopLe)
        {
            var daHoan = hoanTheoDon.TryGetValue(don.Id, out var h) ? h : 0;
            var net = don.TongTien - daHoan;
            if (net > 0) tong += net;
        }
        return tong;
    }

    public async Task<string> HangAsync(string userId) =>
        HangTuChiTieuCauHinh(await TongChiTieuAsync(userId));

    /// <summary>
    /// Tong diem hien co = (Earn + SpendHoan) - (Spend + EarnHoan).
    /// Sprint 2 — Mang 4 (B-9): "EarnHoan" = diem Earn bi thu hoi khi huy /
    /// doi tra duoc duyet; "SpendHoan" = diem Spend duoc tra lai cho don do.
    /// Khong co dong hoan tac thi cong thuc nay giong het cu (Earn - Spend).
    /// </summary>
    public async Task<int> TongDiemAsync(string userId)
    {
        var cong = await _db.PointTransactions
            .Where(p => p.UserId == userId && (p.Loai == "Earn" || p.Loai == "SpendHoan"))
            .SumAsync(p => p.Diem);
        var tru = await _db.PointTransactions
            .Where(p => p.UserId == userId && (p.Loai == "Spend" || p.Loai == "EarnHoan"))
            .SumAsync(p => p.Diem);
        return cong - tru;
    }

    /// <summary>Tong diem Earn da ghi cho 1 don (0 = don khong du diem tich, vd TongTien &lt; 100.000d).</summary>
    public Task<int> EarnCuaDonAsync(int orderId) =>
        _db.PointTransactions
            .Where(p => p.Loai == "Earn" && p.OrderId == orderId)
            .SumAsync(p => p.Diem);

    /// <summary>Tong diem Spend da tru cho 1 don (0 = don khong dung diem).</summary>
    public Task<int> SpendCuaDonAsync(int orderId) =>
        _db.PointTransactions
            .Where(p => p.Loai == "Spend" && p.OrderId == orderId)
            .SumAsync(p => p.Diem);

    /// <summary>
    /// Thu hoi diem Earn cua 1 don khi huy / doi tra duoc duyet (Sprint 2 —
    /// Mang 4, B-9): ghi 1 dong "EarnHoan" (Diem &gt; 0, tru vao so du) co
    /// lien ket OrderId de truy vet. Idempotent + khong thu qua so Earn that:
    /// INSERT ... SELECT co dieu kien trong 1 cau lenh — chi chen khi tong
    /// EarnHoan da ghi cho don + soDiem &lt;= tong Earn cua don. Tra true neu
    /// vua chen, false neu khong thu duoc gi (het Earn / da thu du).
    /// Luu y: thu FULL so Earn ke ca khi khach da tieu het diem do — so du
    /// co the am tam (no diem, don mua sau bu lai); khong cat bot am tham
    /// vi se mat dau vet doi soat. TruDiemAsync chan tieu diem khi so du am.
    /// </summary>
    public async Task<bool> ThuHoiEarnAsync(string userId, int orderId, int soDiem)
    {
        if (soDiem <= 0) return false;
        var soDong = await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"PointTransactions\" (\"UserId\", \"Diem\", \"Loai\", \"OrderId\", \"NgayTao\") "
            + "SELECT {0}, {1}, 'EarnHoan', {2}, {3} "
            + "WHERE (SELECT COALESCE(SUM(\"Diem\"), 0) FROM \"PointTransactions\" "
            + "WHERE \"Loai\" = 'EarnHoan' AND \"OrderId\" = {2}) + {1} "
            + "<= (SELECT COALESCE(SUM(\"Diem\"), 0) FROM \"PointTransactions\" "
            + "WHERE \"Loai\" = 'Earn' AND \"OrderId\" = {2})",
            userId, soDiem, orderId, DateTime.UtcNow);
        return soDong > 0;
    }

    /// <summary>
    /// Tra lai diem Spend cua 1 don khi huy / doi tra duoc duyet (Sprint 2 —
    /// Mang 4, B-9): ghi 1 dong "SpendHoan" (Diem &gt; 0, cong vao so du) co
    /// lien ket OrderId. Guard giong ThuHoiEarnAsync: tong SpendHoan khong
    /// bao gio vuot tong Spend that cua don; goi 2 lan chi chen 1 lan.
    /// </summary>
    public async Task<bool> HoanSpendAsync(string userId, int orderId, int soDiem)
    {
        if (soDiem <= 0) return false;
        var soDong = await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"PointTransactions\" (\"UserId\", \"Diem\", \"Loai\", \"OrderId\", \"NgayTao\") "
            + "SELECT {0}, {1}, 'SpendHoan', {2}, {3} "
            + "WHERE (SELECT COALESCE(SUM(\"Diem\"), 0) FROM \"PointTransactions\" "
            + "WHERE \"Loai\" = 'SpendHoan' AND \"OrderId\" = {2}) + {1} "
            + "<= (SELECT COALESCE(SUM(\"Diem\"), 0) FROM \"PointTransactions\" "
            + "WHERE \"Loai\" = 'Spend' AND \"OrderId\" = {2})",
            userId, soDiem, orderId, DateTime.UtcNow);
        return soDong > 0;
    }

    /// <summary>
    /// Tich diem khi mua hang: Earn = (int)(TongTien / 100.000).
    /// Chi ghi khi diem &gt; 0 va Order.UserId != null.
    /// Guard chong ghi trung theo OrderId (Sprint 1): goi lai lan 2 (vd replay
    /// idempotency, retry sau loi khoa DB) khong tao them dong Earn.
    /// Luu y: ham nay goi SaveChangesAsync, nhung khi duoc goi trong mot
    /// transaction tuong minh (checkout) thi SaveChanges chi ghi vao
    /// transaction do, khong commit rieng.
    /// </summary>
    public async Task GhiDiemMuaHangAsync(Order order)
    {
        if (order.UserId is null) return;
        var diem = (int)(order.TongTien / 100_000m);
        if (diem <= 0) return;
        var daGhi = await _db.PointTransactions
            .AnyAsync(p => p.Loai == "Earn" && p.OrderId == order.Id);
        if (daGhi) return;
        _db.PointTransactions.Add(new PointTransaction
        {
            UserId = order.UserId,
            Diem = diem,
            Loai = "Earn",
            OrderId = order.Id,
            NgayTao = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Tru diem an toan race (Sprint 2 — Mang 5): ghi 1 dong Spend bang
    /// INSERT ... SELECT co dieu kien trong 1 cau lenh duy nhat — chi chen
    /// khi so du hien tai (SUM Earn - SUM Spend) &gt;= so diem can tru, va
    /// chua co dong Spend nao cho don nay. 0 dong = het diem hoac da tru
    /// roi. Khong co khoang check-then-act giua 2 checkout dong thoi:
    /// SQLite chi cho 1 writer tai 1 thoi diem nen cau lenh thu 2 thay
    /// so du da tru va tu choi. Goi trong transaction checkout: SaveChanges
    /// khong commit rieng; rollback cua checkout se huy ca dong Spend.
    /// Tra (true, null) khi tru thanh cong HOAC da tru truoc do cho cung
    /// don (idempotent, vd replay); (false, Loi) khi khong du diem.
    /// </summary>
    public async Task<(bool ThanhCong, string? Loi)> TruDiemAsync(string userId, int soDiem, int orderId)
    {
        if (soDiem <= 0)
            return (false, "Số điểm dùng phải lớn hơn 0.");
        var soDong = await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"PointTransactions\" (\"UserId\", \"Diem\", \"Loai\", \"OrderId\", \"NgayTao\") "
            + "SELECT {0}, {1}, 'Spend', {2}, {3} "
            + "WHERE (SELECT COALESCE(SUM(CASE WHEN \"Loai\" IN ('Earn', 'SpendHoan') THEN \"Diem\" ELSE -\"Diem\" END), 0) "
            + "FROM \"PointTransactions\" WHERE \"UserId\" = {0}) >= {1} "
            + "AND NOT EXISTS (SELECT 1 FROM \"PointTransactions\" "
            + "WHERE \"Loai\" = 'Spend' AND \"OrderId\" = {2})",
            userId, soDiem, orderId, DateTime.UtcNow);
        if (soDong > 0) return (true, null);

        // 0 dong: phan biet da tru roi (idempotent) voi het diem that.
        var daTru = await _db.PointTransactions
            .AnyAsync(p => p.Loai == "Spend" && p.OrderId == orderId);
        if (daTru) return (true, null);
        var soDu = await TongDiemAsync(userId);
        return (false, soDu <= 0
            ? "Bạn đã hết điểm tích lũy, vui lòng kiểm tra lại."
            : $"Bạn chỉ có {soDu:N0} điểm.");
    }

    /// <summary>
    /// Doi diem giam gia: Giam = diem * 1.000, cap o tamTinh.
    /// Tra Loi tieng Viet khi diem khong hop le, Giam = 0 khi do.
    /// </summary>
    public async Task<(decimal Giam, string? Loi)> DungDiemAsync(string userId, int diemMuonDung, decimal tamTinh)
    {
        if (diemMuonDung <= 0)
            return (0, "Số điểm dùng phải lớn hơn 0.");
        var tongDiem = await TongDiemAsync(userId);
        if (diemMuonDung > tongDiem)
            return (0, $"Bạn chỉ có {tongDiem:N0} điểm.");
        var giam = Math.Min(diemMuonDung * 1_000m, tamTinh);
        return (giam, null);
    }

    /// <summary>
    /// Hang Vang tro len (Vang, Bach kim) giam them 2% tam tinh (lam tron),
    /// hang khac giam 0. Bach kim giu nguyen quyen loi Vang, khong mat
    /// giam gia / free ship khi vuot moc 50tr.
    /// </summary>
    public async Task<decimal> GiamGiaHangAsync(string userId, decimal tamTinh)
    {
        if (tamTinh <= 0) return 0;
        var hang = await HangAsync(userId);
        return hang is "Vàng" or "Bạch kim" ? Math.Round(tamTinh * 2 / 100) : 0;
    }

    /// <summary>Hang Vang tro len (Vang, Bach kim): duoc giam 2% + free ship.</summary>
    public async Task<bool> LaHangVangAsync(string userId) =>
        await HangAsync(userId) is "Vàng" or "Bạch kim";

    /// <summary>Lich su diem gan nhat (moi nhat truoc). Dung cho trang Profile.</summary>
    public async Task<List<PointTransaction>> LichSuAsync(string userId, int take = 10) =>
        await _db.PointTransactions
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.NgayTao)
            .Take(take)
            .ToListAsync();
}
