using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>Bo loc trang danh muc (dang dropdown o giao dien).</summary>
public class ProductFilterViewModel
{
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public string? Chip { get; set; }
    public string? Ram { get; set; }
    public string? DungLuong { get; set; }
    public decimal? GiaTu { get; set; }
    public decimal? GiaDen { get; set; }
    public string? TuKhoa { get; set; }

    /// <summary>mac-dinh | gia-tang | gia-giam | ban-chay | moi-nhat</summary>
    public string SapXep { get; set; } = "mac-dinh";
    public int Trang { get; set; } = 1;

    public List<Product> KetQua { get; set; } = new();
    public int TongSo { get; set; }

    /// <summary>
    /// True = trang yêu cầu (trước khi clamp) vượt quá TongTrang. View dùng
    /// cờ này để hiện thông báo phân biệt "trang không tồn tại, đã đưa về
    /// trang cuối" thay vì câu "không tìm thấy sản phẩm" (Sprint 6 Module 3).
    /// False khi không có kết quả nào (TongTrang = 0) hoặc trang hợp lệ.
    /// </summary>
    public bool TrangVuotGioiHan { get; set; }

    /// <summary>
    /// Số trang người dùng yêu cầu, ghi lại TRƯỚC khi clamp Trang về
    /// trang cuối — để view in được "Trang N không tồn tại" với đúng N
    /// (sau clamp, Trang đã là số trang cuối nên không còn N gốc).
    /// </summary>
    public int TrangYeuCau { get; set; } = 1;

    /// <summary>
    /// Giá hiệu dụng (1 đơn vị, biến thể mặc định) theo Id SP — CÙNG 1 giá
    /// với sort + lọc GiaTu/GiaDen (xem EffectivePriceCalculator). Chỉ chứa
    /// SP sau khi lọc (trước phân trang); test dùng để đối chiếu batch với
    /// TinhGiaAsync. Giá HIỂN THỊ trên card không đọc dict này mà đọc
    /// GiaBan — controller đã gán đè GiaKhuyenMai của SP trong KetQua về
    /// giá này nên GiaBan trên card đã là giá hiệu dụng.
    /// </summary>
    public Dictionary<int, decimal> GiaHieuDung { get; set; } = new();
    public List<Category> DanhMuc { get; set; } = new();
    public List<Brand> ThuongHieu { get; set; } = new();
    public List<string> Chips { get; set; } = new();
    public List<string> Rams { get; set; } = new();
    public List<string> DungLuongs { get; set; } = new();

    public const int SoMoiTrang = 12;
    public int TongTrang => (int)Math.Ceiling(TongSo / (double)SoMoiTrang);
}

public class CartLine
{
    public int ProductId { get; set; }
    public int? VariantId { get; set; }
    public int SoLuong { get; set; }
}

public class CartLineViewModel
{
    public Product Product { get; set; } = null!;
    public ProductVariant? Variant { get; set; }
    public int SoLuong { get; set; }

    /// <summary>
    /// Đơn giá thực trả (Sprint 2 — B-5): do CartService tính qua
    /// PriceCalculationService (Flash Sale / slot / giá gốc), KHÔNG tự
    /// tính từ GiaBan nữa — nếu không giỏ hiển thị một đằng, checkout
    /// thu một nẻo (AUDIT P1-2). CartService luôn gán field này.
    /// </summary>
    public decimal DonGia { get; set; }

    /// <summary>Giá gốc 1 đơn vị (chưa sale) để gạch ngang khi có sale.</summary>
    public decimal GiaGoc { get; set; }

    /// <summary>Tên khuyến mãi đang áp ("Flash Sale", ...), null = không sale.</summary>
    public string? TenKhuyenMai { get; set; }

    /// <summary>True = dòng này đang được giá sale.</summary>
    public bool LaGiaSale => DonGia < GiaGoc;

    public decimal ThanhTien => DonGia * SoLuong;
}

public class CartViewModel
{
    public List<CartLineViewModel> Lines { get; set; } = new();
    public decimal TamTinh => Lines.Sum(l => l.ThanhTien);
    public decimal GiamGia { get; set; }

    /// <summary>So tien giam do doi diem (nhom B, chi o checkout).</summary>
    public decimal GiamGiaDiem { get; set; }

    /// <summary>So tien giam them cho hang Vang / Bach kim 2% tam tinh (nhom B).
    /// P1 (2026-11-06): ap ca o gio (qua PriceCalculationService.TinhTongDonAsync),
    /// khong chi o checkout nua.</summary>
    public decimal GiamGiaHang { get; set; }

    /// <summary>So tien giam do combo (Sprint 3 — B-7, chi o checkout).</summary>
    public decimal GiamGiaCombo { get; set; }

    public string? MaVoucher { get; set; }

    /// <summary>Voucher FREESHIP hoac hang Vang: mien phi van chuyen (nhom B).</summary>
    public bool FreeShip { get; set; }

    /// <summary>Tam tinh sau moi khoan giam, khong am.</summary>
    public decimal SauGiam => Math.Max(0, TamTinh - GiamGia - GiamGiaDiem - GiamGiaHang - GiamGiaCombo);

    public decimal PhiShip => FreeShip || SauGiam >= 2_000_000 || TamTinh == 0 ? 0 : 30_000;
    public decimal TongCong => SauGiam + PhiShip;
}

public class CheckoutViewModel
{
    [Required, MaxLength(100)]
    public string HoTenNhan { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string SoDienThoai { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string DiaChi { get; set; } = string.Empty;

    /// <summary>COD / ChuyenKhoan / MoMo / ZaloPay / VNPay (vi sau la gia lap).</summary>
    public string PhuongThucTT { get; set; } = "COD";

    /// <summary>Tra gop 0% gia lap: 6 hoac 12 thang. Null = tra 1 lan (nhom A).</summary>
    public int? InstallmentMonths { get; set; }

    /// <summary>So diem tich luy muon dung de giam gia (nhom B).</summary>
    public int DiemDung { get; set; }

    /// <summary>Xuat hoa don VAT (nhom C).</summary>
    public bool XuatHoaDon { get; set; }

    [MaxLength(200)]
    public string? TenCongTy { get; set; }

    [MaxLength(20)]
    public string? MaSoThue { get; set; }

    [MaxLength(500)]
    public string? DiaChiCongTy { get; set; }

    public string? MaVoucher { get; set; }
    public CartViewModel GioHang { get; set; } = new();

    /// <summary>
    /// Key chong dat don trung (Sprint 1): GET Checkout sinh 1 GUID, form gui
    /// kem hidden field; POST trung key thi tra ve don cu thay vi tao moi.
    /// </summary>
    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }
}

public class DashboardViewModel
{
    public decimal DoanhThuThang { get; set; }
    public int DonMoi { get; set; }
    public int TongDon { get; set; }
    public int TongSanPham { get; set; }
    public List<Product> TopBanChay { get; set; } = new();
    public List<Product> SapHetHang { get; set; } = new();
    public List<Order> DonGanDay { get; set; } = new();
    public List<(string Ngay, decimal DoanhThu)> DoanhThu7Ngay { get; set; } = new();

    // --- Mo rong 2026-11-05 (nhom D) ---
    /// <summary>Doanh thu 30 ngay gan nhat (ve bieu do cot CSS).</summary>
    public List<(string Ngay, decimal DoanhThu)> DoanhThu30Ngay { get; set; } = new();
    public decimal DoanhThuTuan { get; set; }
    /// <summary>So don theo tung trang thai.</summary>
    public Dictionary<string, int> DonTheoTrangThai { get; set; } = new();
    public int KhachMoiThang { get; set; }
}
