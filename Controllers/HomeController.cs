using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;

namespace TechZone.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var homNay = DateTime.Today;
        var flashSale = await _db.FlashSales
            .Include(f => f.Product).ThenInclude(p => p.Images)
            .Include(f => f.Product).ThenInclude(p => p.Category)
            .Where(f => f.BatDau <= DateTime.Now && f.KetThuc >= DateTime.Now)
            .OrderBy(f => f.KetThuc)
            .ToListAsync();

        var noiBat = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Where(p => p.LaNoiBat)
            .OrderByDescending(p => p.LuotBan)
            .ToListAsync();

        var banChay = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .OrderByDescending(p => p.LuotBan)
            .Take(8)
            .ToListAsync();

        var danhMuc = await _db.Categories
            .OrderBy(c => c.Ten)
            .ToListAsync();

        ViewBag.FlashSale = flashSale;
        ViewBag.NoiBat = noiBat;
        ViewBag.BanChay = banChay;
        ViewBag.DanhMuc = danhMuc;
        ViewBag.FlashSaleKetThuc = flashSale.FirstOrDefault()?.KetThuc;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
