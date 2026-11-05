using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Controllers;

/// <summary>
/// Danh muc + loc dropdown (hang / chip / RAM / dung luong / gia) + sap xep.
/// Loc chip/RAM/dung luong doc tu bang ProductSpecs, khong hard-code.
/// </summary>
public class ProductsController : Controller
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(ProductFilterViewModel filter)
    {
        var query = _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Specs)
            .AsQueryable();

        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        if (filter.BrandId.HasValue)
            query = query.Where(p => p.BrandId == filter.BrandId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Chip))
            query = query.Where(p => p.Specs != null && p.Specs.Chip == filter.Chip);
        if (!string.IsNullOrWhiteSpace(filter.Ram))
            query = query.Where(p => p.Specs != null && p.Specs.Ram == filter.Ram);
        if (!string.IsNullOrWhiteSpace(filter.DungLuong))
            query = query.Where(p => p.Specs != null && p.Specs.Rom == filter.DungLuong);
        if (filter.GiaTu.HasValue)
            query = query.Where(p => (p.GiaKhuyenMai ?? p.Gia) >= filter.GiaTu.Value);
        if (filter.GiaDen.HasValue)
            query = query.Where(p => (p.GiaKhuyenMai ?? p.Gia) <= filter.GiaDen.Value);
        if (!string.IsNullOrWhiteSpace(filter.TuKhoa))
        {
            var kw = filter.TuKhoa.Trim();
            query = query.Where(p => p.Ten.Contains(kw) || (p.MoTa != null && p.MoTa.Contains(kw)));
        }

        query = filter.SapXep switch
        {
            "gia-tang" => query.OrderBy(p => p.GiaKhuyenMai ?? p.Gia),
            "gia-giam" => query.OrderByDescending(p => p.GiaKhuyenMai ?? p.Gia),
            "ban-chay" => query.OrderByDescending(p => p.LuotBan),
            "moi-nhat" => query.OrderByDescending(p => p.NgayTao),
            _ => query.OrderByDescending(p => p.LaNoiBat).ThenByDescending(p => p.LuotBan),
        };

        filter.TongSo = await query.CountAsync();
        var trang = Math.Max(1, filter.Trang);
        filter.Trang = trang;
        filter.KetQua = await query
            .Skip((trang - 1) * ProductFilterViewModel.SoMoiTrang)
            .Take(ProductFilterViewModel.SoMoiTrang)
            .ToListAsync();

        filter.DanhMuc = await _db.Categories.OrderBy(c => c.Ten).ToListAsync();
        filter.ThuongHieu = await _db.Brands.OrderBy(b => b.Ten).ToListAsync();
        filter.Chips = await _db.ProductSpecs
            .Where(s => s.Chip != null).Select(s => s.Chip!).Distinct().OrderBy(c => c).ToListAsync();
        filter.Rams = await _db.ProductSpecs
            .Where(s => s.Ram != null).Select(s => s.Ram!).Distinct().OrderBy(c => c).ToListAsync();
        filter.DungLuongs = await _db.ProductSpecs
            .Where(s => s.Rom != null).Select(s => s.Rom!).Distinct().OrderBy(c => c).ToListAsync();

        return View(filter);
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Specs)
            .Include(p => p.Variants)
            .Include(p => p.Reviews).ThenInclude(r => r.User)
            .Include(p => p.FlashSales)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        var tuongTu = await _db.Products
            .Include(p => p.Images)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != id)
            .OrderByDescending(p => p.LuotBan)
            .Take(4)
            .ToListAsync();
        ViewBag.TuongTu = tuongTu;
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DanhGia(int productId, int sao, string? noiDung)
    {
        if (sao < 1 || sao > 5) return BadRequest("Sao phai tu 1 den 5.");
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();

        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        _db.Reviews.Add(new Review
        {
            ProductId = productId,
            UserId = userId,
            Sao = sao,
            NoiDung = string.IsNullOrWhiteSpace(noiDung) ? null : noiDung.Trim()
        });

        var reviews = await _db.Reviews.Where(r => r.ProductId == productId).ToListAsync();
        product.DanhGiaTB = Math.Round(reviews.Average(r => r.Sao), 1);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = productId });
    }
}
