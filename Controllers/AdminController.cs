using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>Quan tri: chi role Admin. Dashboard + quan ly san pham / don / voucher.</summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly OrderService _orders;
    private const int SoMucMoiTrang = 10;

    public AdminController(AppDbContext db, IWebHostEnvironment env, OrderService orders)
    {
        _db = db;
        _env = env;
        _orders = orders;
    }

    public async Task<IActionResult> Dashboard() =>
        View(await TaiDuLieuDashboardAsync());

    /// <summary>
    /// Helper VeTrangSauTacVu: form tu trang TongQuan gui kem hidden
    /// tuTrang=TongQuan thi quay lai TongQuan; form tu Dashboard (nhap kho
    /// nhanh, Dot 2 B6) gui tuTrang=Dashboard thi quay lai Dashboard;
    /// trang cu goi (khong co tuTrang) thi ve dung action mac dinh nhu
    /// truoc — hanh vi cu giu nguyen 100%. Request.Form chi doc khi co form
    /// content-type (test goi truc tiep khong co form thi khong nem loi).
    /// </summary>
    private IActionResult VeTrangSauTacVu(string actionMacDinh)
    {
        if (Request.HasFormContentType)
        {
            try
            {
                if (Request.Form.TryGetValue("tuTrang", out var t) && t == "TongQuan")
                    return RedirectToAction(nameof(TongQuan));
                if (Request.Form.TryGetValue("tuTrang", out var t2) && t2 == "Dashboard")
                    return RedirectToAction(nameof(Dashboard));
            }
            catch (InvalidOperationException) { /* khong co form hop le: ve mac dinh */ }
        }
        return RedirectToAction(actionMacDinh);
    }

    /// <summary>VeTrangSauTacVu kem route values (vd OrderDetails can id).</summary>
    private IActionResult VeTrangSauTacVu(string actionMacDinh, object routeValues)
    {
        if (Request.HasFormContentType)
        {
            try
            {
                if (Request.Form.TryGetValue("tuTrang", out var t) && t == "TongQuan")
                    return RedirectToAction(nameof(TongQuan));
                if (Request.Form.TryGetValue("tuTrang", out var t2) && t2 == "Dashboard")
                    return RedirectToAction(nameof(Dashboard));
            }
            catch (InvalidOperationException) { /* khong co form hop le: ve mac dinh */ }
        }
        return RedirectToAction(actionMacDinh, routeValues);
    }

    /// <summary>
    /// Trang tong hop: 12 section trong 1 trang. Load dung query/logic
    /// cua tung action goc; Dashboard + KhachHang dung helper chung.
    /// </summary>
    public async Task<IActionResult> TongQuan()
    {
        var dashboard = await TaiDuLieuDashboardAsync();
        var vm = new TongQuanViewModel
        {
            Dashboard = dashboard,
            Products = await _db.Products
                .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Images)
                .OrderByDescending(p => p.NgayTao).ToListAsync(),
            DonGanDay = await _db.Orders
                .Include(o => o.ChiTiet)
                .OrderByDescending(o => o.NgayDat).Take(10).ToListAsync(),
            DonTheoTrangThai = dashboard.DonTheoTrangThai,
            KhachHang = await TaiDuLieuKhachHangAsync(),
            Vouchers = await _db.Vouchers.OrderByDescending(v => v.NgayHetHan).ToListAsync(),
            Combos = await _db.Combos
                .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
                .OrderByDescending(c => c.Id).ToListAsync(),
            FlashSaleSlots = await _db.FlashSaleSlots
                .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
                .OrderBy(s => s.GioBatDau).ToListAsync(),
            Banners = await _db.Banners.OrderBy(b => b.ThuTu).ToListAsync(),
            ReviewsChoDuyet = await _db.Reviews
                .Include(r => r.Product).Include(r => r.User)
                .Where(r => !r.IsApproved)
                .OrderBy(r => r.NgayDanhGia).ToListAsync(),
            ReviewsDaDuyetGanDay = await _db.Reviews
                .Include(r => r.Product).Include(r => r.User)
                .Where(r => r.IsApproved)
                .OrderByDescending(r => r.NgayDanhGia).Take(10).ToListAsync(),
            CauHoiChuaTraLoi = await _db.ProductQuestions
                .Include(q => q.Product).Include(q => q.User)
                .Where(q => q.TraLoi == null)
                .OrderBy(q => q.NgayHoi).ToListAsync(),
            SanPhamKho = await _db.Products.Include(p => p.Images).OrderBy(p => p.Ten).ToListAsync(),
            NhapKhoGanDay = await _db.StockIns
                .Include(s => s.Product)
                .OrderByDescending(s => s.NgayNhap).Take(10).ToListAsync(),
            ThongBaoGanDay = await _db.Notifications
                .Include(n => n.User)
                .OrderByDescending(n => n.NgayTao).Take(20).ToListAsync(),
            NguoiNhanThongBao = await _db.Users
                .Where(u => u.Email != "admin@techzone.vn")
                .OrderBy(u => u.HoTen).ToListAsync(),
            DoiTraChoDuyet = await _db.ReturnRequests
                .Include(r => r.Product).Include(r => r.Order).Include(r => r.User)
                .Where(r => r.TrangThai == "Chờ duyệt")
                .OrderByDescending(r => r.NgayTao).ToListAsync(),
            BaoHanhChoDuyet = await _db.WarrantyClaims
                .Include(w => w.Product).Include(w => w.Order).Include(w => w.User)
                .Where(w => w.TrangThai == "Chờ duyệt")
                .OrderByDescending(w => w.NgayTao).ToListAsync(),
        };
        return View(vm);
    }

    /// <summary>Logic load Dashboard goc, tach ra de Dashboard() + TongQuan() dung chung.</summary>
    private async Task<DashboardViewModel> TaiDuLieuDashboardAsync()
    {
        var thangNay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        var donThang = await _db.Orders
            .Where(o => o.NgayDat >= thangNay && o.TrangThai != OrderStatus.DaHuy)
            .ToListAsync();

        var doanhThu30Ngay = new List<(string Ngay, decimal DoanhThu)>();
        for (var d = DateTime.Today.AddDays(-29); d <= DateTime.Today; d = d.AddDays(1))
        {
            // SQLite khong dich duoc Sum tren decimal -> lay ve roi cong o client.
            var tong = (await _db.Orders
                .Where(o => o.NgayDat.Date == d && o.TrangThai != OrderStatus.DaHuy)
                .Select(o => o.TongTien)
                .ToListAsync()).Sum();
            doanhThu30Ngay.Add((d.ToString("dd/MM"), tong));
        }
        // 7 ngay gan nhat la 7 muc cuoi cua bang 30 ngay.
        var doanhThu7Ngay = doanhThu30Ngay.TakeLast(7).ToList();

        var donTheoTrangThai = await _db.Orders
            .GroupBy(o => o.TrangThai)
            .Select(g => new { TrangThai = g.Key, SoLuong = g.Count() })
            .ToDictionaryAsync(g => g.TrangThai, g => g.SoLuong);

        // Uoc luong khach moi trong thang: dem tai khoan tao trong thang nay,
        // tru tai khoan admin. AdminController chi co _db nen dung NgayTao
        // cua AspNetUsers lam moc; khong co bang log dang ky rieng.
        var khachMoiThang = await _db.Users.CountAsync(u =>
            u.NgayTao >= thangNay && u.Email != "admin@techzone.vn");

        // Dot 1 Admin-enhance (A3): chi so moi chi doc, tinh tu 2 tap da load
        // o tren (DoanhThu30Ngay + DonTheoTrangThai) — khong them query.
        // Tuan truoc = 7 muc ngay truoc 7 ngay gan nhat (ngay -14..-8).
        var doanhThuTuan = doanhThu7Ngay.Sum(d => d.DoanhThu);
        var doanhThuTuanTruoc = doanhThu30Ngay.TakeLast(14).Take(7).Sum(d => d.DoanhThu);
        int DemTrangThai(string tt) => donTheoTrangThai.TryGetValue(tt, out var n) ? n : 0;

        // Dot 2 (B5): doanh thu thang truoc (don khong huy, thang truoc) —
        // query moi chi doc, Sum o client nhu vong 30 ngay (SQLite khong
        // dich duoc Sum tren decimal).
        var thangTruoc = thangNay.AddMonths(-1);
        var doanhThuThangTruoc = (await _db.Orders
            .Where(o => o.NgayDat >= thangTruoc && o.NgayDat < thangNay && o.TrangThai != OrderStatus.DaHuy)
            .Select(o => o.TongTien)
            .ToListAsync()).Sum();
        // Dot 2 (B5): chi so moi chi doc — het hang + doi tra cho duyet la
        // Count moi chi doc; gia tri ton kho tinh theo GIA BAN tu projection
        // chi doc (GiaKhuyenMai ?? Gia) * TonKho, khong phai gia von.
        var tongDon = await _db.Orders.CountAsync();
        var doiTraChoDuyet = await _db.ReturnRequests.CountAsync(r => r.TrangThai == "Chờ duyệt");
        var sanPhamHetHang = await _db.Products.CountAsync(p => p.TonKho == 0);
        var giaTriTonKho = (await _db.Products
            .Select(p => new { p.Gia, p.GiaKhuyenMai, p.TonKho })
            .ToListAsync()).Sum(p => (p.GiaKhuyenMai ?? p.Gia) * p.TonKho);

        return new DashboardViewModel
        {
            DoanhThuThang = donThang.Sum(o => o.TongTien),
            DonMoi = await _db.Orders.CountAsync(o => o.TrangThai == OrderStatus.ChoXacNhan),
            TongDon = tongDon,
            TongSanPham = await _db.Products.CountAsync(),
            TopBanChay = await _db.Products.Include(p => p.Images).OrderByDescending(p => p.LuotBan).Take(5).ToListAsync(),
            SapHetHang = await _db.Products.Include(p => p.Images).Where(p => p.TonKho <= 10).OrderBy(p => p.TonKho).Take(5).ToListAsync(),
            DonGanDay = await _db.Orders
                .Include(o => o.ChiTiet)
                .OrderByDescending(o => o.NgayDat).Take(8).ToListAsync(),
            DoanhThu7Ngay = doanhThu7Ngay,
            DoanhThu30Ngay = doanhThu30Ngay,
            DoanhThuTuan = doanhThuTuan,
            DonTheoTrangThai = donTheoTrangThai,
            KhachMoiThang = khachMoiThang,
            DoanhThuTuanTruoc = doanhThuTuanTruoc,
            TiLeDoiTuan = doanhThuTuanTruoc == 0 ? null : (doanhThuTuan - doanhThuTuanTruoc) / doanhThuTuanTruoc,
            DonChoXuLy = DemTrangThai(OrderStatus.ChoXacNhan) + DemTrangThai(OrderStatus.DaXacNhan) + DemTrangThai(OrderStatus.DangDongGoi),
            DonDaHuy = DemTrangThai(OrderStatus.DaHuy),
            // Dot 2 (B5): doanh thu hom nay = muc cuoi cua bang 30 ngay (da
            // co san, khong them query); gia tri don TB tu donThang da load.
            DoanhThuHomNay = doanhThu30Ngay.Count > 0 ? doanhThu30Ngay[^1].DoanhThu : 0,
            GiaTriDonTrungBinh = donThang.Count > 0 ? donThang.Sum(o => o.TongTien) / donThang.Count : 0,
            TiLeHuy = tongDon > 0 ? (decimal)DemTrangThai(OrderStatus.DaHuy) / tongDon : 0,
            DoiTraChoDuyet = doiTraChoDuyet,
            SanPhamHetHang = sanPhamHetHang,
            GiaTriTonKho = giaTriTonKho,
            DoanhThuThangTruoc = doanhThuThangTruoc,
            TiLeDoiThang = doanhThuThangTruoc == 0 ? null : (donThang.Sum(o => o.TongTien) - doanhThuThangTruoc) / doanhThuThangTruoc
        };
    }

    // --- San pham ---
    /// <summary>
    /// Dot 2 (B1): tim kiem + sap xep server. tuKhoa khop ten SP (substring,
    /// khong phan biet hoa/thuong) hoac ma SP (= Id, khi tuKhoa la so);
    /// sort chi doi OrderBy, khong doi du lieu. Phan trang 10/trang nhu cu.
    /// </summary>
    public async Task<IActionResult> Products(string? tuKhoa, string? sort, int page = 1)
    {
        if (page < 1) page = 1;
        tuKhoa = string.IsNullOrWhiteSpace(tuKhoa) ? null : tuKhoa.Trim();
        var query = _db.Products
            .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Images)
            .AsQueryable();
        if (tuKhoa is not null)
        {
            // Ma SP = Id: "15" vua khop ten chua "15" vua khop dung ma #15.
            if (int.TryParse(tuKhoa, out var maSp))
                query = query.Where(p => p.Id == maSp || p.Ten.Contains(tuKhoa));
            else
                query = query.Where(p => p.Ten.Contains(tuKhoa));
        }
        // GiaBan = GiaKhuyenMai ?? Gia (NotMapped) — EF dich ?? thanh
        // COALESCE tren SQLite nhung SQLite khong ORDER BY duoc decimal
        // (tien le Sprint 6) nen cast sang double truoc khi sap xep.
        query = sort switch
        {
            "ten-asc" => query.OrderBy(p => p.Ten),
            "ten-desc" => query.OrderByDescending(p => p.Ten),
            "gia-asc" => query.OrderBy(p => (double)(p.GiaKhuyenMai ?? p.Gia)).ThenBy(p => p.Id),
            "gia-desc" => query.OrderByDescending(p => (double)(p.GiaKhuyenMai ?? p.Gia)).ThenBy(p => p.Id),
            "ton-asc" => query.OrderBy(p => p.TonKho).ThenBy(p => p.Id),
            "ton-desc" => query.OrderByDescending(p => p.TonKho).ThenBy(p => p.Id),
            "ban-chay" => query.OrderByDescending(p => p.LuotBan).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.NgayTao).ThenBy(p => p.Id),
        };
        var tong = await query.CountAsync();
        var tongTrang = Math.Max(1, (int)Math.Ceiling(tong / (double)SoMucMoiTrang));
        if (page > tongTrang) page = tongTrang;
        var list = await query
            .Skip((page - 1) * SoMucMoiTrang).Take(SoMucMoiTrang).ToListAsync();
        ViewBag.TrangHienTai = page;
        ViewBag.TongTrang = tongTrang;
        ViewBag.TongMuc = tong;
        ViewBag.TuKhoa = tuKhoa;
        ViewBag.Sort = sort;
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
    public async Task<IActionResult> ProductForm(Product model, IFormFile? anhMoi)
    {
        // Navigation Category/Brand/Specs.Product khong co trong form nen khong
        // bind duoc — loai khoi ModelState, neu khong form luon bao loi.
        ModelState.Remove(nameof(Product.Category));
        ModelState.Remove(nameof(Product.Brand));
        ModelState.Remove("Specs.Product");
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Ten).ToListAsync();
        ViewBag.Brands = await _db.Brands.OrderBy(b => b.Ten).ToListAsync();
        if (!ModelState.IsValid) return View(model);

        // Validation nghiep vu server-side (DATA-01): chan gia/tồn kho am,
        // khuyen mai >= gia goc. [Range] tren model la lop 1 (ModelState);
        // day la lop 2, bat ca truong hop ModelState bi bypass.
        var loiGia = AdminValidation.KiemTraProduct(model.Gia, model.GiaKhuyenMai, model.TonKho);
        if (loiGia is not null)
        {
            TempData["Error"] = loiGia;
            return View(model);
        }

        // Kiem tra anh moi truoc khi luu: chi nhan anh, toi da 5MB.
        string? loiAnh = null;
        string? duoiAnh = null;
        if (anhMoi is not null && anhMoi.Length > 0)
        {
            duoiAnh = Path.GetExtension(anhMoi.FileName).ToLowerInvariant();
            var duoiHopLe = new[] { ".jpg", ".jpeg", ".png", ".webp", ".svg" };
            if (!duoiHopLe.Contains(duoiAnh))
                loiAnh = "Chỉ nhận file ảnh .jpg, .jpeg, .png, .webp hoặc .svg.";
            else if (anhMoi.Length > 5 * 1024 * 1024)
                loiAnh = "Ảnh tối đa 5MB.";
        }

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

        // Upload anh moi (neu hop le): ten file Guid duy nhat, ha anh chinh cu xuong.
        // model.Id luc nay da co (san pham moi vua duoc EF gan Id sau SaveChanges).
        if (anhMoi is not null && anhMoi.Length > 0 && loiAnh is null)
        {
            var tenFile = $"{Guid.NewGuid():N}{duoiAnh!}";
            var thuMuc = Path.Combine(_env.WebRootPath, "images");
            Directory.CreateDirectory(thuMuc);
            var duongDan = Path.Combine(thuMuc, tenFile);
            await using var stream = new FileStream(duongDan, FileMode.Create);
            await anhMoi.CopyToAsync(stream);

            var anhCu = await _db.ProductImages
                .Where(i => i.ProductId == model.Id && i.LaAnhChinh).ToListAsync();
            foreach (var anh in anhCu) anh.LaAnhChinh = false;
            _db.ProductImages.Add(new ProductImage
            {
                ProductId = model.Id,
                Url = $"/images/{tenFile}",
                LaAnhChinh = true,
                ThuTu = 0
            });
            await _db.SaveChangesAsync();
        }

        if (loiAnh is not null) TempData["Error"] = loiAnh;
        else TempData["Success"] = "Đã lưu sản phẩm.";
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
    /// <summary>
    /// Dot 3 (B3): xuat CSV don hang — projection chi doc cua query
    /// Orders() (loc trangThai/tuKhoa giong he trang cu, khong ghi DB).
    /// Cot: ma don / ngay dat / khach (ten truoc dau phay dau tien cua
    /// DiaChiGiao — dinh dang "{Ho ten}, {SDT}, {dia chi}") / trang thai
    /// / tong khach tra (TongTien + PhiShip = ThanhToan o view). So tien
    /// ghi so tran (khong dau phay ngan cach) de CSV dung cot. BOM UTF-8
    /// de Excel mo dung tieng Viet.
    /// </summary>
    public async Task<IActionResult> XuatCsvDonHang(string? trangThai, string? tuKhoa)
    {
        var query = _db.Orders.AsQueryable();
        if (!string.IsNullOrEmpty(trangThai))
            query = query.Where(o => o.TrangThai == trangThai);
        tuKhoa = string.IsNullOrWhiteSpace(tuKhoa) ? null : tuKhoa.Trim();
        if (tuKhoa is not null)
        {
            if (int.TryParse(tuKhoa, out var maDon))
                query = query.Where(o => o.Id == maDon || o.DiaChiGiao.Contains(tuKhoa));
            else
                query = query.Where(o => o.DiaChiGiao.Contains(tuKhoa));
        }
        var list = await query.OrderByDescending(o => o.NgayDat).ThenBy(o => o.Id)
            .Select(o => new { o.Id, o.NgayDat, o.DiaChiGiao, o.TrangThai, o.TongTien, o.PhiShip })
            .ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Mã đơn,Ngày đặt,Khách hàng,Trạng thái,Tổng tiền (đ)");
        foreach (var o in list)
        {
            var khach = (o.DiaChiGiao ?? string.Empty).Split(new[] { ", " }, 2, StringSplitOptions.None)[0];
            sb.AppendLine(string.Join(",",
                Csv(o.Id.ToString()),
                Csv(o.NgayDat.ToString("yyyy-MM-dd HH:mm")),
                Csv(khach),
                Csv(o.TrangThai),
                Csv((o.TongTien + o.PhiShip).ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
        }
        return TraFileCsv(sb.ToString(), "don-hang");
    }

    /// <summary>
    /// Dot 3 (B3): xuat CSV kho — projection chi doc (ten / ton / da ban
    /// / gia ban = GiaKhuyenMai ?? Gia). Khong ghi DB.
    /// </summary>
    public async Task<IActionResult> XuatCsvKho()
    {
        var list = await _db.Products.OrderBy(p => p.Ten)
            .Select(p => new { p.Ten, p.TonKho, p.LuotBan, GiaBan = p.GiaKhuyenMai ?? p.Gia })
            .ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Sản phẩm,Tồn kho,Đã bán,Giá bán (đ)");
        foreach (var p in list)
        {
            sb.AppendLine(string.Join(",",
                Csv(p.Ten),
                Csv(p.TonKho.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(p.LuotBan.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(p.GiaBan.ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
        }
        return TraFileCsv(sb.ToString(), "kho");
    }

    /// <summary>Dot 3 (B3): tra CSV kem BOM UTF-8 + header attachment.</summary>
    private FileContentResult TraFileCsv(string noiDung, string ten)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(noiDung)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"{ten}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>Dot 3 (B3): escape 1 o CSV (quote khi co , " xuong dong).</summary>
    private static string Csv(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    public async Task<IActionResult> Orders(string? trangThai, string? tuKhoa, string? sort, int page = 1)
    {
        var query = _db.Orders.Include(o => o.ChiTiet).AsQueryable();
        if (!string.IsNullOrEmpty(trangThai))
            query = query.Where(o => o.TrangThai == trangThai);
        tuKhoa = string.IsNullOrWhiteSpace(tuKhoa) ? null : tuKhoa.Trim();
        if (tuKhoa is not null)
        {
            if (int.TryParse(tuKhoa, out var maDon))
                query = query.Where(o => o.Id == maDon || o.DiaChiGiao.Contains(tuKhoa));
            else
                query = query.Where(o => o.DiaChiGiao.Contains(tuKhoa));
        }
        query = sort switch
        {
            "cu-nhat" => query.OrderBy(o => o.NgayDat).ThenBy(o => o.Id),
            // SQLite khong ORDER BY duoc decimal (tien le Sprint 6) nen
            // cast TongTien sang double truoc khi sap xep.
            "tong-desc" => query.OrderByDescending(o => (double)o.TongTien).ThenBy(o => o.Id),
            "tong-asc" => query.OrderBy(o => (double)o.TongTien).ThenBy(o => o.Id),
            _ => query.OrderByDescending(o => o.NgayDat).ThenBy(o => o.Id),
        };
        if (page < 1) page = 1;
        var tong = await query.CountAsync();
        var tongTrang = Math.Max(1, (int)Math.Ceiling(tong / (double)SoMucMoiTrang));
        if (page > tongTrang) page = tongTrang;
        ViewBag.TrangThai = trangThai;
        ViewBag.TuKhoa = tuKhoa;
        ViewBag.Sort = sort;
        ViewBag.TrangHienTai = page;
        ViewBag.TongTrang = tongTrang;
        ViewBag.TongMuc = tong;
        return View(await query
            .Skip((page - 1) * SoMucMoiTrang).Take(SoMucMoiTrang).ToListAsync());
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

    /// <summary>
    /// SEC-02 + SEC-04 — Cap lai ma tra cuu cho don khach vang lai (chi Admin,
    /// ep buoc boi [Authorize(Roles = "Admin")] o cap class).
    /// Quy trinh van hanh (khong gui ma tu dong — chua co email/SMS that):
    ///  1. Khach mat ma lien he shop, cung cap thong tin dat hang de doi
    ///     chieu: ten nguoi nhan, SDT, ma don, tong tien.
    ///  2. Admin mo trang chi tiet don, nhap ten + SDT khach cung cap vao
    ///     form cap lai. He doi chieu: SDT (chi so, khong phan biet khoang
    ///     trang/dau) va ho ten (khong phan biet hoa/thuong) PHAI KHOP voi
    ///     DiaChiGiao tren don — sai thi khong cap lai (SEC-04: tranh
    ///     Admin bam nham / ke gian doi ten don de cuop ma).
    ///     Don khong co email rieng (checkout vang lai chi luu ten/SDT/
    ///     dia chi trong DiaChiGiao) nen doi chieu dung ten + SDT.
    ///  3. Khop thi he sinh ma moi bang RandomNumberGenerator (128 bit),
    ///     luu SHA-256 hash + han 30 ngay, reset dem sai/khoa; ma cu het
    ///     hieu luc ngay lap tuc, khong co thoi gian chong cheo.
    ///  4. Admin chuyen ma moi cho khach qua kenh da xac minh (goi dien /
    ///     nhan tin vao SDT tren don). Ma moi chi hien trong thong bao
    ///     tren trang admin (TempData) — khong ghi log, khong gui tu dong.
    /// Don cua tai khoan khong cap lai duoc (khach dang nhap la xem duoc);
    /// don cu khong co token thi KHONG backfill tu dong — chi cap khi
    /// khach yeu cau va admin xac minh (xem quy trinh tren).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CapLaiMaTraCuu(int id, string hoTenDoiChieu, string soDienThoaiDoiChieu)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        if (order.UserId is not null)
        {
            TempData["Error"] = "Chỉ cấp lại mã tra cứu cho đơn khách vãng lai. Đơn này thuộc tài khoản — khách đăng nhập để xem đơn.";
            return RedirectToAction(nameof(OrderDetails), new { id });
        }
        // SEC-04: doi chieu chu don truoc khi cap lai. DiaChiGiao luu theo
        // dinh dang "{HoTen}, {SDT}, {DiaChi}" (xem OrdersController.Checkout).
        var diaChiParts = (order.DiaChiGiao ?? string.Empty)
            .Split(new[] { ", " }, 3, StringSplitOptions.None);
        var hoTenTrenDon = diaChiParts.Length > 0 ? diaChiParts[0].Trim() : string.Empty;
        var sdtTrenDon = diaChiParts.Length > 1 ? ChiLaySo(diaChiParts[1]) : string.Empty;
        var hoTenNhap = (hoTenDoiChieu ?? string.Empty).Trim();
        var sdtNhap = ChiLaySo(soDienThoaiDoiChieu ?? string.Empty);
        if (string.IsNullOrEmpty(hoTenNhap) || string.IsNullOrEmpty(sdtNhap)
            || sdtTrenDon.Length == 0
            || !string.Equals(hoTenNhap, hoTenTrenDon, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sdtNhap, sdtTrenDon, StringComparison.Ordinal))
        {
            TempData["Error"] = "Thông tin đối chiếu (họ tên / số điện thoại) không khớp với đơn — không cấp lại mã tra cứu. Kiểm tra lại với khách.";
            return RedirectToAction(nameof(OrderDetails), new { id });
        }
        var tokenMoi = OrderAccess.TaoGuestToken();
        order.GuestToken = OrderAccess.BamGuestToken(tokenMoi);
        order.GuestTokenHetHan = DateTime.Now.AddDays(OrderAccess.HanTokenNgay);
        OrderAccess.GhiNhanDung(order);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cấp mã tra cứu mới cho đơn #{id}: {tokenMoi}. Mã cũ đã hết hiệu lực — chỉ gửi mã mới cho khách qua kênh đã xác minh.";
        return RedirectToAction(nameof(OrderDetails), new { id });
    }

    /// <summary>Chi giu lai chu so (SDT doi chieu khong phan biet khoang trang/dau/gach).</summary>
    private static string ChiLaySo(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Admin doi trang thai don. Ep buoc bang chuyen hop le (Sprint 1):
    /// cam DaHuy -&gt; bat ky trang thai nao, cam DaGiao -&gt; DaHuy.
    /// Huy don chi hoan ton kho (Sprint 1); hoan tac diem/voucher la Sprint 2.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiTrangThai(int id, string trangThaiMoi)
    {
        if (!OrderStatus.LaTrangThaiHopLe(trangThaiMoi)) return BadRequest("Trạng thái không hợp lệ.");
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        var ketQua = await _orders.DoiTrangThaiAsync(id, trangThaiMoi);
        if (!ketQua.ThanhCong)
        {
            TempData["Error"] = ketQua.Loi ?? "Không đổi được trạng thái đơn.";
            return VeTrangSauTacVu(nameof(OrderDetails), new { id });
        }
        TempData["Success"] = ketQua.DaOTrangThaiDich
            ? $"Đơn #{id} đã ở trạng thái: {trangThaiMoi}."
            : $"Đơn #{id} đã chuyển sang: {trangThaiMoi}.";
        return VeTrangSauTacVu(nameof(OrderDetails), new { id });
    }

    // --- Voucher ---
    /// <summary>Dot 2 (B1): tim kiem theo ma voucher + phan trang 10/trang
    /// (truoc day Vouchers khong co phan trang). Ma luu dang viet hoa nen
    /// tuKhoa duoc viet hoa truoc khi khop.</summary>
    public async Task<IActionResult> Vouchers(string? tuKhoa, int page = 1)
    {
        if (page < 1) page = 1;
        tuKhoa = string.IsNullOrWhiteSpace(tuKhoa) ? null : tuKhoa.Trim().ToUpperInvariant();
        var query = _db.Vouchers.AsQueryable();
        if (tuKhoa is not null)
            query = query.Where(v => v.Ma.Contains(tuKhoa));
        var tong = await query.CountAsync();
        var tongTrang = Math.Max(1, (int)Math.Ceiling(tong / (double)SoMucMoiTrang));
        if (page > tongTrang) page = tongTrang;
        ViewBag.TuKhoa = tuKhoa;
        ViewBag.TrangHienTai = page;
        ViewBag.TongTrang = tongTrang;
        ViewBag.TongMuc = tong;
        return View(await query.OrderByDescending(v => v.NgayHetHan)
            .Skip((page - 1) * SoMucMoiTrang).Take(SoMucMoiTrang).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemVoucher(Voucher model)
    {
        // Sprint 3 — Admin: ModelState loi truoc day redirect im lang (khong
        // TempData) nen Admin khong biet vi sao voucher khong duoc them.
        if (!ModelState.IsValid)
        {
            var loiModel = ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
            TempData["Error"] = loiModel ?? "Thông tin voucher chưa hợp lệ.";
            return VeTrangSauTacVu(nameof(Vouchers));
        }
        // Validation nghiep vu server-side (AdminValidation, test duoc truc
        // tiep): chan so am, % > 100, ngay het han qua khu, SoLuong <= 0...
        var loi = AdminValidation.KiemTraVoucher(model);
        if (loi is not null)
        {
            TempData["Error"] = loi;
            return VeTrangSauTacVu(nameof(Vouchers));
        }
        model.Ma = model.Ma.Trim().ToUpper();
        if (await _db.Vouchers.AnyAsync(v => v.Ma == model.Ma))
        {
            TempData["Error"] = $"Mã {model.Ma} đã tồn tại.";
            return VeTrangSauTacVu(nameof(Vouchers));
        }
        // Voucher moi chua ai dung — khong tin DaDung client gui len.
        model.DaDung = 0;
        _db.Vouchers.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm voucher {model.Ma}.";
        return VeTrangSauTacVu(nameof(Vouchers));
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

    // --- Combo (Sprint 3 — B-7) ---
    // CRUD co ban: tao (toi da 2 mon trong form them, them mon sau bang
    // ThemMonCombo), sua thong tin + bat/tat, xoa. Gia combo (% giam) chi sua
    // o day — checkout luon tinh lai tu DB nen don cu khong doi.

    public async Task<IActionResult> Combos()
    {
        ViewBag.Products = await _db.Products.OrderBy(p => p.Ten).ToListAsync();
        return View(await _db.Combos
            .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .OrderByDescending(c => c.Id).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemCombo(string ten, string? moTa, decimal phanTramGiam,
        DateTime batDau, DateTime ketThuc, int? gioiHanSL,
        int productId1, int soLuong1, int? productId2, int soLuong2)
    {
        var loi = KiemTraCombo(ten, phanTramGiam, batDau, ketThuc, gioiHanSL);
        if (loi is null && (soLuong1 < 1 || productId1 <= 0))
            loi = "Món đầu tiên của combo chưa hợp lệ.";
        if (loi is null && productId2.HasValue && productId2.Value == productId1)
            loi = "Hai món trong combo không được trùng nhau.";
        if (loi is null && productId2.HasValue && soLuong2 < 1)
            loi = "Số lượng món thứ hai phải từ 1 trở lên.";
        if (loi is not null)
        {
            TempData["Error"] = loi;
            return RedirectToAction(nameof(Combos));
        }
        var sp1 = await _db.Products.FindAsync(productId1);
        Product? sp2 = null;
        if (productId2.HasValue)
        {
            sp2 = await _db.Products.FindAsync(productId2.Value);
            if (sp2 is null)
            {
                TempData["Error"] = "Sản phẩm thứ hai không tồn tại.";
                return RedirectToAction(nameof(Combos));
            }
        }
        if (sp1 is null)
        {
            TempData["Error"] = "Sản phẩm đầu tiên không tồn tại.";
            return RedirectToAction(nameof(Combos));
        }
        var combo = new Combo
        {
            Ten = ten.Trim(),
            MoTa = string.IsNullOrWhiteSpace(moTa) ? null : moTa.Trim(),
            PhanTramGiam = phanTramGiam,
            BatDau = batDau,
            KetThuc = ketThuc,
            GioiHanSL = gioiHanSL,
            IsActive = true,
            Items = new List<ComboItem>
            {
                new() { ProductId = productId1, SoLuong = soLuong1 },
            },
        };
        if (sp2 is not null)
            combo.Items.Add(new ComboItem { ProductId = productId2!.Value, SoLuong = soLuong2 });
        _db.Combos.Add(combo);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm combo {combo.Ten}.";
        return RedirectToAction(nameof(Combos));
    }

    public async Task<IActionResult> SuaCombo(int id)
    {
        var combo = await _db.Combos
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (combo is null) return NotFound();
        ViewBag.Products = await _db.Products.OrderBy(p => p.Ten).ToListAsync();
        return View(combo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuaCombo(int id, string ten, string? moTa, decimal phanTramGiam,
        DateTime batDau, DateTime ketThuc, int? gioiHanSL, bool isActive)
    {
        var combo = await _db.Combos.FindAsync(id);
        if (combo is null) return NotFound();
        var loi = KiemTraCombo(ten, phanTramGiam, batDau, ketThuc, gioiHanSL);
        if (loi is not null)
        {
            TempData["Error"] = loi;
            return RedirectToAction(nameof(SuaCombo), new { id });
        }
        // Khong cho ha GioiHanSL xuong duoi so bo da ban (se khoa combo vinh vien).
        if (gioiHanSL.HasValue && gioiHanSL.Value < combo.DaBan)
        {
            TempData["Error"] = $"Giới hạn không được nhỏ hơn số bộ đã bán ({combo.DaBan}).";
            return RedirectToAction(nameof(SuaCombo), new { id });
        }
        combo.Ten = ten.Trim();
        combo.MoTa = string.IsNullOrWhiteSpace(moTa) ? null : moTa.Trim();
        combo.PhanTramGiam = phanTramGiam;
        combo.BatDau = batDau;
        combo.KetThuc = ketThuc;
        combo.GioiHanSL = gioiHanSL;
        combo.IsActive = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật combo {combo.Ten}.";
        return RedirectToAction(nameof(Combos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatTatCombo(int id)
    {
        var combo = await _db.Combos.FindAsync(id);
        if (combo is null) return NotFound();
        combo.IsActive = !combo.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = combo.IsActive
            ? $"Đã bật combo {combo.Ten}."
            : $"Đã tắt combo {combo.Ten}.";
        return VeTrangSauTacVu(nameof(Combos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaCombo(int id)
    {
        var combo = await _db.Combos.FindAsync(id);
        if (combo is null) return NotFound();
        _db.Combos.Remove(combo);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa combo {combo.Ten}.";
        return RedirectToAction(nameof(Combos));
    }

    /// <summary>
    /// Dot 3 (B8): form them/xoa mon nhung trong trang SuaCombo (gui kem
    /// tuTrang=SuaCombo) thi quay lai SuaCombo thay vi ve danh sach;
    /// form o trang Combos (khong co tuTrang) giu nguyen ve Combos.
    /// </summary>
    private IActionResult VeCombosHoacSuaCombo(int comboId)
    {
        if (Request.HasFormContentType)
        {
            try
            {
                if (Request.Form.TryGetValue("tuTrang", out var t) && t == "SuaCombo")
                    return RedirectToAction(nameof(SuaCombo), new { id = comboId });
            }
            catch (InvalidOperationException) { /* khong co form hop le */ }
        }
        return RedirectToAction(nameof(Combos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemMonCombo(int comboId, int productId, int soLuong)
    {
        var combo = await _db.Combos
            .Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == comboId);
        if (combo is null) return NotFound();
        if (soLuong < 1 || await _db.Products.FindAsync(productId) is null)
        {
            TempData["Error"] = "Món thêm vào combo chưa hợp lệ.";
            return VeCombosHoacSuaCombo(comboId);
        }
        if (combo.Items.Any(i => i.ProductId == productId))
        {
            TempData["Error"] = "Sản phẩm này đã có trong combo.";
            return VeCombosHoacSuaCombo(comboId);
        }
        combo.Items.Add(new ComboItem { ComboId = comboId, ProductId = productId, SoLuong = soLuong });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm món vào combo.";
        return VeCombosHoacSuaCombo(comboId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaMonCombo(int id)
    {
        var item = await _db.ComboItems
            .Include(i => i.Combo).ThenInclude(c => c.Items)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound();
        // Combo rong mon thi TinhGiaComboAsync khong tinh duoc — giu lai it nhat 1 mon.
        if (item.Combo.Items.Count <= 1)
        {
            TempData["Error"] = "Combo phải có ít nhất 1 món — xóa cả combo thay vì xóa món cuối.";
            return VeCombosHoacSuaCombo(item.Combo.Id);
        }
        _db.ComboItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa món khỏi combo.";
        return VeCombosHoacSuaCombo(item.Combo.Id);
    }

    private static string? KiemTraCombo(string ten, decimal phanTramGiam,
        DateTime batDau, DateTime ketThuc, int? gioiHanSL)
    {
        if (string.IsNullOrWhiteSpace(ten)) return "Tên combo không được trống.";
        if (phanTramGiam <= 0 || phanTramGiam >= 100)
            return "% giảm phải lớn hơn 0 và nhỏ hơn 100.";
        if (batDau > ketThuc) return "Ngày bắt đầu phải trước ngày kết thúc.";
        // Sprint 3 — Admin: combo co ngay ket thuc da qua thi vua tao da het
        // han — chan ngay, khong de Admin tuong combo dang chay.
        if (ketThuc.Date < DateTime.Today) return "Ngày kết thúc không được trong quá khứ.";
        if (gioiHanSL is <= 0) return "Giới hạn số lượng phải lớn hơn 0 (hoặc để trống = không giới hạn).";
        return null;
    }

    // --- Kho (nhom D) ---
    public async Task<IActionResult> Kho()
    {
        ViewBag.NhapGanDay = await _db.StockIns
            .Include(s => s.Product)
            .OrderByDescending(s => s.NgayNhap).Take(10).ToListAsync();
        return View("Stock", await _db.Products.Include(p => p.Images).OrderBy(p => p.Ten).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NhapKho(int productId, int soLuong, string? ghiChu)
    {
        if (soLuong <= 0)
        {
            TempData["Error"] = "Số lượng nhập phải lớn hơn 0.";
            return VeTrangSauTacVu(nameof(Kho));
        }
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        _db.StockIns.Add(new StockIn
        {
            ProductId = productId,
            SoLuong = soLuong,
            GhiChu = ghiChu,
            NgayNhap = DateTime.Now
        });
        product.TonKho += soLuong;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã nhập {soLuong} chiếc {product.Ten} vào kho.";
        return VeTrangSauTacVu(nameof(Kho));
    }

    // --- Khach hang (nhom D) ---
    /// <summary>
    /// Dot 2 (B1): tim kiem + phan trang server 10/trang. Tong chi tieu
    /// van tinh in-memory nhu cu (TaiDuLieuKhachHangAsync) — chi them tim
    /// (ten/email/SDT, khong phan biet hoa/thuong) + phan trang tren ket
    /// qua da co, khong doi cach tinh.
    /// </summary>
    public async Task<IActionResult> KhachHang(string? tuKhoa = null, int page = 1)
    {
        if (page < 1) page = 1;
        tuKhoa = string.IsNullOrWhiteSpace(tuKhoa) ? null : tuKhoa.Trim();
        var list = await TaiDuLieuKhachHangAsync();
        if (tuKhoa is not null)
        {
            list = list.Where(k =>
                (k.User.HoTen ?? string.Empty).Contains(tuKhoa, StringComparison.OrdinalIgnoreCase)
                || (k.User.Email ?? string.Empty).Contains(tuKhoa, StringComparison.OrdinalIgnoreCase)
                || (k.User.SoDienThoai ?? string.Empty).Contains(tuKhoa, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var tong = list.Count;
        var tongTrang = Math.Max(1, (int)Math.Ceiling(tong / (double)SoMucMoiTrang));
        if (page > tongTrang) page = tongTrang;
        ViewBag.TuKhoa = tuKhoa;
        ViewBag.TrangHienTai = page;
        ViewBag.TongTrang = tongTrang;
        ViewBag.TongMuc = tong;
        return View("Customers", list.Skip((page - 1) * SoMucMoiTrang).Take(SoMucMoiTrang).ToList());
    }

    /// <summary>Logic load KhachHang goc, tach ra de KhachHang() + TongQuan() dung chung.</summary>
    private async Task<List<(ApplicationUser User, decimal TongChiTieu, string Hang, int Diem)>> TaiDuLieuKhachHangAsync()
    {
        var users = await _db.Users
            .Where(u => u.Email != "admin@techzone.vn")
            .OrderByDescending(u => u.NgayTao).ToListAsync();

        // Tong chi tieu HOP LE (cung dinh nghia LoyaltyService.TongChiTieuAsync):
        // don khong huy, tru so tien da hoan theo tung don, kep moi don >= 0.
        var donHopLe = await _db.Orders
            .Where(o => o.TrangThai != OrderStatus.DaHuy && o.UserId != null)
            .Select(o => new { o.Id, UserId = o.UserId!, o.TongTien })
            .ToListAsync();
        var hoanTheoDon = (await _db.Refunds
                .Select(r => new { r.OrderId, r.SoTien })
                .ToListAsync())
            .GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.SoTien));
        var chiTieu = donHopLe
            .GroupBy(o => o.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(o =>
                Math.Max(0, o.TongTien - (hoanTheoDon.TryGetValue(o.Id, out var h) ? h : 0))));

        // Diem hien tai: tong Earn tru tong Spend, gom theo UserId.
        var diem = await _db.PointTransactions
            .GroupBy(p => p.UserId)
            .ToDictionaryAsync(g => g.Key,
                g => g.Sum(p => p.Loai == "Earn" ? p.Diem : -p.Diem));

        var list = new List<(ApplicationUser User, decimal TongChiTieu, string Hang, int Diem)>();
        foreach (var u in users)
        {
            var tong = chiTieu.TryGetValue(u.Id, out var t) ? t : 0;
            // Hang tinh inline theo tong chi tieu (cung nguong nhom B + Bach kim B-6).
            var hang = LoyaltyService.HangTuChiTieu(tong);
            var d = diem.TryGetValue(u.Id, out var dm) ? dm : 0;
            list.Add((u, tong, hang, d));
        }
        return list;
    }

    // --- Duyet danh gia + tra loi cau hoi (nhom D) ---
    public async Task<IActionResult> Reviews()
    {
        ViewBag.DaDuyetGanDay = await _db.Reviews
            .Include(r => r.Product).Include(r => r.User)
            .Where(r => r.IsApproved)
            .OrderByDescending(r => r.NgayDanhGia).Take(10).ToListAsync();
        ViewBag.CauHoiChuaTraLoi = await _db.ProductQuestions
            .Include(q => q.Product).Include(q => q.User)
            .Where(q => q.TraLoi == null)
            .OrderBy(q => q.NgayHoi).ToListAsync();
        return View(await _db.Reviews
            .Include(r => r.Product).Include(r => r.User)
            .Where(r => !r.IsApproved)
            .OrderBy(r => r.NgayDanhGia).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DuyetDanhGia(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();
        review.IsApproved = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã duyệt đánh giá.";
        return VeTrangSauTacVu(nameof(Reviews));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnDanhGia(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();
        review.IsApproved = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã ẩn đánh giá.";
        return VeTrangSauTacVu(nameof(Reviews));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TraLoiCauHoi(int questionId, string traLoi)
    {
        if (string.IsNullOrWhiteSpace(traLoi))
        {
            TempData["Error"] = "Câu trả lời không được để trống.";
            return VeTrangSauTacVu(nameof(Reviews));
        }
        var question = await _db.ProductQuestions.FindAsync(questionId);
        if (question is null) return NotFound();
        question.TraLoi = traLoi.Trim();
        question.NguoiTraLoiId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        question.NgayTraLoi = DateTime.Now;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã trả lời câu hỏi.";
        return VeTrangSauTacVu(nameof(Reviews));
    }

    // --- Banner (nhom D) ---
    public async Task<IActionResult> Banners() =>
        View(await _db.Banners.OrderBy(b => b.ThuTu).ToListAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemBanner(string tieuDe, string anhUrl, string? linkUrl, int thuTu)
    {
        if (string.IsNullOrWhiteSpace(tieuDe) || string.IsNullOrWhiteSpace(anhUrl))
        {
            TempData["Error"] = "Tiêu đề và ảnh banner không được để trống.";
            return RedirectToAction(nameof(Banners));
        }
        if (!anhUrl.StartsWith("/images/") && !anhUrl.StartsWith("http"))
        {
            ModelState.AddModelError(nameof(anhUrl), "Ảnh banner phải là đường dẫn /images/... hoặc link http(s).");
            TempData["Error"] = "Ảnh banner phải là đường dẫn /images/... hoặc link http(s).";
            return RedirectToAction(nameof(Banners));
        }
        _db.Banners.Add(new Banner
        {
            TieuDe = tieuDe.Trim(),
            AnhUrl = anhUrl.Trim(),
            LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim(),
            ThuTu = thuTu,
            IsActive = true
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm banner.";
        return RedirectToAction(nameof(Banners));
    }

    /// <summary>
    /// Dot 3 (B8): sua banner — chi doi noi dung hien thi (tieu de / anh /
    /// link / thu tu). Khong doi IsActive (da co DoiTrangThaiBanner rieng).
    /// Validation giong ThemBanner.
    /// </summary>
    public async Task<IActionResult> SuaBanner(int id)
    {
        var banner = await _db.Banners.FindAsync(id);
        return banner is null ? NotFound() : View(banner);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuaBanner(int id, string tieuDe, string anhUrl, string? linkUrl, int thuTu)
    {
        var banner = await _db.Banners.FindAsync(id);
        if (banner is null) return NotFound();
        if (string.IsNullOrWhiteSpace(tieuDe) || string.IsNullOrWhiteSpace(anhUrl))
        {
            TempData["Error"] = "Tiêu đề và ảnh banner không được để trống.";
            return RedirectToAction(nameof(SuaBanner), new { id });
        }
        if (!anhUrl.StartsWith("/images/") && !anhUrl.StartsWith("http"))
        {
            TempData["Error"] = "Ảnh banner phải là đường dẫn /images/... hoặc link http(s).";
            return RedirectToAction(nameof(SuaBanner), new { id });
        }
        banner.TieuDe = tieuDe.Trim();
        banner.AnhUrl = anhUrl.Trim();
        banner.LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim();
        banner.ThuTu = thuTu;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật banner {banner.TieuDe}.";
        return RedirectToAction(nameof(Banners));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiTrangThaiBanner(int id)
    {
        var banner = await _db.Banners.FindAsync(id);
        if (banner is null) return NotFound();
        banner.IsActive = !banner.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = banner.IsActive ? "Đã bật banner." : "Đã tắt banner.";
        return VeTrangSauTacVu(nameof(Banners));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaBanner(int id)
    {
        var banner = await _db.Banners.FindAsync(id);
        if (banner is null) return NotFound();
        _db.Banners.Remove(banner);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa banner.";
        return RedirectToAction(nameof(Banners));
    }

    // --- Flash Sale slots (nhom D) ---
    public async Task<IActionResult> FlashSaleSlots()
    {
        ViewBag.Products = await _db.Products.OrderBy(p => p.Ten).ToListAsync();
        return View(await _db.FlashSaleSlots
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .OrderBy(s => s.GioBatDau).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemSlot(int gioBatDau, int gioKetThuc)
    {
        if (gioBatDau < 0 || gioKetThuc > 24 || gioBatDau >= gioKetThuc)
        {
            TempData["Error"] = "Giờ không hợp lệ: cần 0 ≤ giờ bắt đầu < giờ kết thúc ≤ 24.";
            return VeTrangSauTacVu(nameof(FlashSaleSlots));
        }
        // Chong lan [start, end) voi slot active hien co thi tu choi.
        var trung = await _db.FlashSaleSlots.AnyAsync(s => s.IsActive
            && gioBatDau < s.GioKetThuc && s.GioBatDau < gioKetThuc);
        if (trung)
        {
            TempData["Error"] = "Khung giờ này trùng với một khung Flash Sale đang bật.";
            return VeTrangSauTacVu(nameof(FlashSaleSlots));
        }
        _db.FlashSaleSlots.Add(new FlashSaleSlot
        {
            GioBatDau = gioBatDau,
            GioKetThuc = gioKetThuc,
            IsActive = true
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã thêm khung Flash Sale {gioBatDau}h – {gioKetThuc}h.";
        return VeTrangSauTacVu(nameof(FlashSaleSlots));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemSlotItem(int slotId, int productId, decimal giaSale)
    {
        var slot = await _db.FlashSaleSlots.FindAsync(slotId);
        if (slot is null) return NotFound();
        var product = await _db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        // Sprint 3 — Admin: truoc day chi chan giaSale <= 0; gia sale >= gia
        // ban (cau hinh sai, PriceCalculationService se bo qua) van lot.
        var loi = AdminValidation.KiemTraSlotItem(giaSale, product.GiaBan);
        if (loi is not null)
        {
            TempData["Error"] = loi;
            return RedirectToAction(nameof(FlashSaleSlots));
        }
        if (await _db.FlashSaleSlotItems.AnyAsync(i => i.SlotId == slotId && i.ProductId == productId))
        {
            TempData["Error"] = "Sản phẩm này đã có trong khung giờ này.";
            return RedirectToAction(nameof(FlashSaleSlots));
        }
        _db.FlashSaleSlotItems.Add(new FlashSaleSlotItem
        {
            SlotId = slotId,
            ProductId = productId,
            GiaSale = giaSale
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm sản phẩm vào khung Flash Sale.";
        return RedirectToAction(nameof(FlashSaleSlots));
    }

    /// <summary>
    /// Dot 2 (B6): bat/tat khung Flash Sale — chi doi co IsActive cua slot,
    /// gia sale van do PriceCalculationService tinh lai (khong doi gia,
    /// khong doi item trong slot). Slot tat thi ThemSlot khong tinh trung
    /// gio nua (check trung chi xet slot active).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatTatSlot(int id)
    {
        var slot = await _db.FlashSaleSlots.FindAsync(id);
        if (slot is null) return NotFound();
        slot.IsActive = !slot.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = slot.IsActive
            ? $"Đã bật khung Flash Sale {slot.Ten}."
            : $"Đã tắt khung Flash Sale {slot.Ten}.";
        return VeTrangSauTacVu(nameof(FlashSaleSlots));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaSlotItem(int id)
    {
        var item = await _db.FlashSaleSlotItems.FindAsync(id);
        if (item is null) return NotFound();
        _db.FlashSaleSlotItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa sản phẩm khỏi khung Flash Sale.";
        return RedirectToAction(nameof(FlashSaleSlots));
    }

    // --- Thong bao (nhom D) ---
    public async Task<IActionResult> ThongBao()
    {
        ViewBag.Users = await _db.Users
            .Where(u => u.Email != "admin@techzone.vn")
            .OrderBy(u => u.HoTen).ToListAsync();
        return View("Notifications", await _db.Notifications
            .Include(n => n.User)
            .OrderByDescending(n => n.NgayTao).Take(20).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuiThongBao(string userId, string tieuDe, string? noiDung)
    {
        if (string.IsNullOrWhiteSpace(tieuDe))
        {
            TempData["Error"] = "Tiêu đề thông báo không được để trống.";
            return VeTrangSauTacVu(nameof(ThongBao));
        }
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["Error"] = "Chưa chọn người nhận thông báo.";
            return VeTrangSauTacVu(nameof(ThongBao));
        }
        if (userId == "all")
        {
            var users = await _db.Users
                .Where(u => u.Email != "admin@techzone.vn").ToListAsync();
            foreach (var u in users)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = u.Id,
                    TieuDe = tieuDe.Trim(),
                    NoiDung = noiDung,
                    NgayTao = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã gửi thông báo tới {users.Count} khách hàng.";
        }
        else
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return NotFound();
            _db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                TieuDe = tieuDe.Trim(),
                NoiDung = noiDung,
                NgayTao = DateTime.Now
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã gửi thông báo tới {user.HoTen}.";
        }
        return VeTrangSauTacVu(nameof(ThongBao));
    }
}
