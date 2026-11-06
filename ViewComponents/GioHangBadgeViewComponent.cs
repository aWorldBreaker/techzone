using Microsoft.AspNetCore.Mvc;
using TechZone.Services;

namespace TechZone.ViewComponents;

/// <summary>
/// Link "Giỏ hàng" + badge số lượng ở header (Sprint 6 Module 1): đếm tổng
/// số lượng từ CartService/session, giống cách chuông lấy từ
/// NotificationService — chỉ hiển thị, không đổi logic giỏ. Giỏ trống
/// (0) thì không render badge.
/// </summary>
public class GioHangBadgeViewComponent : ViewComponent
{
    private readonly CartService _gio;

    public GioHangBadgeViewComponent(CartService gio)
    {
        _gio = gio;
    }

    public IViewComponentResult Invoke()
    {
        var soLuong = _gio.GetLines().Sum(l => l.SoLuong);
        return View(soLuong);
    }
}
