using System.Security.Cryptography;
using System.Text;

namespace TechZone.Services;

/// <summary>
/// Quy tac xem don hang (SEC-02, siết SEC-04): pure static de test duoc ma
/// khong can DbContext hay HttpContext (tru XacMinhToken chi doi entity).
/// - Don cua tai khoan (UserId != null): chi chu don hoac Admin.
/// - Don khach vang lai (UserId == null): chi nguoi co dung ma tra cuu
///   (GuestToken) hoac Admin. Ma don (Id) khong du — Id doan duoc.
///
/// SEC-04 — GuestToken hardening:
///  1. Sinh bang RandomNumberGenerator: 16 byte = 128 bit, hex 32 ky tu
///     (TaoGuestToken). Guid.NewGuid truoc day cung 122 bit nhung khong
///     phai nguon ngau nhien bao mat theo dung nghia — doi sang RNG.
///  2. Chi luu SHA-256 hash (hex 64 ky tu) trong DB, khong luu plaintext
///     (BamGuestToken); verify hash token nhap vao roi so sanh bang
///     CryptographicOperations.FixedTimeEquals (hang thoi gian).
///  3. Han dung 30 ngay (HanTokenNgay) ke tu ngay dat; het han -> tu choi.
///  4. Rate limit luu trong DB: 5 lan sai lien tiep (GioiHanSai) -> khoa
///     tra cuu 15 phut (KhoaPhut); nhap dung reset dem. XacMinhToken la
///     diem vao duy nhat dem sai/khoa — controller goi no roi SaveChanges.
///  5. Hang legacy: GuestToken plaintext 32 ky tu (truoc SEC-04) van verify
///     duoc (duong doc kep); lan khop dau tien se nang cap len hash ngay
///     (XacMinhToken) hoac chay GuestTokenBackfill.ChayAsync de hash hang
///     loat. SQLite khong co ham SHA-256 trong SQL nen khong the hash truc
///     tiep trong migration — migration chi them cot + an han 30 ngay cho
///     token cu (xem migration GuestTokenHardening).
/// </summary>
public static class OrderAccess
{
    /// <summary>Han dung ma tra cuu moi: 30 ngay ke tu ngay dat.</summary>
    public const int HanTokenNgay = 30;

    /// <summary>So lan nhap sai lien tiep thi khoa tra cuu tam thoi.</summary>
    public const int GioiHanSai = 5;

    /// <summary>Thoi gian khoa tra cuu sau khi vuot gioi han sai (phut).</summary>
    public const int KhoaPhut = 15;

    public static bool CoTheXemDonQ(
        Models.Order order, string? userId, bool laAdmin, string? guestToken,
        DateTime? now = null)
    {
        if (laAdmin) return true;
        if (order.UserId is not null)
            return userId is not null && userId == order.UserId;
        // Don vang lai: bat buoc khop ma tra cuu (hash + han + khoa).
        // Pure read — khong dem sai o day; dem sai chi o XacMinhToken.
        return KhopTokenQ(order, guestToken, now ?? DateTime.Now);
    }

    /// <summary>
    /// Ma tra cuu 32 ky tu hex — 16 byte tu RandomNumberGenerator (128 bit),
    /// khong doan duoc. Day la MA GOC (plaintext): chi tra ve cho khach 1 lan
    /// (sau checkout / cap lai), DB chi luu hash cua no (BamGuestToken).
    /// </summary>
    public static string TaoGuestToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>SHA-256 hash (hex 64 ky tu, chu thuong) — gia tri luu trong DB.</summary>
    public static string BamGuestToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Ma tra cuu da het han chua (null = khong han — chi hang legacy sau migrate).</summary>
    public static bool HetHanQ(Models.Order order, DateTime? now = null) =>
        order.GuestTokenHetHan.HasValue && (now ?? DateTime.Now) > order.GuestTokenHetHan.Value;

    /// <summary>Tra cuu don nay co dang bi khoa tam thoi (rate limit) khong.</summary>
    public static bool BiKhoaQ(Models.Order order, DateTime? now = null) =>
        order.GuestTokenKhoaDen.HasValue && (now ?? DateTime.Now) < order.GuestTokenKhoaDen.Value;

    /// <summary>
    /// So sanh token nhap vao voi gia tri luu (pure, khong doi entity):
    /// het han / bi khoa / thieu token / don khong co token deu false.
    /// Gia tri luu 64 ky tu = hash SHA-256 (so sanh hang thoi gian);
    /// 32 ky tu = plaintext legacy (so sanh hang thoi gian luon).
    /// </summary>
    public static bool KhopTokenQ(Models.Order order, string? guestToken, DateTime? now = null)
    {
        now ??= DateTime.Now;
        if (string.IsNullOrEmpty(guestToken)
            || string.IsNullOrEmpty(order.GuestToken)
            || HetHanQ(order, now)
            || BiKhoaQ(order, now))
            return false;
        if (order.GuestToken.Length == 64)
        {
            var hashNhap = Convert.FromHexString(BamGuestToken(guestToken));
            var hashLuu = Convert.FromHexString(order.GuestToken);
            return hashNhap.Length == hashLuu.Length
                && CryptographicOperations.FixedTimeEquals(hashNhap, hashLuu);
        }
        // Legacy plaintext (32 ky tu): so sanh hang thoi gian tren bytes UTF-8.
        var nhapBytes = Encoding.UTF8.GetBytes(guestToken);
        var luuBytes = Encoding.UTF8.GetBytes(order.GuestToken);
        return nhapBytes.Length == luuBytes.Length
            && CryptographicOperations.FixedTimeEquals(nhapBytes, luuBytes);
    }

    /// <summary>
    /// Verify token KEM rate limit — diem vao duy nhat cho Orders.Details /
    /// Orders.HoaDon (POST) / Returns.TraCuu. Doi entity (dem sai, khoa,
    /// reset, nang cap legacy len hash); caller PHAI SaveChangesAsync sau
    /// khi goi — ke ca khi tra false (dem sai/khoa phai duoc luu).
    /// - Dang khoa -> false, khong dem them.
    /// - Het han -> false, khong dem (ma het han khong phai doan sai).
    /// - Khop -> reset dem/khoa; neu luu dang plaintext legacy thi ghi de
    ///   bang hash ngay (nang cap co hoi — token cu van dung duoc).
    /// - Khong khop -> tang dem; dat 5 lan thi khoa 15 phut, reset dem ve 0
    ///   de sau khi het khoa khach co lai 5 luot moi.
    /// </summary>
    public static bool XacMinhToken(Models.Order order, string? guestToken, DateTime? now = null)
    {
        now ??= DateTime.Now;
        if (BiKhoaQ(order, now)) return false;
        if (HetHanQ(order, now)) return false;
        if (KhopTokenQ(order, guestToken, now))
        {
            GhiNhanDung(order);
            if (order.GuestToken is not null && order.GuestToken.Length == 32)
                order.GuestToken = BamGuestToken(guestToken!);
            return true;
        }
        GhiNhanSai(order, now.Value);
        return false;
    }

    /// <summary>Ghi nhan 1 lan nhap sai; dat gioi han thi khoa + reset dem.</summary>
    public static void GhiNhanSai(Models.Order order, DateTime now)
    {
        order.GuestTokenSaiLan++;
        if (order.GuestTokenSaiLan < GioiHanSai) return;
        order.GuestTokenKhoaDen = now.AddMinutes(KhoaPhut);
        order.GuestTokenSaiLan = 0;
    }

    /// <summary>Nhap dung: xoa dem sai + mo khoa (neu co).</summary>
    public static void GhiNhanDung(Models.Order order)
    {
        order.GuestTokenSaiLan = 0;
        order.GuestTokenKhoaDen = null;
    }
}
