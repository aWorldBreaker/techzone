using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>Anh san pham. Url la duong dan local /images/... (anh that cua hang).
/// Khong dung anh AI render lam anh san pham.</summary>
public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public bool LaAnhChinh { get; set; }
    public int ThuTu { get; set; }
}
