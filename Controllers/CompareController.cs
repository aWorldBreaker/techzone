using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

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
    private readonly PriceCalculationService _gia;

    public CompareController(AppDbContext db, PriceCalculationService gia)
    {
        _db = db;
        _gia = gia;
    }

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
        var list = await _db.Products.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Specs)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            // Follow-up M4-3: hang "Danh gia" tinh sao tu Reviews da duyet,
            // khong dung p.DanhGiaTB (seed cu — VD Id 1 seed 4.8 trong khi
            // TB that 4.5; SP 0 review van hien "X.Y sao").
            .Include(p => p.Reviews.Where(r => r.IsApproved))
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
        ViewBag.GiaHieuDung = await _gia.TinhGiaDanhMucAsync(list, DateTime.Now);
        // Where khong dam bao thu tu: sap lai theo thu tu da them.
        var sapXep = ids.Select(id => list.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Cast<Product>().ToList();
        return View(sapXep);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, string? returnUrl = null)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        var returnToCatalog = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            && returnUrl.EndsWith("#catalog-results", StringComparison.Ordinal);
        var ids = LayDanhSach();
        if (!ids.Contains(productId))
        {
            if (ids.Count >= ToiDa)
            {
                TempData[returnToCatalog ? "CatalogCompareStatus" : "Error"] =
                    $"Chỉ so sánh tối đa {ToiDa} sản phẩm. Hãy bỏ bớt một sản phẩm rồi thêm lại.";
                return VeDanhSach(returnToCatalog, returnUrl);
            }
            ids.Add(productId);
            Luu(ids);
            TempData[returnToCatalog ? "CatalogCompareStatus" : "Success"] = $"Đã thêm {product.Ten} vào so sánh.";
        }
        else if (returnToCatalog)
        {
            TempData["CatalogCompareStatus"] = $"{product.Ten} đã có trong bảng so sánh.";
        }
        return VeDanhSach(returnToCatalog, returnUrl);
    }

    private IActionResult VeDanhSach(bool returnToCatalog, string? returnUrl) => returnToCatalog
        ? LocalRedirect(returnUrl!)
        : RedirectToAction(nameof(Index));

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
