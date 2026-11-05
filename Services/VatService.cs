using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>Mot rule thue suat theo danh muc (cau hinh o appsettings.json, muc "Vat").</summary>
public class VatRuleConfig
{
    /// <summary>Slug cua Category, vd "dien-thoai". Khop khong phan biet hoa/thuong.</summary>
    public string CategorySlug { get; set; } = string.Empty;

    /// <summary>Thue suat, vd 0.08m = 8%.</summary>
    public decimal ThueSuat { get; set; }

    /// <summary>Ngay bat dau hieu luc (bao gom). Null = khong gioi han duoi.</summary>
    public DateTime? HieuLucTu { get; set; }

    /// <summary>Ngay het hieu luc (bao gom). Null = khong gioi han tren.</summary>
    public DateTime? HieuLucDen { get; set; }

    /// <summary>
    /// True = thue suat nay da duoc doi chieu truc tiep voi Phu luc I/II
    /// kem theo Nghi dinh (theo ma HS tung mat hang). False = chi suy ra tu
    /// Dieu 1 Nghi dinh, chua doi chieu chi tiet -> NEEDS_TAX_REVIEW.
    /// </summary>
    public bool DaXacMinh { get; set; }

    public string? GhiChu { get; set; }
}

/// <summary>Cau hinh VAT (appsettings.json, muc "Vat").</summary>
public class VatOptions
{
    /// <summary>
    /// True = gia niem yet / DonGia da BAO GOM VAT. VAT chi duoc TACH ra khoi
    /// gia (tien thue = gia * suat / (1 + suat)), khong bao gio cong them
    /// len tong tien. False = gia chua gom VAT (hien chua dung nhanh nay).
    /// </summary>
    public bool GiaDaGomVat { get; set; } = true;

    /// <summary>Thue suat ap cho SP khong co rule rieng, hoac ngoai cua so hieu luc (mac dinh 10%).</summary>
    public decimal ThueSuatMacDinh { get; set; } = 0.10m;

    public List<VatRuleConfig> Rules { get; set; } = new();
}

/// <summary>Mot dong VAT da tinh xong cua 1 OrderDetail (sau khi phan bo giam gia).</summary>
public record VatLine(OrderDetail ChiTiet, decimal ThueSuat, decimal SauGiam, decimal TruocThue, decimal TienVat);

/// <summary>Bang tinh VAT cua 1 don: cac dong tien cong lai phai ra dung TongThanhToan.</summary>
public record VatBreakdown(
    decimal TamTinhGoc,
    decimal TongGiam,
    decimal SauGiam,
    decimal TruocThue,
    decimal TongVat,
    decimal PhiShip,
    decimal TongThanhToan,
    List<VatLine> Lines)
{
    /// <summary>Tien VAT gom theo tung thue suat (vd 8%: x, 10%: y) de hien tren hoa don.</summary>
    public List<(decimal ThueSuat, decimal TienVat)> VatTheoSuat =>
        Lines.GroupBy(l => l.ThueSuat)
            .Select(g => (g.Key, g.Sum(l => l.TienVat)))
            .OrderBy(g => g.Key)
            .ToList();
}

/// <summary>
/// Tinh VAT (Sprint 2 — B-10, Nghi dinh 174/2025/ND-CP).
///
/// CAN CU PHAP LY (tra cuu 2026-11-05, nguon: luatvietnam.vn ban dich tieng Anh
/// toan van Nghi dinh 174/2025/ND-CP; doisongphapluat.com.vn; stax.com.vn):
/// - Nghi dinh 174/2025/ND-CP ngay 30/6/2025, theo Nghi quyet 204/2025/QH15.
/// - Hieu luc 01/07/2025 den het 31/12/2026.
/// - Hang hoa/dich vu dang chiu 10% duoc giam con 8% (phuong phap khau tru),
///   TRU: (a) Phu luc I — vien thong, tai chinh, ngan hang, chung khoan, bao
///   hiem, kinh doanh bat dong san, san pham kim loai, san pham khai khoang
///   (tru than); (b) Phu luc II — hang hoa/dich vu chiu thue tieu thu dac
///   biet (tru xang).
/// - CHUA XAC MINH: doi chieu tung nhom hang cong nghe cua TechZone (dien
///   thoai, laptop, tablet, tai nghe, dong ho, phu kien, gaming, linh kien PC)
///   voi tung dong cua Phu luc I/II theo ma HS — can tra cuu ma HS tung mat
///   hang truoc khi dung thue suat lam can cu ke khai thue that. Vi vay moi
///   rule 8% trong appsettings deu ghi DaXacMinh=false + NEEDS_TAX_REVIEW.
///   KHONG bao gio khang dinh thue suat cua 1 mat hang cu the khi chua doi
///   chieu Phu luc.
/// - Quy tac mac dinh an toan: khong co rule rieng / ngoai cua so hieu luc
///   -&gt; 10% (ThueSuatMacDinh).
///
/// QUY TAC TIEN:
/// - Gia niem yet cua TechZone DA GOM VAT (GiaDaGomVat = true): tien thue
///   duoc tach ra, tong tien khach tra KHONG doi. Cong them VAT len gia da
///   gom VAT la sai (tinh thue 2 lan) va bi cam o day.
/// - Cac khoan giam (voucher / doi diem / hang Vang / combo) tru vao gia da gom VAT
///   truoc, roi moi tach thue tren phan sau giam, phan bo giam gia theo ty le
///   tien hang tung dong (lam tron ve dong, bu tru phan le vao dong cuoi).
/// - Thue suat duoc SNAPSHOT vao OrderDetail.ThueSuatVat luc dat hang: doi
///   cau hinh sau nay khong lam doi so lieu don cu.
/// </summary>
public class VatService
{
    public const string GhiChuDemo =
        "Hóa đơn demo — không có giá trị pháp lý, chưa tích hợp hệ thống phát hành hóa đơn điện tử hợp pháp.";

    private static readonly Regex MaSoThueRegex = new(@"^\d{10}(-\d{3})?$", RegexOptions.Compiled);

    private readonly VatOptions _options;

    public VatService(IOptions<VatOptions> options) => _options = options.Value;

    /// <summary>Gia niem yet da gom VAT? (doc tu cau hinh; mac dinh true).</summary>
    public bool GiaDaGomVat => _options.GiaDaGomVat;

    /// <summary>Thue suat ap dung cho 1 danh muc tai ngay dat hang.</summary>
    public decimal LayThueSuat(string? categorySlug, DateTime ngayDat)
    {
        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            foreach (var rule in _options.Rules)
            {
                if (!string.Equals(rule.CategorySlug, categorySlug.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (rule.HieuLucTu.HasValue && ngayDat.Date < rule.HieuLucTu.Value.Date) continue;
                if (rule.HieuLucDen.HasValue && ngayDat.Date > rule.HieuLucDen.Value.Date) continue;
                return rule.ThueSuat;
            }
        }
        return _options.ThueSuatMacDinh;
    }

    /// <summary>
    /// Tach VAT khoi gia da gom VAT: tien thue = gia * suat / (1 + suat),
    /// lam tron ve dong (AwayFromZero); truoc thue = gia - tien thue.
    /// </summary>
    public static (decimal TruocThue, decimal TienVat) TachVat(decimal giaGomVat, decimal thueSuat)
    {
        if (giaGomVat <= 0 || thueSuat <= 0) return (Math.Max(0, giaGomVat), 0);
        var vat = Math.Round(giaGomVat * thueSuat / (1 + thueSuat), 0, MidpointRounding.AwayFromZero);
        return (giaGomVat - vat, vat);
    }

    /// <summary>
    /// Bang tinh VAT cua 1 don. Su dung snapshot OrderDetail.ThueSuatVat khi
    /// co (&gt; 0); don cu chua co snapshot thi suy lai tu danh muc + NgayDat
    /// (cung 1 ket qua neu cau hinh khong doi, nhung don cu khong bi anh
    /// huong khi cau hinh doi nho snapshot). Hang so:
    /// TamTinhGoc - TongGiam = SauGiam = TruocThue + TongVat;
    /// SauGiam + PhiShip = TongThanhToan (= Order.ThanhToan).
    /// Phi ship khong tinh VAT (phi dich vu van chuyen ngoai, hien thi rieng).
    /// </summary>
    public VatBreakdown TinhVatDon(Order order)
    {
        var chiTiet = order.ChiTiet.ToList();
        var tamTinhGoc = chiTiet.Sum(d => d.ThanhTien);
        // Sprint 4 — QA P1: GiamGiaCombo PHAI cong vao tong giam (giong
        // Order.TongGiam). Truoc day thieu no nen don combo bi tinh VAT
        // tren so tien TRUOC khi tru giam combo (tong + VAT deu cao hon
        // so tien khach tra that dung bang giam combo).
        var tongGiam = order.GiamGia + order.GiamGiaDiem + order.GiamGiaHang + order.GiamGiaCombo;
        var sauGiam = Math.Max(0, tamTinhGoc - tongGiam);
        var lines = new List<VatLine>();
        var tongVat = 0m;

        if (tamTinhGoc > 0 && sauGiam > 0)
        {
            // Phan bo phan sau giam theo ty le tien hang tung dong; dong cuoi
            // nhan phan le con lai de tong khop chinh xac sauGiam.
            var sauGiamTheoDong = new decimal[chiTiet.Count];
            var daPhanBo = 0m;
            for (var i = 0; i < chiTiet.Count; i++)
            {
                if (i == chiTiet.Count - 1)
                {
                    sauGiamTheoDong[i] = sauGiam - daPhanBo;
                }
                else
                {
                    sauGiamTheoDong[i] = Math.Round(chiTiet[i].ThanhTien * sauGiam / tamTinhGoc,
                        0, MidpointRounding.AwayFromZero);
                    daPhanBo += sauGiamTheoDong[i];
                }
            }
            for (var i = 0; i < chiTiet.Count; i++)
            {
                var detail = chiTiet[i];
                var suat = detail.ThueSuatVat > 0
                    ? detail.ThueSuatVat
                    : LayThueSuat(detail.Product?.Category?.Slug, order.NgayDat);
                var (truocThue, tienVat) = GiaDaGomVat
                    ? TachVat(sauGiamTheoDong[i], suat)
                    : (sauGiamTheoDong[i], Math.Round(sauGiamTheoDong[i] * suat, 0, MidpointRounding.AwayFromZero));
                lines.Add(new VatLine(detail, suat, sauGiamTheoDong[i], truocThue, tienVat));
                tongVat += tienVat;
            }
        }
        else
        {
            foreach (var detail in chiTiet)
            {
                var suat = detail.ThueSuatVat > 0
                    ? detail.ThueSuatVat
                    : LayThueSuat(detail.Product?.Category?.Slug, order.NgayDat);
                lines.Add(new VatLine(detail, suat, 0, 0, 0));
            }
        }

        var truocThueTong = sauGiam - tongVat;
        return new VatBreakdown(tamTinhGoc, tongGiam, sauGiam, truocThueTong, tongVat,
            order.PhiShip, sauGiam + order.PhiShip, lines);
    }

    /// <summary>
    /// Kiem tra thong tin xuat hoa don doanh nghiep. Chi goi khi khach tick
    /// "Xuat hoa don": hoa don doanh nghiep BAT BUOC du ten cong ty, ma so
    /// thue (10 so, chi nhanh them -xxx), dia chi — thieu/ sai dinh dang thi
    /// tra ve loi (khong cho dat don dang xuat VAT). Khong tick thi null.
    /// Luu y: yeu cau xuat hoa don != nghia vu thue — day chi la thong tin
    /// tren hoa don demo cua shop; hoa don that can he thong HĐĐT hop phap.
    /// </summary>
    public string? KiemTraHoaDonDoanhNghiep(bool xuatHoaDon, string? tenCongTy, string? maSoThue, string? diaChiCongTy)
    {
        if (!xuatHoaDon) return null;
        var thieu = new List<string>();
        if (string.IsNullOrWhiteSpace(tenCongTy)) thieu.Add("tên công ty");
        if (string.IsNullOrWhiteSpace(maSoThue)) thieu.Add("mã số thuế");
        if (string.IsNullOrWhiteSpace(diaChiCongTy)) thieu.Add("địa chỉ công ty");
        if (thieu.Count > 0)
            return "Xuất hóa đơn doanh nghiệp cần đủ thông tin: thiếu " + string.Join(", ", thieu) + ".";
        if (!MaSoThueRegex.IsMatch(maSoThue!.Trim()))
            return "Mã số thuế không hợp lệ (10 chữ số, chi nhánh thêm -xxx, vd 0312345678-001).";
        return null;
    }
}
