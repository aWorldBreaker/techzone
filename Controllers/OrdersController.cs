using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>
/// Dat hang COD / chuyen khoan GIA LAP (khong thu tien that).
/// Phi ship: mien phi don tu 2.000.000d sau giam, khong thi 30.000d.
/// </summary>
public class OrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly CartService _cart;

    public OrdersController(AppDbContext db, CartService cart)
    {
        _db = db;
        _cart = cart;
    }

    public async Task<IActionResult> Checkout()
    {
        var vm = new CheckoutViewModel { GioHang = await _cart.GetViewModelAsync() };
        if (!vm.GioHang.Lines.Any())
        {
            TempData["Error"] = "Giỏ hàng đang trống.";
            return RedirectToAction("Index", "Cart");
        }
        await _cart.TinhGiamGiaAsync(vm.GioHang, HttpContext.Session.GetString("TechZone.Voucher"));
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        var gioHang = await _cart.GetViewModelAsync();
        if (!gioHang.Lines.Any())
        {
            TempData["Error"] = "Giỏ hàng đang trống.";
            return RedirectToAction("Index", "Cart");
        }
        if (!ModelState.IsValid)
        {
            model.GioHang = gioHang;
            return View(model);
        }

        await _cart.TinhGiamGiaAsync(gioHang, HttpContext.Session.GetString("TechZone.Voucher"));

        // Kiem tra ton kho truoc khi tao don
        foreach (var line in gioHang.Lines)
        {
            if (line.SoLuong > line.Product.TonKho)
            {
                TempData["Error"] = $"{line.Product.Ten} chỉ còn {line.Product.TonKho} trong kho.";
                return RedirectToAction("Index", "Cart");
            }
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;

        var order = new Order
        {
            UserId = userId,
            TrangThai = OrderStatus.ChoXacNhan,
            DiaChiGiao = $"{model.HoTenNhan}, {model.SoDienThoai}, {model.DiaChi}",
            PhuongThucTT = model.PhuongThucTT,
            NgayDat = DateTime.Now,
            MaVoucher = gioHang.MaVoucher,
            GiamGia = gioHang.GiamGia,
            PhiShip = gioHang.PhiShip,
            TongTien = gioHang.TamTinh - gioHang.GiamGia,
        };
        foreach (var line in gioHang.Lines)
        {
            order.ChiTiet.Add(new OrderDetail
            {
                ProductId = line.Product.Id,
                VariantId = line.Variant?.Id,
                DonGia = line.DonGia,
                SoLuong = line.SoLuong
            });
            // Tru ton kho + tang luot ban
            var product = await _db.Products.FindAsync(line.Product.Id);
            if (product is not null)
            {
                product.TonKho -= line.SoLuong;
                product.LuotBan += line.SoLuong;
            }
        }
        _db.Orders.Add(order);

        if (!string.IsNullOrEmpty(gioHang.MaVoucher))
        {
            var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Ma == gioHang.MaVoucher);
            if (voucher is not null) voucher.DaDung += 1;
        }

        await _db.SaveChangesAsync();
        _cart.Clear();
        HttpContext.Session.Remove("TechZone.Voucher");
        TempData["Success"] = $"Đặt hàng thành công. Mã đơn: #{order.Id}.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [Authorize]
    public async Task<IActionResult> MyOrders()
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var orders = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.NgayDat)
            .ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Don cua khach vang lai: ai co ma don thi xem duoc (demo).
        // Don cua tai khoan: chi chu don hoac Admin moi xem.
        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        if (order.UserId is not null && order.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();
        return View(order);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Huy(int id)
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var order = await _db.Orders
            .Include(o => o.ChiTiet)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return NotFound();
        if (order.TrangThai != OrderStatus.ChoXacNhan)
        {
            TempData["Error"] = "Chỉ hủy được đơn đang chờ xác nhận.";
            return RedirectToAction(nameof(MyOrders));
        }
        order.TrangThai = OrderStatus.DaHuy;
        foreach (var detail in order.ChiTiet)
        {
            var product = await _db.Products.FindAsync(detail.ProductId);
            if (product is not null) product.TonKho += detail.SoLuong;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã hủy đơn #{id}, tồn kho đã hoàn lại.";
        return RedirectToAction(nameof(MyOrders));
    }
}
