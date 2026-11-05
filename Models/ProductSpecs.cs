using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Thong so ky thuat, quan he 1-1 voi Product. Cac truong de null vi khong
/// phai loai san pham nao cung co du: dong ho khong co chip dien thoai,
/// sac du phong khong co RAM/ROM, chuot khong co pin dien thoai...
/// Chi dien dung truong nao co that, khong bia thong so cho san pham khong co.
/// </summary>
public class ProductSpecs
{
    [Key]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [MaxLength(100)]
    public string? Chip { get; set; }

    [MaxLength(50)]
    public string? Ram { get; set; }

    [MaxLength(50)]
    public string? Rom { get; set; }

    [MaxLength(50)]
    public string? Pin { get; set; }

    [MaxLength(100)]
    public string? ManHinh { get; set; }

    [MaxLength(100)]
    public string? Camera { get; set; }

    [MaxLength(100)]
    public string? CardDoHoa { get; set; }

    [MaxLength(200)]
    public string? ThongSoKhac { get; set; }
}
