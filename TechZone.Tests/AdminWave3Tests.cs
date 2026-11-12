using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Test Dot 3 Admin-enhance ("Chuyen sau + mobile", 2026-11-12):
/// - (a) B3: XuatCsvDonHang / XuatCsvKho — header + dong dung so that,
///   loc theo trang thai/tuKhoa, escape CSV (dau phay / dau " trong ten
///   khach), tong = TongTien + PhiShip, gia kho = GiaKhuyenMai ?? Gia.
/// - (b) B8: SuaBanner GET/POST — chi doi noi dung hien thi, IsActive
///   khong doi; du lieu loi thi khong luu.
/// - (c) B8: ThemMonCombo / XoaMonCombo gui kem tuTrang=SuaCombo thi
///   quay lai SuaCombo; khong co tuTrang thi ve Combos nhu cu.
/// - (d) Marker file Dot 3: B3/B4/C1/C3/C4/C5/A7/B8 co mat o dung file.
/// DB test: file tam + MigrateAsync; KHONG dung techzone.db that.
/// </summary>
[Collection("DbTests")]
public class AdminWave3Tests
{
    // ============ (a) B3: CSV don hang ============

    [Fact]
    public async Task XuatCsvDonHang_Header_DongDung_TongBangThanhToan_LocTrangThai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                ctx.Orders.AddRange(
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = new DateTime(2026, 11, 12, 9, 15, 0), TongTien = 1_000_000, PhiShip = 20_000, DiaChiGiao = "Nguyễn Test, 0901111222, Hà Nội" },
                    new Order { TrangThai = OrderStatus.ChoXacNhan, NgayDat = new DateTime(2026, 11, 12, 10, 30, 0), TongTien = 500_000, PhiShip = 0, DiaChiGiao = "Trần Test, 0901333444, TP.HCM" },
                    // Ten khach co dau " — phai escape thanh "" trong CSV.
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = new DateTime(2026, 11, 11, 8, 0, 0), TongTien = 300_000, PhiShip = 0, DiaChiGiao = "Lê \"Biệt danh\" Test, 0901555666, Đà Nẵng" });
                await ctx.SaveChangesAsync();
            }

            string csvHet, csvLoc;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                var kq = Assert.IsType<FileContentResult>(await admin.XuatCsvDonHang(null, null));
                Assert.Equal("text/csv; charset=utf-8", kq.ContentType);
                Assert.StartsWith("don-hang-", kq.FileDownloadName);
                csvHet = Encoding.UTF8.GetString(kq.FileContents).TrimStart('\uFEFF');
                var dong = csvHet.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(4, dong.Length); // header + 3 don
                Assert.Equal("Mã đơn,Ngày đặt,Khách hàng,Trạng thái,Tổng tiền (đ)", dong[0].Trim());
                // Don moi nhat len dau (Tran 10:30), roi Nguyen (09:15);
                // tong Nguyen = TongTien + PhiShip = 1.020.000.
                Assert.Contains("Trần Test", dong[1]);
                Assert.Contains(",500000", dong[1]);
                Assert.Contains("Nguyễn Test", dong[2]);
                Assert.Contains(",1020000", dong[2]);
                Assert.Contains("Đã giao", dong[2]);
                // Ten co dau " duoc escape "".
                Assert.Contains("Lê \"\"Biệt danh\"\" Test", csvHet);

                // Loc dung trang thai DaGiao -> 2 don.
                var kqLoc = Assert.IsType<FileContentResult>(await admin.XuatCsvDonHang(OrderStatus.DaGiao, null));
                csvLoc = Encoding.UTF8.GetString(kqLoc.FileContents).TrimStart('\uFEFF');
                Assert.Equal(3, csvLoc.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.DoesNotContain("Trần Test", csvLoc);

                // Tim theo SDT -> dung 1 don.
                var kqTim = Assert.IsType<FileContentResult>(await admin.XuatCsvDonHang(null, "0901333444"));
                var csvTim = Encoding.UTF8.GetString(kqTim.FileContents).TrimStart('\uFEFF');
                Assert.Equal(2, csvTim.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.Contains("Trần Test", csvTim);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task XuatCsvKho_GiaBanDungKhuyenMai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var cat = new Category { Ten = "Test Cat", Slug = "test-cat" };
                var brand = new Brand { Ten = "Test Brand" };
                ctx.Categories.Add(cat); ctx.Brands.Add(brand);
                await ctx.SaveChangesAsync();
                ctx.Products.AddRange(
                    new Product { Ten = "W3 SP KM", Gia = 2_000_000, GiaKhuyenMai = 1_500_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 3, LuotBan = 7, NgayTao = DateTime.Today },
                    // Ten co dau phay — phai quote trong CSV.
                    new Product { Ten = "W3 SP, Thường", Gia = 500_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 0, LuotBan = 0, NgayTao = DateTime.Today });
                await ctx.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                var kq = Assert.IsType<FileContentResult>(await admin.XuatCsvKho());
                Assert.Equal("text/csv; charset=utf-8", kq.ContentType);
                Assert.StartsWith("kho-", kq.FileDownloadName);
                var csv = Encoding.UTF8.GetString(kq.FileContents).TrimStart('\uFEFF');
                var dong = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(3, dong.Length);
                Assert.Equal("Sản phẩm,Tồn kho,Đã bán,Giá bán (đ)", dong[0].Trim());
                // Gia ban = GiaKhuyenMai (1.500.000), khong phai Gia goc.
                Assert.Contains("W3 SP KM,3,7,1500000", csv);
                Assert.Contains("\"W3 SP, Thường\",0,0,500000", csv);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (b) B8: SuaBanner ============

    [Fact]
    public async Task SuaBanner_DoiNoiDung_KhongDoiIsActive_LoiKhongLuu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            int bannerId;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var b = new Banner { TieuDe = "W3 Banner Cu", AnhUrl = "/images/a.jpg", LinkUrl = "/cu", ThuTu = 1, IsActive = true };
                ctx.Banners.Add(b);
                await ctx.SaveChangesAsync();
                bannerId = b.Id;
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                admin.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
                admin.TempData = new TempDataDictionary(admin.HttpContext, new FakeTempDataProvider());

                var get = Assert.IsType<ViewResult>(await admin.SuaBanner(bannerId));
                Assert.Equal("W3 Banner Cu", Assert.IsType<Banner>(get.Model).TieuDe);
                Assert.IsType<NotFoundResult>(await admin.SuaBanner(999999));

                var kq = Assert.IsType<RedirectToActionResult>(
                    await admin.SuaBanner(bannerId, "W3 Banner Moi", "/images/b.jpg", "/moi", 5));
                Assert.Equal("Banners", kq.ActionName);
                var sau = (await ctx.Banners.FindAsync(bannerId))!;
                Assert.Equal("W3 Banner Moi", sau.TieuDe);
                Assert.Equal("/images/b.jpg", sau.AnhUrl);
                Assert.Equal("/moi", sau.LinkUrl);
                Assert.Equal(5, sau.ThuTu);
                Assert.True(sau.IsActive); // IsActive khong doi.

                // Tieu de trong -> khong luu, quay lai SuaBanner.
                var kqLoi = Assert.IsType<RedirectToActionResult>(
                    await admin.SuaBanner(bannerId, "   ", "/images/c.jpg", null, 1));
                Assert.Equal("SuaBanner", kqLoi.ActionName);
                Assert.Equal("W3 Banner Moi", (await ctx.Banners.FindAsync(bannerId))!.TieuDe);

                // Anh sai dinh dang -> khong luu.
                var kqLoi2 = Assert.IsType<RedirectToActionResult>(
                    await admin.SuaBanner(bannerId, "Khac", "ftp://x/y.jpg", null, 1));
                Assert.Equal("SuaBanner", kqLoi2.ActionName);
                Assert.Equal("/images/b.jpg", (await ctx.Banners.FindAsync(bannerId))!.AnhUrl);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (c) B8: ThemMon/XoaMon quay lai SuaCombo ============

    [Fact]
    public async Task ThemMonCombo_TuTrangSuaCombo_QuayLaiSuaCombo_KhongCoThiVeCombos()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            int comboId, sp1Id, sp2Id;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var cat = new Category { Ten = "Test Cat", Slug = "test-cat" };
                var brand = new Brand { Ten = "Test Brand" };
                ctx.Categories.Add(cat); ctx.Brands.Add(brand);
                await ctx.SaveChangesAsync();
                var sp1 = new Product { Ten = "W3 SP1", Gia = 1_000_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 5, NgayTao = DateTime.Today };
                var sp2 = new Product { Ten = "W3 SP2", Gia = 2_000_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 5, NgayTao = DateTime.Today };
                ctx.Products.AddRange(sp1, sp2);
                await ctx.SaveChangesAsync();
                sp1Id = sp1.Id; sp2Id = sp2.Id;
                var combo = new Combo { Ten = "W3 Combo", PhanTramGiam = 10, BatDau = DateTime.Today, KetThuc = DateTime.Today.AddDays(30), IsActive = true, Items = new List<ComboItem> { new() { ProductId = sp1Id, SoLuong = 1 } } };
                ctx.Combos.Add(combo);
                await ctx.SaveChangesAsync();
                comboId = combo.Id;
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                // Khong co form tuTrang -> ve Combos nhu cu.
                admin.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
                admin.TempData = new TempDataDictionary(admin.HttpContext, new FakeTempDataProvider());
                var kq1 = Assert.IsType<RedirectToActionResult>(await admin.ThemMonCombo(comboId, sp2Id, 2));
                Assert.Equal("Combos", kq1.ActionName);
                Assert.Equal(2, (await ctx.Combos.Include(c => c.Items).FirstAsync(c => c.Id == comboId)).Items.Count);

                // Co form tuTrang=SuaCombo -> quay lai SuaCombo (id dung).
                var httpSua = new DefaultHttpContext();
                httpSua.Request.ContentType = "application/x-www-form-urlencoded";
                httpSua.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["tuTrang"] = "SuaCombo" });
                admin.ControllerContext = new ControllerContext { HttpContext = httpSua };
                admin.TempData = new TempDataDictionary(admin.HttpContext, new FakeTempDataProvider());
                var itemMoi = (await ctx.ComboItems.FirstAsync(i => i.ComboId == comboId && i.ProductId == sp2Id)).Id;
                var kq2 = Assert.IsType<RedirectToActionResult>(await admin.XoaMonCombo(itemMoi));
                Assert.Equal("SuaCombo", kq2.ActionName);
                Assert.Equal(comboId, kq2.RouteValues!["id"]);
                Assert.Single((await ctx.Combos.Include(c => c.Items).FirstAsync(c => c.Id == comboId)).Items);

                // Xoa mon cuoi bi chan (combo phai co it nhat 1 mon) — van o SuaCombo.
                var itemCuoi = (await ctx.ComboItems.FirstAsync(i => i.ComboId == comboId)).Id;
                var kq3 = Assert.IsType<RedirectToActionResult>(await admin.XoaMonCombo(itemCuoi));
                Assert.Equal("SuaCombo", kq3.ActionName);
                Assert.Single((await ctx.Combos.Include(c => c.Items).FirstAsync(c => c.Id == comboId)).Items);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (d) Marker file Dot 3 ============

    [Fact]
    public void Wave3_MarkerFile_DuVaDung()
    {
        var root = ProjectRoot();
        string Doc(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

        var ctrl = Doc("Controllers", "AdminController.cs");
        // B3: endpoint CSV chi doc.
        Assert.Contains("XuatCsvDonHang", ctrl);
        Assert.Contains("XuatCsvKho", ctrl);
        Assert.Contains("text/csv", ctrl);
        Assert.Contains("Mã đơn,Ngày đặt,Khách hàng,Trạng thái,Tổng tiền (đ)", ctrl);
        Assert.Contains("Sản phẩm,Tồn kho,Đã bán,Giá bán (đ)", ctrl);
        // B8: SuaBanner + quay lai SuaCombo.
        Assert.Contains("SuaBanner", ctrl);
        Assert.Contains("VeCombosHoacSuaCombo", ctrl);
        Assert.Contains("\"SuaCombo\"", ctrl);

        // B3: nut o Orders + Kho (+ TongQuan tab Don/Kho).
        Assert.Contains("XuatCsvDonHang", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("Xuất CSV", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("XuatCsvKho", Doc("Views", "Admin", "Stock.cshtml"));
        Assert.Contains("Xuất CSV", Doc("Views", "Admin", "Stock.cshtml"));
        var tq = Doc("Views", "Admin", "TongQuan.cshtml");
        Assert.Contains("XuatCsvDonHang", tq);
        Assert.Contains("XuatCsvKho", tq);

        // B4: drawer partial + trigger + aria-modal + focus trap.
        Assert.Contains("_OrderDrawer", Doc("Views", "Shared", "_AdminLayout.cshtml"));
        var drawer = Doc("Views", "Shared", "_OrderDrawer.cshtml");
        Assert.Contains("role=\"dialog\"", drawer);
        Assert.Contains("aria-modal=\"true\"", drawer);
        Assert.Contains("order-drawer", drawer);
        Assert.Contains("data-order-drawer", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("data-order-drawer", tq);
        var js = Doc("wwwroot", "js", "admin.js");
        Assert.Contains("order-drawer", js);
        Assert.Contains("Escape", js);
        Assert.Contains("OrderDetails", js);
        Assert.Contains("querySelectorAll('form')", js);

        // C1: tablist ARIA + roving tabindex + focus panel.
        Assert.Contains("aria-controls=\"tq-dashboard\"", tq);
        Assert.Contains("aria-labelledby=\"tq-tab-dashboard\"", tq);
        Assert.Contains("tabindex=\"-1\"", tq);
        Assert.Contains("tabindex=\"0\"", tq);
        Assert.Contains("ArrowRight", js);
        Assert.Contains("Home", js);
        Assert.Contains("tabIndex = active ? 0 : -1", js);
        Assert.Contains("focusPanel", js);

        // C3: bang 30 ngay o ca 2 chart.
        var dash = Doc("Views", "Admin", "Dashboard.cshtml");
        Assert.Contains("chart-data-table", dash);
        Assert.Contains("DoanhThu30Ngay", dash);
        Assert.Contains("Doanh thu từng ngày", dash);
        Assert.Contains("chart-data-table", tq);
        Assert.Contains("Doanh thu từng ngày", tq);
        Assert.Contains("tabindex=\"0\"", dash); // chart Dashboard goc (A4 Dot 2) van con

        // C4 + C5: CSS co cham 44px + card mobile + tab cuon + chart vua 390.
        var css = Doc("wwwroot", "css", "admin.css");
        Assert.Contains("min-height: 44px", css);
        Assert.Contains(".table--card-mobile", css);
        Assert.Contains("data-label", css);
        Assert.Contains("scroll-snap-type: x mandatory", css);
        Assert.Contains(".chart { overflow-x: visible", css);
        Assert.Contains("table--card-mobile", Doc("Views", "Admin", "Products.cshtml"));
        Assert.Contains("table--card-mobile", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("table--card-mobile", tq);
        Assert.Contains("data-label=\"Mã đơn\"", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("data-label=\"Sản phẩm\"", Doc("Views", "Admin", "Products.cshtml"));

        // A7: form card 2 cot o 5 form cu + 2 form TongQuan.
        Assert.Contains("admin-form-grid", css);
        Assert.Contains("admin-form-card", css);
        foreach (var f in new[] { "Vouchers", "Combos", "Banners", "Notifications", "ProductForm" })
        {
            var v = Doc("Views", "Admin", f + ".cshtml");
            Assert.Contains("admin-form-grid", v);
            Assert.Contains("admin-form-card", v);
        }
        Assert.Contains("admin-form-grid", tq);
        Assert.Contains("Giá trị", Doc("Views", "Admin", "Vouchers.cshtml"));
        Assert.Contains("Điều kiện", Doc("Views", "Admin", "Vouchers.cshtml"));
        Assert.Contains("Thời gian", Doc("Views", "Admin", "Vouchers.cshtml"));

        // B8: nut Sua banner + form them mon o SuaCombo + view SuaBanner.
        Assert.Contains("SuaBanner", Doc("Views", "Admin", "Banners.cshtml"));
        Assert.Contains("SuaBanner", tq);
        Assert.Contains("ThemMonCombo", Doc("Views", "Admin", "SuaCombo.cshtml"));
        Assert.Contains("XoaMonCombo", Doc("Views", "Admin", "SuaCombo.cshtml"));
        Assert.Contains("tuTrang\" value=\"SuaCombo\"", Doc("Views", "Admin", "SuaCombo.cshtml"));
        var suaBanner = Doc("Views", "Admin", "SuaBanner.cshtml");
        Assert.Contains("SuaBanner", suaBanner);
        Assert.Contains("admin-form-grid", suaBanner);
    }

    // ================= Helpers (theo pattern AdminWave2Tests) =================

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TechZone", "TechZone.csproj")))
                return Path.Combine(dir.FullName, "TechZone");
            if (File.Exists(Path.Combine(dir.FullName, "TechZone.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Khong tim thay TechZone.csproj tu " + AppContext.BaseDirectory);
    }

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<NotificationService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = "admin@techzone.vn",
                ["DemoAccounts:AdminPassword"] = "AdminTest123",
                ["DemoAccounts:CustomerEmail"] = "khach@techzone.vn",
                ["DemoAccounts:CustomerPassword"] = "KhachTest123",
            }).Build());
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }
}
