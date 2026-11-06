using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>
/// Danh muc + loc dropdown (hang / chip / RAM / dung luong / gia) + sap xep.
/// Loc chip/RAM/dung luong doc tu bang ProductSpecs, khong hard-code.
/// Mo rong nhom A: lich su da xem (session), goi y mua kem, hoi - dap,
/// danh gia kem anh cho Admin duyet.
/// </summary>
public class ProductsController : Controller
{
    private const string SessionDaXem = "TechZone.RecentlyViewed";
    private const int ToiDaDaXem = 8;

    // Goi y mua kem (Sprint 3 — B-7): doc tu bang Combos (combo dang mo,
    // con han, co chua SP nay), khong hard-code Dictionary nhu truoc
    // (AUDIT P2-12). Khong co combo hop le thi fallback nhu cu: SP cung
    // danh muc ban chay nhat. ViewBag.ComboId de form AddCombo gui kem.
    private async Task<(Product? SanPhamKem, Combo? ComboDb)> TimComboAsync(int productId, DateTime now)
    {
        var comboDb = await _db.Combos
            .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .Where(c => c.IsActive && c.BatDau <= now && c.KetThuc >= now
                && c.Items.Any(i => i.ProductId == productId))
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
        var kem = comboDb?.Items
            .Where(i => i.ProductId != productId)
            .OrderBy(i => i.Id)
            .Select(i => i.Product)
            .FirstOrDefault();
        return (kem, comboDb);
    }

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly PriceCalculationService _gia;

    public ProductsController(AppDbContext db, IWebHostEnvironment env, PriceCalculationService gia)
    {
        _db = db;
        _env = env;
        _gia = gia;
    }

    public async Task<IActionResult> Index(ProductFilterViewModel filter)
    {
        var query = _db.Products
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Specs)
            // M4-3: _ProductCard tinh sao tu Reviews da duyet (giong
            // Details) nen Listing phai nap Reviews — chi review da duyet.
            .Include(p => p.Reviews.Where(r => r.IsApproved))
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

        // P1 Sprint 6 Module 3: SQLite khong ORDER BY duoc cot decimal
        // ("SQLite does not support expressions of type 'decimal' in
        // ORDER BY") nen ep gia hieu dung sang double ngay trong ORDER BY.
        // Gia VND la so nguyen (decimal(18,0), toi da 1e9 theo Range tren
        // Product) nen double bieu dien CHINH XAC toi 2^53 — khong mat
        // precision thuc te, thu tu sort giu nguyen. Sort van chay phia DB,
        // TRUOC Skip/Take nen phan trang dung; khong materialize roi moi
        // sort (cach do se phai tai toan bo bang ve bo nho).
        // (PriceCalculationService da ghi nhan gioi han tuong tu cho
        // FlashSale: sort decimal o client sau ToListAsync — o day khong
        // ap dung duoc vi listing co phan trang.)
        query = filter.SapXep switch
        {
            "gia-tang" => query.OrderBy(p => (double)(p.GiaKhuyenMai ?? p.Gia)),
            "gia-giam" => query.OrderByDescending(p => (double)(p.GiaKhuyenMai ?? p.Gia)),
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
            // Chi hien danh gia Admin da duyet (nhom A); danh gia moi gui
            // IsApproved = false nen khong hien cho toi khi duyet.
            .Include(p => p.Reviews.Where(r => r.IsApproved)).ThenInclude(r => r.User)
            .Include(p => p.FlashSales)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        // SALE-01: gia hien thi tinh qua PriceCalculationService — cung
        // ham, cung thu tu uu tien (FlashSale > slot theo gio > gia goc)
        // nhu Cart/Checkout, so luong mac dinh 1, bien the mac dinh
        // (giaThem = 0). Dung 1 moc `now` cho ca gia lan combo de cung
        // 1 thoi diem thi gia Details == gia Cart/Checkout.
        var now = DateTime.Now;
        var gia = await _gia.TinhGiaAsync(id, 0, 1, now);
        ViewBag.GiaHienThi = gia.DonGia;
        ViewBag.GiaGoc = gia.GiaGoc;
        ViewBag.TenKhuyenMai = gia.TenKhuyenMai;
        ViewBag.FlashSaleApDung = gia.FlashSaleId.HasValue
            ? product.FlashSales.FirstOrDefault(f => f.Id == gia.FlashSaleId.Value)
            : null;

        // Lich su da xem: moi nhat dau, khu trung, toi da 8 (mau CompareController).
        var daXem = LayDaXem();
        daXem.Remove(id);
        daXem.Insert(0, id);
        if (daXem.Count > ToiDaDaXem) daXem = daXem.Take(ToiDaDaXem).ToList();
        HttpContext.Session.SetString(SessionDaXem, JsonSerializer.Serialize(daXem));

        var idDaXemKhac = daXem.Where(x => x != id).Take(4).ToList();
        var sanPhamDaXem = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Specs)
            // M4-3: _RecentlyViewed dung _ProductCard (tinh sao tu Reviews).
            .Include(p => p.Reviews.Where(r => r.IsApproved))
            .Where(p => idDaXemKhac.Contains(p.Id))
            .ToListAsync();
        // Where khong dam bao thu tu: sap lai theo thu tu da xem.
        ViewBag.RecentlyViewed = idDaXemKhac
            .Select(x => sanPhamDaXem.FirstOrDefault(p => p.Id == x))
            .Where(p => p is not null).Cast<Product>().ToList();

        // Goi y mua kem: theo bang Combos, neu khong co / het hang thi lay
        // san pham cung danh muc ban chay nhat (khac san pham hien tai).
        // ViewBag.ComboGiamGia = so tien giam cua 1 bo (neu co combo).
        Product? combo = null;
        Combo? comboDb = null;
        (combo, comboDb) = await TimComboAsync(id, now);
        // SALE-01: tong bo + giam gia combo tinh qua TinhGiaComboAsync
        // (cung ham checkout dung — gia tung mon lay lai qua TinhGiaAsync
        // nen Flash Sale duoc ap truoc khi giam %). Khong cong GiaBan nua.
        Dictionary<int, decimal>? giaMonCombo = null;
        if (comboDb is not null)
        {
            var linesCombo = comboDb.Items.Select(i => new CartLineViewModel
            {
                Product = i.Product,
                Variant = null,
                SoLuong = i.SoLuong,
            }).ToList();
            var ketQuaCombo = await _gia.TinhGiaComboAsync(comboDb.Id, linesCombo, now);
            if (ketQuaCombo.HopLe)
            {
                ViewBag.ComboTongBo = ketQuaCombo.TongTienBo;
                ViewBag.ComboGiamGia = ketQuaCombo.GiamGia;
                ViewBag.ComboId = comboDb.Id;
                // Gia hien thi tung mon (da ap Flash Sale neu co) de tong
                // cac mon hien thi khop TongTienBo.
                giaMonCombo = new Dictionary<int, decimal>();
                foreach (var item in comboDb.Items)
                {
                    var g = await _gia.TinhGiaAsync(item.ProductId, 0, item.SoLuong, now);
                    giaMonCombo[item.ProductId] = g.DonGia;
                }
            }
            // Khong hop le (vd Flash Sale cua 1 mon vua het suat): khong
            // set ComboId — form AddCombo se them 2 mon nhu hang thuong,
            // checkout khong ap giam combo; khong hien tong bo sai nhu truoc.
        }
        combo ??= await _db.Products
            .Include(p => p.Images)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != id)
            .OrderByDescending(p => p.LuotBan)
            .FirstOrDefaultAsync();
        ViewBag.Combo = combo;
        if (giaMonCombo is not null)
        {
            ViewBag.ComboGiaChinh = giaMonCombo.TryGetValue(id, out var gc) ? gc : product.GiaBan;
            ViewBag.ComboGiaKem = combo is not null
                && giaMonCombo.TryGetValue(combo.Id, out var gk) ? gk : (decimal?)null;
        }

        // Trang thai yeu thich cua tai khoan dang nhap (neu co).
        var daYeuThich = false;
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id;
            if (userId is not null)
                daYeuThich = await _db.Wishlists
                    .AnyAsync(w => w.UserId == userId && w.ProductId == id);
        }
        ViewBag.DaYeuThich = daYeuThich;

        // Hoi - dap: Product khong co navigation Questions nen doc rieng
        // tu bang ProductQuestions (kem nguoi hoi + nguoi tra loi).
        ViewBag.CauHoi = await _db.ProductQuestions
            .Include(q => q.User)
            .Include(q => q.NguoiTraLoi)
            .Where(q => q.ProductId == id)
            .OrderByDescending(q => q.NgayHoi)
            .ToListAsync();

        var tuongTu = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Specs)
            // M4-3: "San pham tuong tu" dung _ProductCard (tinh sao tu Reviews).
            .Include(p => p.Reviews.Where(r => r.IsApproved))
            .Where(p => p.CategoryId == product.CategoryId && p.Id != id)
            .OrderByDescending(p => p.LuotBan)
            .Take(4)
            .ToListAsync();
        ViewBag.TuongTu = tuongTu;

        // Sprint 5 — viewer 3D dung chung: chi SP co trong Product3dModels.Map
        // moi co viewer (Id 1/3/4); SP khac (VD Id 2 iPhone) giu nguyen anh.
        ViewBag.Model3d = Product3dModels.ForProduct(id);
        return View(product);
    }

    private List<int> LayDaXem()
    {
        var json = HttpContext.Session.GetString(SessionDaXem);
        return string.IsNullOrEmpty(json)
            ? new List<int>()
            : JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
    }

    // Sprint 4 — QA P1 (audit P1-15): danh gia bat buoc dang nhap (guest
    // khong tao duoc Review UserId null); giong HoiDap ben duoi.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DanhGia(int productId, int sao, string? noiDung, IFormFile? anhKem)
    {
        if (sao < 1 || sao > 5) return BadRequest("Sao phai tu 1 den 5.");
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();

        // Kiem tra anh kem truoc khi luu: chi nhan anh, toi da 5MB
        // (mau AdminController.ProductForm).
        string? loiAnh = null;
        string? duoiAnh = null;
        if (anhKem is not null && anhKem.Length > 0)
        {
            duoiAnh = Path.GetExtension(anhKem.FileName).ToLowerInvariant();
            var duoiHopLe = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!duoiHopLe.Contains(duoiAnh))
                loiAnh = "Chỉ nhận file ảnh .jpg, .jpeg, .png hoặc .webp.";
            else if (anhKem.Length > 5 * 1024 * 1024)
                loiAnh = "Ảnh tối đa 5MB.";
        }
        if (loiAnh is not null)
        {
            TempData["Error"] = loiAnh;
            return RedirectToAction(nameof(Details), new { id = productId });
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Sao = sao,
            NoiDung = string.IsNullOrWhiteSpace(noiDung) ? null : noiDung.Trim(),
            IsApproved = false
        };
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        // Upload anh kem (neu co): ten file Guid duy nhat trong wwwroot/images.
        if (anhKem is not null && anhKem.Length > 0)
        {
            var tenFile = $"{Guid.NewGuid():N}{duoiAnh!}";
            var thuMuc = Path.Combine(_env.WebRootPath, "images");
            Directory.CreateDirectory(thuMuc);
            await using var stream = new FileStream(Path.Combine(thuMuc, tenFile), FileMode.Create);
            await anhKem.CopyToAsync(stream);
            review.ImageUrl = $"/images/{tenFile}";
            await _db.SaveChangesAsync();
        }

        // Tinh lai diem trung binh CHI tu danh gia da duyet.
        var daDuyet = await _db.Reviews
            .Where(r => r.ProductId == productId && r.IsApproved)
            .ToListAsync();
        product.DanhGiaTB = daDuyet.Any() ? Math.Round(daDuyet.Average(r => r.Sao), 1) : 0;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đánh giá của bạn đang chờ Admin duyệt.";
        return RedirectToAction(nameof(Details), new { id = productId });
    }

    /// <summary>Khach dat cau hoi duoi san pham (nhom A); Admin tra loi (nhom D).</summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HoiDap(int productId, string? cauHoi)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        if (string.IsNullOrWhiteSpace(cauHoi))
        {
            TempData["Error"] = "Vui lòng nhập câu hỏi.";
            return RedirectToAction(nameof(Details), new { id = productId });
        }
        if (cauHoi.Trim().Length > 1000)
        {
            TempData["Error"] = "Câu hỏi tối đa 1000 ký tự.";
            return RedirectToAction(nameof(Details), new { id = productId });
        }

        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        _db.ProductQuestions.Add(new ProductQuestion
        {
            ProductId = productId,
            UserId = userId,
            CauHoi = cauHoi.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Câu hỏi của bạn đã được gửi. TechZone sẽ trả lời sớm.";
        return RedirectToAction(nameof(Details), new { id = productId });
    }
}
