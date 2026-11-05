using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

public class OrderDetail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    /// <summary>Don gia tai thoi diem dat hang (khoa gia, sau nay doi gia SP khong anh huong don cu).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal DonGia { get; set; }

    public int SoLuong { get; set; }

    [NotMapped]
    public decimal ThanhTien => DonGia * SoLuong;
}
