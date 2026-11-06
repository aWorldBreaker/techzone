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
    private readonly VoucherService _voucher;

    public CartController(CartService cart, AppDbContext db, VoucherService voucher)
    {
        _cart = cart;
        _db = db;
        _voucher = voucher;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await _cart.GetViewModelAsync();
        // P1 (2026-11-06): giỏ áp đủ ưu đãi như checkout (voucher + combo +
        // giảm hạng) qua cùng 1 hàm tính tổng — trước đây chỉ có voucher nên
        // khách Vàng thấy tổng giỏ cao hơn checkout 2% tạm tính.
        var ketQua = await _cart.ApDungUuDaiDayDuAsync(vm,
            HttpContext.Session.GetString("TechZone.Voucher"),
            int.TryParse(HttpContext.Session.GetString("TechZone.ComboId"), out var comboId)
                ? comboId : null);
        ViewBag.Hang = ketQua.Hang;
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

    /// <summary>
    /// Mua combo 1 lan (Sprint 3 — B-7): neu co comboId thi them TAT CA mon
    /// cua combo theo so luong moi mon (SoLuong mon x so bo) va luu comboId
    /// vao session de checkout biet ap combo nao; khong co comboId thi giu
    /// hanh vi cu (them cap 2 SP). Kiem tra ton kho tung mon truoc khi them;
    /// checkout se kiem tra lai day du (han/tat/suat/gia) tu DB.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCombo(int productId, int? comboProductId, int? comboId, int soLuong = 1)
    {
        if (soLuong < 1) soLuong = 1;
        if (comboId.HasValue)
        {
            var combo = await _db.Combos
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == comboId.Value);
            if (combo is null) return NotFound();
            if (!combo.IsActive || DateTime.Now < combo.BatDau || DateTime.Now > combo.KetThuc)
            {
                TempData["Error"] = "Combo này đã hết hạn hoặc đã tắt.";
                return RedirectToAction("Details", "Products", new { id = productId });
            }
            foreach (var item in combo.Items)
            {
                var sp = await _db.Products.FindAsync(item.ProductId);
                if (sp is null) return NotFound();
                if (item.SoLuong * soLuong > sp.TonKho)
                {
                    TempData["Error"] = $"Combo đã hết hàng: {sp.Ten} chỉ còn {sp.TonKho}.";
                    return RedirectToAction("Details", "Products", new { id = productId });
                }
            }
            foreach (var item in combo.Items)
                _cart.Add(item.ProductId, null, item.SoLuong * soLuong);
            HttpContext.Session.SetString("TechZone.ComboId", combo.Id.ToString());
            TempData["Success"] = $"Đã thêm combo {combo.Ten} vào giỏ hàng.";
            return RedirectToAction(nameof(Index));
        }
        if (comboProductId is null) return NotFound();
        var chinh = await _db.Products.FindAsync(productId);
        var kem = await _db.Products.FindAsync(comboProductId.Value);
        if (chinh is null || kem is null) return NotFound();
        if (soLuong > chinh.TonKho || soLuong > kem.TonKho)
        {
            TempData["Error"] = "Combo đã hết hàng một trong hai món.";
            return RedirectToAction("Details", "Products", new { id = productId });
        }
        _cart.Add(productId, null, soLuong);
        _cart.Add(comboProductId.Value, null, soLuong);
        TempData["Success"] = $"Đã thêm combo {chinh.Ten} + {kem.Ten} vào giỏ hàng.";
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

    /// <summary>
    /// Ap dung voucher qua VoucherService (nhom B) de bao ro ly do khi khong
    /// ap duoc: het han, het luot, chua du don toi thieu, sai hang/danh muc/hang.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApDungVoucher(string ma)
    {
        var vm = await _cart.GetViewModelAsync();
        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        var ketQua = await _voucher.KiemTraAsync(ma, userId, vm.TamTinh, vm.Lines);
        if (!ketQua.HopLe)
        {
            TempData["Error"] = ketQua.Loi ?? "Mã voucher không hợp lệ.";
            HttpContext.Session.Remove("TechZone.Voucher");
            return RedirectToAction(nameof(Index));
        }
        var maChuan = ma.Trim().ToUpper();
        HttpContext.Session.SetString("TechZone.Voucher", maChuan);
        TempData["Success"] = ketQua.FreeShip
            ? $"Đã áp dụng mã {maChuan} (miễn phí vận chuyển)."
            : $"Đã áp dụng mã {maChuan}.";
        return RedirectToAction(nameof(Index));
    }

}
