using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>
/// San pham. Gia ban hien tai = GiaKhuyenMai neu co, khong thi Gia.
/// Thong so ky thuat (chip, RAM...) nam o bang rieng ProductSpecs (quan he 1-1),
/// khong nhet chung vao Products de sau nay them loai thong so khong phai sua bang.
/// </summary>
public class Product
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Ten { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? MoTa { get; set; }

    /// <summary>Gia goc (gia gach ngang).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal Gia { get; set; }

    /// <summary>Gia khuyen mai. Null = khong giam gia.</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal? GiaKhuyenMai { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;

    public int TonKho { get; set; }
    public int LuotBan { get; set; }
    public double DanhGiaTB { get; set; }
    public bool LaNoiBat { get; set; }
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;

    public ProductSpecs? Specs { get; set; }
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<FlashSale> FlashSales { get; set; } = new List<FlashSale>();

    /// <summary>Gia thuc te khach phai tra cho 1 cai.</summary>
    [NotMapped]
    public decimal GiaBan => GiaKhuyenMai ?? Gia;

    /// <summary>Anh dai dien: anh chinh, neu khong co thi anh placeholder.</summary>
    [NotMapped]
    public string AnhDaiDien =>
        Images.FirstOrDefault(i => i.LaAnhChinh)?.Url
        ?? Images.FirstOrDefault()?.Url
        ?? "/images/placeholder.svg";
}
