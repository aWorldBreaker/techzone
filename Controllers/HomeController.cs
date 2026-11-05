using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

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
            .Include(f => f.Product).ThenInclude(p => p.Specs)
            // Sprint 2 — B-5: chi hien sale con suat (het quota thi dung hien
            // gia sale keo khach mua nhung checkout lai thu gia goc).
            .Where(f => f.BatDau <= DateTime.Now && f.KetThuc >= DateTime.Now
                && f.DaBan < f.GioiHanSL)
            .OrderBy(f => f.KetThuc)
            .ToListAsync();

        var noiBat = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Specs)
            .Where(p => p.LaNoiBat)
            .OrderByDescending(p => p.LuotBan)
            .ToListAsync();

        var banChay = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Specs)
            .OrderByDescending(p => p.LuotBan)
            .Take(8)
            .ToListAsync();

        var danhMuc = await _db.Categories
            .Include(c => c.Products)
            .OrderBy(c => c.Ten)
            .ToListAsync();

        ViewBag.FlashSale = flashSale;
        ViewBag.NoiBat = noiBat;
        ViewBag.BanChay = banChay;
        ViewBag.DanhMuc = danhMuc;
        ViewBag.FlashSaleKetThuc = flashSale.FirstOrDefault()?.KetThuc;
        // Khung gio Flash Sale + banner (nhom B): slot kem san pham, sap xep theo gio bat dau.
        var gioHienTai = DateTime.Now.Hour;
        var slots = await _db.FlashSaleSlots
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .Where(s => s.IsActive)
            .OrderBy(s => s.GioBatDau)
            .ToListAsync();
        ViewBag.Slots = slots;
        ViewBag.SlotHienTai = slots.FirstOrDefault(s => s.DangChay(gioHienTai));
        ViewBag.SlotTiepTheo = slots.FirstOrDefault(s => s.GioBatDau > gioHienTai)
            ?? slots.FirstOrDefault();
        ViewBag.Banners = await _db.Banners
            .Where(b => b.IsActive)
            .OrderBy(b => b.ThuTu)
            .ToListAsync();
        // Số liệu thật cho dải proof ở trang chủ — đếm từ DB, không ghi tay.
        ViewBag.TongSanPham = await _db.Products.CountAsync();
        ViewBag.TongHang = await _db.Brands.CountAsync();
        ViewBag.TongDanhMuc = await _db.Categories.CountAsync();

        // Đã xem gần đây (nhóm A): đọc session, giữ đúng thứ tự xem.
        var daXemJson = HttpContext.Session.GetString("TechZone.RecentlyViewed");
        var daXemIds = string.IsNullOrEmpty(daXemJson)
            ? new List<int>()
            : System.Text.Json.JsonSerializer.Deserialize<List<int>>(daXemJson) ?? new List<int>();
        var daXem = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Specs)
            .Where(p => daXemIds.Contains(p.Id))
            .ToListAsync();
        ViewBag.RecentlyViewed = daXemIds
            .Select(id => daXem.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Cast<Product>()
            .Take(4).ToList();
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
