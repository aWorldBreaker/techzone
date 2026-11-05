namespace TechZone.Models;

/// <summary>San pham yeu thich cua 1 tai khoan (nhom A). 1 user + 1 SP chi 1 dong.</summary>
public class Wishlist
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
