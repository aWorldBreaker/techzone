using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

public class VoucherResult
{
    public bool HopLe { get; set; }
    public string? Loi { get; set; }
    public decimal GiamGia { get; set; }
    public bool FreeShip { get; set; }

    /// <summary>
    /// Tam tinh cua cac dong DU DIEU KIEN ap voucher (khop BrandId/CategoryId).
    /// Voucher khong gioi han hang/danh muc thi bang toan bo tam tinh.
    /// Day la co so tinh % / cap giam tien co dinh (Sprint 2 — P1-1).
    /// </summary>
    public decimal TamTinhDuDieuKien { get; set; }
}

/// <summary>
/// Kiem tra voucher (nhom B). Parent se noi vao CartService.TinhGiamGiaAsync.
/// Sprint 2 — P1-1: voucher % / tien co dinh chi tinh tren phan tien cua
/// san pham DU DIEU KIEN (khop BrandId/CategoryId), khong tinh tren toan bo
/// tam tinh. Vd gio Xiaomi 15 (12.990.000d) + iPhone 15 Pro (28.990.000d)
/// voi XIAOMI10 -10% chi giam 1.299.000d, khong phai 4.198.000d.
/// Dieu kien don toi thieu (DieuKien) cung xet tren phan du dieu kien do.
/// Moi noi tinh voucher (Cart / Checkout / Order / Invoice) deu doc so
/// GiamGia da luu tu ham nay — khong tinh le te tung cho.
/// </summary>
public class VoucherService
{
    private readonly AppDbContext _db;
    private readonly LoyaltyService _loyalty;

    public VoucherService(AppDbContext db, LoyaltyService loyalty)
    {
        _db = db;
        _loyalty = loyalty;
    }

    /// <summary>Cap hang thanh vien: Dong = 0, Bac = 1, Vang = 2, Bach kim = 3.</summary>
    private static int CapHang(string hang) => hang switch
    {
        "Bạch kim" => 3,
        "Vàng" => 2,
        "Bạc" => 1,
        _ => 0
    };

    /// <summary>
    /// Dong gio co du dieu kien ap voucher khong: khop BrandId (neu co) VA
    /// CategoryId (neu co). Null = khong gioi han chieu do.
    /// </summary>
    public static bool DongDuDieuKien(Voucher voucher, CartLineViewModel line) =>
        (!voucher.BrandId.HasValue || line.Product.BrandId == voucher.BrandId.Value)
        && (!voucher.CategoryId.HasValue || line.Product.CategoryId == voucher.CategoryId.Value);

    /// <summary>Tam tinh chi cua cac dong du dieu kien (co so tinh giam, P1-1).</summary>
    public static decimal TinhTamTinhDuDieuKien(Voucher voucher, List<CartLineViewModel> lines) =>
        (lines ?? new List<CartLineViewModel>())
            .Where(l => DongDuDieuKien(voucher, l))
            .Sum(l => l.ThanhTien);

    /// <summary>
    /// Kiem tra theo thu tu: ma ton tai? ConHieuLuc? phan du dieu kien &gt;=
    /// DieuKien? co dong nao du dieu kien BrandId/CategoryId? MinTier?
    /// FreeShip thi GiamGia = 0, FreeShip = true.
    /// Giam % tinh tren phan du dieu kien; giam tien co dinh cap o phan
    /// du dieu kien (khong vuot phan tien duoc giam). Loi tieng Viet cu the.
    /// </summary>
    public async Task<VoucherResult> KiemTraAsync(string? ma, string? userId, decimal tamTinh, List<CartLineViewModel> lines)
    {
        if (string.IsNullOrWhiteSpace(ma))
            return new VoucherResult { HopLe = false, Loi = "Vui lòng nhập mã voucher." };

        var maChuan = ma.Trim().ToUpper();
        var voucher = await _db.Vouchers
            .Include(v => v.Brand)
            .Include(v => v.Category)
            .FirstOrDefaultAsync(v => v.Ma == maChuan);
        if (voucher is null)
            return new VoucherResult { HopLe = false, Loi = $"Mã {maChuan} không tồn tại." };

        if (!voucher.ConHieuLuc(DateTime.Now))
            return new VoucherResult { HopLe = false, Loi = $"Mã {voucher.Ma} đã hết hạn hoặc hết lượt dùng." };

        var linesAnToan = lines ?? new List<CartLineViewModel>();

        // Phan tien du dieu kien — co so duy nhat cho DieuKien va GiamGia (P1-1).
        var tamTinhDuDieuKien = TinhTamTinhDuDieuKien(voucher, linesAnToan);

        if (voucher.BrandId.HasValue &&
            !linesAnToan.Any(l => l.Product.BrandId == voucher.BrandId.Value))
        {
            var tenHang = voucher.Brand?.Ten ?? "hãng này";
            return new VoucherResult
            {
                HopLe = false,
                Loi = $"Mã {voucher.Ma} chỉ áp dụng cho sản phẩm hãng {tenHang}."
            };
        }

        if (voucher.CategoryId.HasValue &&
            !linesAnToan.Any(l => l.Product.CategoryId == voucher.CategoryId.Value))
        {
            var tenDanhMuc = voucher.Category?.Ten ?? "danh mục này";
            return new VoucherResult
            {
                HopLe = false,
                Loi = $"Mã {voucher.Ma} chỉ áp dụng cho sản phẩm thuộc danh mục {tenDanhMuc}."
            };
        }

        if (tamTinhDuDieuKien < voucher.DieuKien)
            return new VoucherResult
            {
                HopLe = false,
                Loi = $"Đơn tối thiểu {voucher.DieuKien:N0}đ (phần đủ điều kiện) mới dùng được mã {voucher.Ma}."
            };

        if (!string.IsNullOrEmpty(voucher.MinTier))
        {
            // User chua dang nhap coi nhu hang Dong.
            var hang = userId is null ? "Đồng" : await _loyalty.HangAsync(userId);
            if (CapHang(hang) < CapHang(voucher.MinTier))
                return new VoucherResult
                {
                    HopLe = false,
                    Loi = $"Mã {voucher.Ma} chỉ dành cho hạng {voucher.MinTier} trở lên."
                };
        }

        if (voucher.FreeShip)
            return new VoucherResult { HopLe = true, GiamGia = 0, FreeShip = true, TamTinhDuDieuKien = tamTinhDuDieuKien };

        // P1-1: % va tien co dinh chi tinh tren phan du dieu kien, cap o phan do.
        var giam = voucher.LoaiGiam == "PhanTram"
            ? Math.Round(tamTinhDuDieuKien * voucher.GiaTri / 100)
            : voucher.GiaTri;
        giam = Math.Min(giam, tamTinhDuDieuKien);
        return new VoucherResult { HopLe = true, GiamGia = giam, FreeShip = false, TamTinhDuDieuKien = tamTinhDuDieuKien };
    }

    /// <summary>
    /// Hoan 1 luot voucher khi huy don (Sprint 2 — P1-4, phia voucher).
    /// Chong hoan 2 lan / vuot SoLuong theo 2 lop guard:
    /// 1) Chi goi tu nhanh huy don cua OrderService, ma nhanh nay chi chay
    ///    khi UPDATE trang thai co dieu kien thang (1 lan duy nhat cho moi don).
    /// 2) UPDATE co dieu kien AND DaDung &gt; 0: khong bao gio giam xuong am;
    ///    0 dong bi anh huong = voucher da xoa hoac DaDung da ve 0.
    /// Tra true neu da hoan 1 luot, false neu khong hoan duoc (ma rong,
    /// voucher khong ton tai, hoac DaDung da = 0).
    /// Ham nay chi hoan LUOT DUNG; thu hoi diem Earn / tra diem Spend khi
    /// huy don la phan cua Mang 4 (goi cung cho trong OrderService).
    /// </summary>
    public async Task<bool> HoanLuotAsync(string? ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return false;
        var soDong = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Vouchers\" SET \"DaDung\" = \"DaDung\" - 1 "
            + "WHERE \"Ma\" = {0} AND \"DaDung\" > 0",
            ma.Trim().ToUpper());
        return soDong > 0;
    }
}
