using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>Thong bao in-app + nhac hen Flash Sale (nhom B).</summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly NotificationService _thongBao;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public NotificationsController(NotificationService thongBao, UserManager<ApplicationUser> userManager, AppDbContext db)
    {
        _thongBao = thongBao;
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var ds = await _thongBao.DanhSachAsync(user.Id);
        return View(ds);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DanhDauDaDoc()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        await _thongBao.DanhDauDaDocAsync(user.Id);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Dat nhac khung Flash Sale: tao 1 thong bao cho user hien tai.
    /// Slot khong co thi NotFound. Id slot lay tu form POST.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NhacSlot(int slotId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var slot = await _db.FlashSaleSlots.FindAsync(slotId);
        if (slot is null) return NotFound();
        await _thongBao.TaoThongBaoAsync(
            user.Id,
            $"Đã đặt nhắc Flash Sale {slot.Ten}",
            $"Bạn sẽ được nhắc khi khung Flash Sale {slot.Ten} bắt đầu.");
        TempData["Success"] = $"Đã đặt nhắc Flash Sale {slot.Ten}.";
        return RedirectToAction("Index", "Home");
    }
}
