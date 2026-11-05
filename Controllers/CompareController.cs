using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Controllers;

/// <summary>
/// So sanh toi da 3 san pham. Chi luu danh sach Id trong session (JSON),
/// doc san pham tu DB moi lan hien thi nen gia/ton kho luon moi nhat.
/// Khong them bang DB moi.
/// </summary>
public class CompareController : Controller
{
    private const string SessionKey = "TechZone.Compare";
    private const int ToiDa = 3;
    private readonly AppDbContext _db;

    public CompareController(AppDbContext db) => _db = db;

    private List<int> LayDanhSach()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        return string.IsNullOrEmpty(json)
            ? new List<int>()
            : JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
    }

    private void Luu(List<int> ids) =>
        HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(ids));

    public async Task<IActionResult> Index()
    {
        var ids = LayDanhSach();
        var list = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Specs)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
        // Where khong dam bao thu tu: sap lai theo thu tu da them.
        var sapXep = ids.Select(id => list.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Cast<Product>().ToList();
        return View(sapXep);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        var ids = LayDanhSach();
        if (!ids.Contains(productId))
        {
            if (ids.Count >= ToiDa)
            {
                TempData["Error"] = $"Chỉ so sánh tối đa {ToiDa} sản phẩm. Hãy bỏ bớt một sản phẩm rồi thêm lại.";
                return RedirectToAction(nameof(Index));
            }
            ids.Add(productId);
            Luu(ids);
            TempData["Success"] = $"Đã thêm {product.Ten} vào so sánh.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int productId)
    {
        var ids = LayDanhSach();
        ids.Remove(productId);
        Luu(ids);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult XoaHet()
    {
        HttpContext.Session.Remove(SessionKey);
        return RedirectToAction(nameof(Index));
    }
}
