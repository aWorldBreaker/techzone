using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Controllers;

/// <summary>
/// San pham yeu thich cua tai khoan dang nhap (nhom A).
/// Index/Remove yeu cau dang nhap; Add tu kiem tra de dua ve trang
/// dang nhap kem returnUrl ve trang chi tiet san pham.
/// </summary>
public class WishlistController : Controller
{
    private readonly AppDbContext _db;

    public WishlistController(AppDbContext db) => _db = db;

    private string? LayUserId() =>
        User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;

    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userId = LayUserId();
        var list = await _db.Wishlists
            .Include(w => w.Product).ThenInclude(p => p.Images)
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.NgayTao)
            .ToListAsync();
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();

        var userId = LayUserId();
        if (userId is null)
            return RedirectToAction("Login", "Account",
                new { returnUrl = Url.Action("Details", "Products", new { id = productId }) });

        var daCo = await _db.Wishlists
            .AnyAsync(w => w.UserId == userId && w.ProductId == productId);
        if (daCo)
        {
            TempData["Error"] = $"{product.Ten} đã có trong yêu thích.";
            return RedirectToAction("Details", "Products", new { id = productId });
        }

        _db.Wishlists.Add(new Wishlist { UserId = userId, ProductId = productId });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm {product.Ten} vào yêu thích.";
        return RedirectToAction("Details", "Products", new { id = productId });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId)
    {
        var userId = LayUserId();
        var dong = await _db.Wishlists
            .Include(w => w.Product)
            .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);
        if (dong is not null)
        {
            var ten = dong.Product.Ten;
            _db.Wishlists.Remove(dong);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã bỏ {ten} khỏi yêu thích.";
        }
        return RedirectToAction(nameof(Index));
    }
}
