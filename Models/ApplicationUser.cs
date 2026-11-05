using Microsoft.AspNetCore.Identity;

namespace TechZone.Models;

/// <summary>Tai khoan nguoi dung (Identity). Role: "Admin" hoac "Khach".</summary>
public class ApplicationUser : IdentityUser
{
    public string HoTen { get; set; } = string.Empty;
    public string? SoDienThoai { get; set; }
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public Cart? Cart { get; set; }
}
