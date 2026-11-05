using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>Danh muc do cong nghe: Dien thoai, Laptop, Tablet, Tai nghe,
/// Dong ho, Phu kien, Gaming, Linh kien PC.</summary>
public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Ten { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? MoTa { get; set; }

    /// <summary>Slug dung cho URL, vd: "dien-thoai".</summary>
    [Required, MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
