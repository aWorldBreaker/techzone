namespace TechZone.Models;

/// <summary>
/// Gio hang luu DB cho tai khoan da dang nhap (bang Carts + CartItems).
/// Ban demo dung gio hang session (Services/CartService) cho ca khach vang lai,
/// 2 bang nay da map san trong DbContext de giai doan sau dong bo gio session
/// vao DB khi khach dang nhap, khong phai sua schema.
/// </summary>
public class Cart
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public DateTime CapNhat { get; set; } = DateTime.UtcNow;

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
