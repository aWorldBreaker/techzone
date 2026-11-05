using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>Bien the: mau sac / dung luong. GiaThem cong them vao GiaBan.</summary>
public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(100)]
    public string TenBienThe { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaThem { get; set; }

    public int TonKho { get; set; }
}
