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
    public decimal DonGia => (Product.GiaBan + (Variant?.GiaThem ?? 0));
    public decimal ThanhTien => DonGia * SoLuong;
}

public class CartViewModel
{
    public List<CartLineViewModel> Lines { get; set; } = new();
    public decimal TamTinh => Lines.Sum(l => l.ThanhTien);
    public decimal GiamGia { get; set; }
    public string? MaVoucher { get; set; }
    public decimal PhiShip => TamTinh - GiamGia >= 2_000_000 || TamTinh == 0 ? 0 : 30_000;
    public decimal TongCong => TamTinh - GiamGia + PhiShip;
}

public class CheckoutViewModel
{
    [Required, MaxLength(100)]
    public string HoTenNhan { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string SoDienThoai { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string DiaChi { get; set; } = string.Empty;

    /// <summary>COD hoac ChuyenKhoan (ca hai deu gia lap, khong thu tien that).</summary>
    public string PhuongThucTT { get; set; } = "COD";

    public string? MaVoucher { get; set; }
    public CartViewModel GioHang { get; set; } = new();
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
}
