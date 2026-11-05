using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Yeu cau bao hanh online (nhom C). Bao hanh 24 thang tu ngay mua.
/// TrangThai: "Chờ duyệt" / "Đã duyệt" / "Từ chối" / "Hoàn thành".
/// </summary>
public class WarrantyClaim
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(1000)]
    public string MoTaLoi { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string TrangThai { get; set; } = "Chờ duyệt";

    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
