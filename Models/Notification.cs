using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Thong bao in-app cho khach (nhom B/D): don hang, flash sale, nhac hen.
/// Chuong o header dem so DaDoc = false.
/// </summary>
public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string TieuDe { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? NoiDung { get; set; }

    public bool DaDoc { get; set; }
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
