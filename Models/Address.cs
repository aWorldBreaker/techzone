using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

public class Address
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required, MaxLength(100)]
    public string HoTenNhan { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string SoDienThoai { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string DiaChi { get; set; } = string.Empty;

    public bool MacDinh { get; set; }
}
