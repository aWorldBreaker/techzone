using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>Hoi - dap duoi san pham (nhom A): khach dat cau hoi, Admin tra loi (nhom D).</summary>
public class ProductQuestion
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(1000)]
    public string CauHoi { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? TraLoi { get; set; }

    public string? NguoiTraLoiId { get; set; }
    public ApplicationUser? NguoiTraLoi { get; set; }

    public DateTime NgayHoi { get; set; } = DateTime.UtcNow;
    public DateTime? NgayTraLoi { get; set; }

    public bool DaTraLoi => TraLoi is not null;
}
