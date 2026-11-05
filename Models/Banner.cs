using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Banner trang chu do Admin quan ly (nhom D). Chi banner IsActive
/// moi hien, sap xep theo ThuTu.
/// </summary>
public class Banner
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string TieuDe { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string AnhUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public int ThuTu { get; set; }
}
