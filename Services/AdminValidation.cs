using TechZone.Models;

namespace TechZone.Services;

/// <summary>
/// Validation server-side cho cac thao tac Admin tao/sua (Sprint 3 — Admin).
/// Ham thuan (pure): khong doc DB, khong phu thuoc HttpContext — de test truc
/// tiep bang unit test. AdminController goi cac ham nay TRUOC KHI luu, va
/// hien loi qua TempData["Error"] (khong im lang). Form HTML co min/max chi
/// la lop phu — khong bao gio tin du lieu client gui len.
/// Quy tac chung: chan so am, % giam &gt; 100, ngay het han trong qua khu,
/// ngay bat dau sau ngay ket thuc.
/// </summary>
public static class AdminValidation
{
    private static readonly string[] HangThanhVienHopLe = { "Đồng", "Bạc", "Vàng", "Bạch kim" };

    /// <summary>Kiem tra voucher moi (tao). Tra null = hop le.</summary>
    public static string? KiemTraVoucher(Voucher v)
    {
        if (v is null) return "Voucher không hợp lệ.";
        if (string.IsNullOrWhiteSpace(v.Ma)) return "Mã voucher không được trống.";
        if (v.LoaiGiam is not ("Tien" or "PhanTram"))
            return "Loại giảm phải là “Tiền” hoặc “Phần trăm”.";
        if (v.LoaiGiam == "PhanTram" && (v.GiaTri <= 0 || v.GiaTri > 100))
            return "% giảm phải lớn hơn 0 và không vượt quá 100.";
        // Giam tien co dinh: am la vo ly; = 0 chi co nghia khi la voucher
        // mien phi ship (FREESHIP seed: GiaTri = 0, FreeShip = true).
        if (v.LoaiGiam == "Tien" && (v.GiaTri < 0 || (v.GiaTri == 0 && !v.FreeShip)))
            return "Số tiền giảm phải lớn hơn 0 (voucher miễn phí ship được để 0).";
        if (v.DieuKien < 0) return "Đơn tối thiểu không được âm.";
        if (v.SoLuong <= 0) return "Số lượng mã phải lớn hơn 0.";
        if (v.DaDung < 0 || v.DaDung > v.SoLuong)
            return "Số lượt đã dùng không hợp lệ.";
        if (v.NgayHetHan.Date < DateTime.Today)
            return "Ngày hết hạn không được trong quá khứ.";
        if (v.BrandId is <= 0) return "Hãng áp dụng không hợp lệ.";
        if (v.CategoryId is <= 0) return "Danh mục áp dụng không hợp lệ.";
        if (!string.IsNullOrEmpty(v.MinTier) && !HangThanhVienHopLe.Contains(v.MinTier))
            return "Hạng thành viên tối thiểu không hợp lệ.";
        return null;
    }

    /// <summary>
    /// Kiem tra 1 mon trong khung Flash Sale. giaGoc = GiaBan cua SP
    /// (GiaKhuyenMai neu co). Gia sale phai &gt; 0 VA re hon gia ban —
    /// PriceCalculationService bo qua sale &gt;= GiaBan nen gia sale
    /// nhu vay chi la cau hinh sai, khong bao gio duoc ap.
    /// </summary>
    public static string? KiemTraSlotItem(decimal giaSale, decimal giaGoc)
    {
        if (giaSale <= 0) return "Giá sale phải lớn hơn 0.";
        if (giaSale >= giaGoc)
            return $"Giá sale ({giaSale:N0}đ) phải rẻ hơn giá bán ({giaGoc:N0}đ).";
        return null;
    }
}
