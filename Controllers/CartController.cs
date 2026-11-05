using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>Gio hang session (xem Services/CartService).</summary>
public class CartController : Controller
{
    private readonly CartService _cart;
    private readonly AppDbContext _db;

    public CartController(CartService cart, AppDbContext db)
    {
        _cart = cart;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await _cart.GetViewModelAsync();
        await _cart.TinhGiamGiaAsync(vm, HttpContext.Session.GetString("TechZone.Voucher"));
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int? variantId, int soLuong = 1)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        if (soLuong < 1) soLuong = 1;
        if (soLuong > product.TonKho)
        {
            TempData["Error"] = $"Chỉ còn {product.TonKho} sản phẩm trong kho.";
            return RedirectToAction("Details", "Products", new { id = productId });
        }
        _cart.Add(productId, variantId, soLuong);
        TempData["Success"] = $"Đã thêm {product.Ten} vào giỏ hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(int productId, int? variantId, int soLuong)
    {
        _cart.UpdateSoLuong(productId, variantId, soLuong);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int productId, int? variantId)
    {
        _cart.Remove(productId, variantId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApDungVoucher(string ma)
    {
        var vm = await _cart.GetViewModelAsync();
        var voucher = string.IsNullOrWhiteSpace(ma) ? null
            : await _db.Vouchers.FirstOrDefaultAsync(v => v.Ma == ma.Trim().ToUpper());
        if (voucher is null || !voucher.ConHieuLuc(DateTime.Now))
        {
            TempData["Error"] = "Mã voucher không tồn tại hoặc đã hết hạn.";
            HttpContext.Session.Remove("TechZone.Voucher");
            return RedirectToAction(nameof(Index));
        }
        if (vm.TamTinh < voucher.DieuKien)
        {
            TempData["Error"] = $"Đơn tối thiểu {voucher.DieuKien:N0}đ mới dùng được mã này.";
            return RedirectToAction(nameof(Index));
        }
        HttpContext.Session.SetString("TechZone.Voucher", voucher.Ma);
        TempData["Success"] = $"Đã áp dụng mã {voucher.Ma}.";
        return RedirectToAction(nameof(Index));
    }

}
