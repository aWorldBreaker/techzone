using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Controllers;

/// <summary>Quan tri: chi role Admin. Dashboard + quan ly san pham / don / voucher.</summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Dashboard()
    {
        var thangNay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var bayNgayTruoc = DateTime.Today.AddDays(-6);

        var donThang = await _db.Orders
            .Where(o => o.NgayDat >= thangNay && o.TrangThai != OrderStatus.DaHuy)
            .ToListAsync();

        var doanhThu7Ngay = new List<(string Ngay, decimal DoanhThu)>();
        for (var d = bayNgayTruoc; d <= DateTime.Today; d = d.AddDays(1))
        {
            var tong = await _db.Orders
                .Where(o => o.NgayDat.Date == d && o.TrangThai != OrderStatus.DaHuy)
                .SumAsync(o => (decimal?)o.TongTien) ?? 0;
            doanhThu7Ngay.Add((d.ToString("dd/MM"), tong));
        }

        var vm = new DashboardViewModel
        {
            DoanhThuThang = donThang.Sum(o => o.TongTien),
            DonMoi = await _db.Orders.CountAsync(o => o.TrangThai == OrderStatus.ChoXacNhan),
            TongDon = await _db.Orders.CountAsync(),
            TongSanPham = await _db.Products.CountAsync(),
            TopBanChay = await _db.Products.OrderByDescending(p => p.LuotBan).Take(5).ToListAsync(),
            SapHetHang = await _db.Products.Where(p => p.TonKho <= 10).OrderBy(p => p.TonKho).Take(5).ToListAsync(),
            DonGanDay = await _db.Orders
                .Include(o => o.ChiTiet)
                .OrderByDescending(o => o.NgayDat).Take(8).ToListAsync(),
            DoanhThu7Ngay = doanhThu7Ngay
        };
        return View(vm);
    }

    // --- San pham ---
    public async Task<IActionResult> Products()
    {
        var list = await _db.Products
            .Include(p => p.Category).Include(p => p.Brand)
            .OrderByDescending(p => p.NgayTao).ToListAsync();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> ProductForm(int? id)
    {
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Ten).ToListAsync();
        ViewBag.Brands = await _db.Brands.OrderBy(b => b.Ten).ToListAsync();
        if (id is null) return View(new Product());
        var product = await _db.Products
            .Include(p => p.Specs).FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductForm(Product model)
    {
        // Navigation Category/Brand/Specs.Product khong co trong form nen khong
        // bind duoc — loai khoi ModelState, neu khong form luon bao loi.
        ModelState.Remove(nameof(Product.Category));
        ModelState.Remove(nameof(Product.Brand));
        ModelState.Remove("Specs.Product");
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Ten).ToListAsync();
        ViewBag.Brands = await _db.Brands.OrderBy(b => b.Ten).ToListAsync();
        if (!ModelState.IsValid) return View(model);

        // Neu admin khong nhap thong so nao thi khong tao dong Specs trong.
        if (model.Specs is not null && string.IsNullOrWhiteSpace(model.Specs.Chip)
            && string.IsNullOrWhiteSpace(model.Specs.Ram)
            && string.IsNullOrWhiteSpace(model.Specs.Rom)
            && string.IsNullOrWhiteSpace(model.Specs.Pin)
            && string.IsNullOrWhiteSpace(model.Specs.ManHinh)
            && string.IsNullOrWhiteSpace(model.Specs.Camera)
            && string.IsNullOrWhiteSpace(model.Specs.CardDoHoa)
            && string.IsNullOrWhiteSpace(model.Specs.ThongSoKhac))
        {
            model.Specs = null;
        }

        if (model.Id == 0)
        {
            model.NgayTao = DateTime.Now;
            _db.Products.Add(model);
        }
        else
        {
            var cu = await _db.Products.Include(p => p.Specs)
                .FirstOrDefaultAsync(p => p.Id == model.Id);
            if (cu is null) return NotFound();
            cu.Ten = model.Ten; cu.MoTa = model.MoTa; cu.Gia = model.Gia;
            cu.GiaKhuyenMai = model.GiaKhuyenMai; cu.CategoryId = model.CategoryId;
            cu.BrandId = model.BrandId; cu.TonKho = model.TonKho; cu.LaNoiBat = model.LaNoiBat;
            if (model.Specs is not null)
            {
                if (cu.Specs is null)
                {
                    model.Specs.ProductId = cu.Id;
                    _db.ProductSpecs.Add(model.Specs);
                }
                else
                {
                    cu.Specs.Chip = model.Specs.Chip; cu.Specs.Ram = model.Specs.Ram;
                    cu.Specs.Rom = model.Specs.Rom; cu.Specs.Pin = model.Specs.Pin;
                    cu.Specs.ManHinh = model.Specs.ManHinh; cu.Specs.Camera = model.Specs.Camera;
                    cu.Specs.CardDoHoa = model.Specs.CardDoHoa; cu.Specs.ThongSoKhac = model.Specs.ThongSoKhac;
                }
            }
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu sản phẩm.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaSanPham(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();
        if (await _db.OrderDetails.AnyAsync(d => d.ProductId == id))
        {
            TempData["Error"] = "Sản phẩm đã có trong đơn hàng, không xóa được. Hãy để tồn kho = 0 để ngừng bán.";
            return RedirectToAction(nameof(Products));
        }
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa sản phẩm.";
        return RedirectToAction(nameof(Products));
    }

    // --- Don hang ---
    public async Task<IActionResult> Orders(string? trangThai)
    {
        var query = _db.Orders.Include(o => o.ChiTiet).AsQueryable();
        if (!string.IsNullOrEmpty(trangThai))
            query = query.Where(o => o.TrangThai == trangThai);
        ViewBag.TrangThai = trangThai;
        return View(await query.OrderByDescending(o => o.NgayDat).ToListAsync());
    }

    public async Task<IActionResult> OrderDetails(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiTrangThai(int id, string trangThaiMoi)
    {
        var hopLe = new[] { OrderStatus.ChoXacNhan, OrderStatus.DangGiao, OrderStatus.DaGiao, OrderStatus.DaHuy };
        if (!hopLe.Contains(trangThaiMoi)) return BadRequest("Trạng thái không hợp lệ.");
        var order = await _db.Orders.Include(o => o.ChiTiet).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Huy don: hoan ton kho (chi hoan 1 lan: don chua o trang thai DaHuy).
        if (trangThaiMoi == OrderStatus.DaHuy && order.TrangThai != OrderStatus.DaHuy)
        {
            foreach (var detail in order.ChiTiet)
            {
                var product = await _db.Products.FindAsync(detail.ProductId);
                if (product is not null) product.TonKho += detail.SoLuong;
            }
        }
        order.TrangThai = trangThaiMoi;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đơn #{id} đã chuyển sang: {trangThaiMoi}.";
        return RedirectToAction(nameof(OrderDetails), new { id });
    }

    // --- Voucher ---
    public async Task<IActionResult> Vouchers() =>
        View(await _db.Vouchers.OrderByDescending(v => v.NgayHetHan).ToListAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemVoucher(Voucher model)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Vouchers));
        model.Ma = model.Ma.Trim().ToUpper();
        if (await _db.Vouchers.AnyAsync(v => v.Ma == model.Ma))
        {
            TempData["Error"] = $"Mã {model.Ma} đã tồn tại.";
            return RedirectToAction(nameof(Vouchers));
        }
        _db.Vouchers.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm voucher {model.Ma}.";
        return RedirectToAction(nameof(Vouchers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaVoucher(int id)
    {
        var voucher = await _db.Vouchers.FindAsync(id);
        if (voucher is null) return NotFound();
        _db.Vouchers.Remove(voucher);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Vouchers));
    }
}
