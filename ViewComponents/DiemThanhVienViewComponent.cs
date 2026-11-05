using Microsoft.AspNetCore.Mvc;
using TechZone.Data;
using TechZone.Services;

namespace TechZone.ViewComponents;

/// <summary>
/// Diem tich luy + hang thanh vien o header (nhom B): hien cho user da
/// dang nhap, link toi trang tai khoan (Account/Profile).
/// </summary>
public class DiemThanhVienViewComponent : ViewComponent
{
    private readonly LoyaltyService _loyalty;
    private readonly AppDbContext _db;

    public DiemThanhVienViewComponent(LoyaltyService loyalty, AppDbContext db)
    {
        _loyalty = loyalty;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var ten = HttpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(ten)) return View((0, "Đồng"));
        var userId = _db.Users.FirstOrDefault(u => u.UserName == ten)?.Id;
        if (userId is null) return View((0, "Đồng"));
        var diem = await _loyalty.TongDiemAsync(userId);
        var hang = await _loyalty.HangAsync(userId);
        return View((diem, hang));
    }
}
