using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Test Dot 1 Admin-enhance ("Nhin la thay khac", 2026-11-12):
/// - (a) A3: chi so Dashboard moi (DonChoXuLy, DonDaHuy, DoanhThuTuanTruoc,
///   TiLeDoiTuan) tinh dung tu query da co — khong them query, khong doi
///   logic tien/kho. Don DaHuy khong tinh vao doanh thu.
/// - (b) A3: tuan truoc = 0 thi TiLeDoiTuan = null (khong chia cho 0).
/// - (c) File view/css/js chua dung marker Dot 1: badge partial, nut Xoa
///   .btn-danger, het TempData thua, het <th> trong / scope, het `hidden`
///   cung tren panel TongQuan, bo cot Phien ban, pill ADMIN + breadcrumb
///   "Khu quản trị", Esc tra focus, JS an panel sau load.
/// DB test: file tam + MigrateAsync; KHONG dung techzone.db that.
/// </summary>
[Collection("DbTests")]
public class AdminWave1Tests
{
    // ============ (a) Chi so moi tinh dung tu du lieu da co ============

    [Fact]
    public async Task Dashboard_ChiSoMoi_TinhDungTuDonDaCo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                // Ngay dung DateTime.Today (TaiDuLieuDashboardAsync so .Date).
                ctx.Orders.AddRange(
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today.AddDays(-3), TongTien = 1_000_000, DiaChiGiao = "A" },
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today.AddDays(-10), TongTien = 500_000, DiaChiGiao = "B" },
                    new Order { TrangThai = OrderStatus.ChoXacNhan, NgayDat = DateTime.Today.AddDays(-2), TongTien = 100_000, DiaChiGiao = "C" },
                    new Order { TrangThai = OrderStatus.DaHuy, NgayDat = DateTime.Today.AddDays(-1), TongTien = 999_000, DiaChiGiao = "D" },
                    new Order { TrangThai = OrderStatus.DangDongGoi, NgayDat = DateTime.Today.AddDays(-5), TongTien = 200_000, DiaChiGiao = "E" });
                await ctx.SaveChangesAsync();
            }

            DashboardViewModel vm;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                var ketQua = await admin.Dashboard();
                var view = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(ketQua);
                vm = Assert.IsType<DashboardViewModel>(view.Model);
            }

            Assert.Equal(5, vm.TongDon);
            // Cho xu ly = ChoXacNhan (1) + DangDongGoi (1); DaHuy rieng.
            Assert.Equal(2, vm.DonChoXuLy);
            Assert.Equal(1, vm.DonDaHuy);
            Assert.Equal(1, vm.DonMoi);
            // Tuan nay = ngay -3 (1.000.000) + -2 (100.000) + -5 (200.000);
            // don DaHuy 999.000 KHONG tinh. Tuan truoc = ngay -10 (500.000).
            Assert.Equal(1_300_000, vm.DoanhThuTuan);
            Assert.Equal(500_000, vm.DoanhThuTuanTruoc);
            Assert.NotNull(vm.TiLeDoiTuan);
            Assert.Equal(1.6m, vm.TiLeDoiTuan!.Value);
            // Doanh thu 30 ngay van du 30 muc, so cu khong doi.
            Assert.Equal(30, vm.DoanhThu30Ngay.Count);
            Assert.Equal(1_300_000, vm.DoanhThu30Ngay.TakeLast(7).Sum(d => d.DoanhThu));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (b) Tuan truoc = 0 -> delta null ============

    [Fact]
    public async Task Dashboard_TuanTruocBang0_TiLeDoiTuanNull()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                ctx.Orders.Add(new Order
                {
                    TrangThai = OrderStatus.DaGiao,
                    NgayDat = DateTime.Today.AddDays(-1),
                    TongTien = 700_000,
                    DiaChiGiao = "A",
                });
                await ctx.SaveChangesAsync();
            }

            DashboardViewModel vm;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                var view = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await admin.Dashboard());
                vm = Assert.IsType<DashboardViewModel>(view.Model);
            }

            Assert.Equal(700_000, vm.DoanhThuTuan);
            Assert.Equal(0, vm.DoanhThuTuanTruoc);
            Assert.Null(vm.TiLeDoiTuan);
            Assert.Equal(0, vm.DonChoXuLy);
            Assert.Equal(0, vm.DonDaHuy);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (c) Marker file Dot 1 ============

    [Fact]
    public void Wave1_MarkerFile_DuVaDung()
    {
        var root = ProjectRoot();
        string Doc(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

        var css = Doc("wwwroot", "css", "admin.css");
        Assert.Contains(".badge-xong", css);
        Assert.Contains(".badge-vang", css);
        Assert.Contains(".badge-xanh", css);
        Assert.Contains(".admin-pill", css);
        Assert.Contains(".admin-eyebrow", css);
        Assert.Contains(".stat-card--tien", css);
        Assert.Contains(".stat-delta", css);
        Assert.Contains(".filter-bar", css);
        Assert.Contains(".chip-active", css);
        Assert.Contains(".low-stock-soft", css);
        Assert.Contains(".summary-row.total", css);
        Assert.Contains("BANG MAU TRANG THAI", css);

        var layout = Doc("Views", "Shared", "_AdminLayout.cshtml");
        Assert.Contains("ADMIN", layout);
        Assert.Contains("Khu quản trị", layout);
        Assert.Contains("admin-eyebrow", layout);

        var badge = Doc("Views", "Shared", "_TrangThaiBadge.cshtml");
        Assert.Contains("Chờ xác nhận", badge);
        Assert.Contains("badge-cho", badge);
        Assert.Contains("badge-vang", badge);

        // A2: trang thai hien qua partial, khong con chu thuong o cac bang.
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Orders.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Customers.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Banners.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Combos.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Notifications.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Returns", "AdminIndex.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "Dashboard.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "TongQuan.cshtml"));
        Assert.Contains("_TrangThaiBadge", Doc("Views", "Admin", "OrderDetails.cshtml"));

        // A8: Xoa do, Sua vien; khong con nut Xoa dang link-button.
        foreach (var f in new[] { "Products", "Vouchers", "Banners", "Combos", "FlashSaleSlots" })
        {
            var view = Doc("Views", "Admin", f + ".cshtml");
            Assert.Contains("btn btn-danger btn-sm", view);
            Assert.DoesNotContain("link-button\" onclick=\"return confirm('Xóa", view);
        }
        Assert.Contains("btn btn-secondary btn-sm", Doc("Views", "Admin", "Products.cshtml"));

        // C6: het TempData thua (layout da render 1 lan co role="alert").
        Assert.DoesNotContain("TempData", Doc("Views", "Admin", "Combos.cshtml"));
        Assert.DoesNotContain("TempData", Doc("Views", "Admin", "SuaCombo.cshtml"));

        // C2: het <th> trong / thieu scope trong toan bo view Admin.
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Views", "Admin"), "*.cshtml")
            .Concat(Directory.GetFiles(Path.Combine(root, "Views", "Returns"), "AdminIndex.cshtml")))
        {
            var view = File.ReadAllText(f);
            Assert.DoesNotContain("<th></th>", view);
            Assert.DoesNotContain("<th>", view);
        }

        // C6: panel TongQuan het `hidden` cung; JS an sau khi load.
        var tq = Doc("Views", "Admin", "TongQuan.cshtml");
        Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Sản phẩm\" hidden", tq);
        Assert.Contains("stat-delta", tq);
        Assert.Contains("DonChoXuLy", tq);
        Assert.Contains("DonDaHuy", tq);
        var js = Doc("wwwroot", "js", "admin.js");
        Assert.Contains("toggle.focus()", js);
        Assert.Contains("chonTab('dashboard')", js);

        // A5: bo cot Phien ban trong o OrderDetails.
        Assert.DoesNotContain("Phiên bản", Doc("Views", "Admin", "OrderDetails.cshtml"));

        // Nguong kho chot Dot 1: <= 10 to nhat, = 0 badge "Het hang".
        Assert.Contains("low-stock-soft", Doc("Views", "Admin", "Stock.cshtml"));
        Assert.Contains("Hết hàng", Doc("Views", "Admin", "Stock.cshtml"));
        Assert.DoesNotContain("TonKho < 5", Doc("Views", "Admin", "Stock.cshtml"));
    }

    // ================= Helpers (theo pattern AdminTongQuanTests) =================

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
