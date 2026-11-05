using Microsoft.AspNetCore.Mvc;
using TechZone.Data;
using TechZone.Services;

namespace TechZone.ViewComponents;

/// <summary>
/// Chuong thong bao o header (nhom B): dem so thong bao chua doc cua user
/// hien tai. Khach vang lai tra 0 (an badge).
/// </summary>
public class ThongBaoBellViewComponent : ViewComponent
{
    private readonly NotificationService _thongBao;
    private readonly AppDbContext _db;

    public ThongBaoBellViewComponent(NotificationService thongBao, AppDbContext db)
    {
        _thongBao = thongBao;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var ten = HttpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(ten)) return View(0);
        var userId = _db.Users.FirstOrDefault(u => u.UserName == ten)?.Id;
        if (userId is null) return View(0);
        return View(await _thongBao.DemChuaDocAsync(userId));
    }
}
