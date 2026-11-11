using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
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
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test trang Admin tong hop (Admin/TongQuan) — 12 section trong 1 trang.
/// - (a) TongQuan() tra ViewResult + TongQuanViewModel du 12 section, du lieu
///   that tu seed (Xiaomi 15 5G, GiaBan 12.990.000d); view .cshtml chua du
///   tieu de 12 section, khong du lieu gia / khong goi y tai khoan demo.
/// - (b)/(c) Auth: AdminController khoa [Authorize(Roles="Admin")] o cap
///   class, TongQuan khong co [AllowAnonymous] — anonymous bi Challenge ve
///   /Account/Login, user non-admin bi day ve /Account/Login (AccessDeniedPath
///   cau hinh trong Program.cs); kiem chung them bang HTTP that (boot app).
/// - (d)-(f) POST tu TongQuan (hidden tuTrang=TongQuan) quay lai TongQuan.
/// - (g) POST khong co tuTrang quay ve trang cu nhu truoc (hoi quy).
/// - Dashboard()/KhachHang() sau khi tach helper van tra dung view/model cu.
/// - HTTP: boot app that (dotnet run + curl, theo pattern MigrationTests),
///   login admin that qua form + antiforgery -> GET /Admin/TongQuan 200.
/// DB test: file tam + MigrateAsync; KHONG dung techzone.db that.
/// </summary>
[Collection("DbTests")]
public class AdminTongQuanTests
{
    private readonly ITestOutputHelper _out;

    public AdminTongQuanTests(ITestOutputHelper output) => _out = output;

    // ============ (a) TongQuan: du 12 section, du lieu that tu seed ============

    [Fact]
    public async Task TongQuan_SeedDayDu_TraVeDu12Section_VoiDuLieuThat()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedFullAsync(services);

            TongQuanViewModel vm;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                GanTempData(admin);
                var ketQua = await admin.TongQuan();
                var view = Assert.IsType<ViewResult>(ketQua);
                Assert.True(view.ViewName is null or "TongQuan");
                vm = Assert.IsType<TongQuanViewModel>(view.Model);
            }

            // 1. Dashboard (dung helper chung) + don gan nhat.
            Assert.NotNull(vm.Dashboard);
            Assert.Equal(3, vm.Dashboard.TongDon);
            Assert.Equal(13, vm.Dashboard.TongSanPham);
            Assert.Equal(30, vm.Dashboard.DoanhThu30Ngay.Count);
            Assert.NotNull(vm.Dashboard.TopBanChay);
            Assert.NotNull(vm.Dashboard.SapHetHang);
            Assert.Equal(3, vm.DonGanDay.Count);
            Assert.Equal(3, vm.DonTheoTrangThai.Values.Sum());

            // 2. San pham: SP that + gia that (GiaKhuyenMai ?? Gia).
            Assert.Equal(13, vm.Products.Count);
            var xiaomi = Assert.Single(vm.Products, p => p.Ten == "Xiaomi 15 5G");
            Assert.Equal(12_990_000, xiaomi.GiaBan);
            Assert.Equal(15_990_000, xiaomi.Gia);
            Assert.Equal(24, xiaomi.TonKho);

            // 3-4. Don hang + khach hang (tru admin).
            Assert.Equal(3, vm.DonGanDay.Count);
            Assert.Single(vm.KhachHang);
            Assert.Equal("khach@techzone.vn", vm.KhachHang[0].User.Email);

            // 5-8. Voucher / Combo / Flash Sale / Banner.
            Assert.Equal(5, vm.Vouchers.Count);
            Assert.Contains(vm.Vouchers, v => v.Ma == "TECHZONE500");
            Assert.Equal(2, vm.Combos.Count);
            Assert.Equal(3, vm.FlashSaleSlots.Count);
            Assert.Equal(2, vm.Banners.Count);

            // 9. Danh gia & hoi dap: seed co 0 cho duyet (empty state) + 4 da duyet + 1 cau hoi chua tra loi.
            Assert.Empty(vm.ReviewsChoDuyet);
            Assert.Equal(4, vm.ReviewsDaDuyetGanDay.Count);
            Assert.Single(vm.CauHoiChuaTraLoi);

            // 10. Kho: 13 SP sap theo ten + 3 lan nhap kho mau.
            Assert.Equal(13, vm.SanPhamKho.Count);
            Assert.Equal(3, vm.NhapKhoGanDay.Count);

            // 11. Thong bao: 2 thong bao mau + 1 nguoi nhan (tru admin).
            Assert.Equal(2, vm.ThongBaoGanDay.Count);
            Assert.Single(vm.NguoiNhanThongBao);

            // 12. Doi tra / Bao hanh: 1 doi tra cho duyet, 0 bao hanh.
            Assert.Single(vm.DoiTraChoDuyet);
            Assert.Equal("Chờ duyệt", vm.DoiTraChoDuyet[0].TrangThai);
            Assert.Empty(vm.BaoHanhChoDuyet);
        }
        finally { DeleteDb(db); }
    }

    // ============ (a2) View .cshtml: du 12 section, khong du lieu gia ============

    [Fact]
    public void TongQuan_View_Du12Section_KhongDuLieuGia_KhongGoiYDemo()
    {
        var projectDir = Path.GetDirectoryName(FindTechZoneProject())!;
        var view = File.ReadAllText(Path.Combine(projectDir, "Views", "Admin", "TongQuan.cshtml"));

        Assert.Contains("Trang tổng hợp", view);
        foreach (var tieuDe in new[]
        {
            "Dashboard", "Sản phẩm", "Đơn hàng", "Khách hàng", "Voucher", "Combo",
            "Flash Sale", "Banner", "Đánh giá", "Kho", "Thông báo", "Đổi trả / Bảo hành",
        })
            Assert.Contains(tieuDe, view);

        // Dung 12 panel (data-tongquan-panel) + 12 tab.
        Assert.Equal(12, Regex.Matches(view, "data-tongquan-panel=").Count);
        Assert.Equal(12, Regex.Matches(view, "data-tongquan-tab=").Count);

        // Moi form thao tac nhanh deu kem hidden tuTrang=TongQuan.
        Assert.True(Regex.Matches(view, "name=\"tuTrang\" value=\"TongQuan\"").Count >= 10,
            "Thieu hidden tuTrang=TongQuan o form thao tac nhanh.");

        // Du lieu that lay tu DB qua model — view khong hard-code ten/gia SP, khong goi y demo.
        Assert.DoesNotContain("Xiaomi 15", view);
        Assert.DoesNotContain("12.990.000", view);
        Assert.DoesNotContain("demo", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin@techzone.vn", view);
    }

    // ============ (b)/(c) Auth: chi role Admin (reflection) ============

    [Fact]
    public void Auth_TongQuan_ChiRoleAdmin_KhongAllowAnonymous()
    {
        var auth = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(auth);
        Assert.Equal("Admin", auth.Roles);
        Assert.Null(typeof(AdminController).GetCustomAttribute<AllowAnonymousAttribute>());

        var tongQuan = typeof(AdminController).GetMethod(nameof(AdminController.TongQuan));
        Assert.NotNull(tongQuan);
        Assert.Null(tongQuan.GetCustomAttribute<AllowAnonymousAttribute>());

        // ReturnsController.Duyet (doi tra / bao hanh tu section 12) cung chi Admin.
        var duyet = typeof(ReturnsController).GetMethod(nameof(ReturnsController.Duyet));
        Assert.NotNull(duyet);
        var duyetAuth = duyet.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(duyetAuth);
        Assert.Equal("Admin", duyetAuth.Roles);
    }

    // ============ (d) DuyetDanhGia tu TongQuan -> ve TongQuan ============

    [Fact]
    public async Task DuyetDanhGia_TuTongQuan_RedirectVeTongQuan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Review", 100_000, tonKho: 10);
            await SeedUserAsync(services, "user-1");
            var reviewId = await SeedReviewAsync(services, productId, "user-1", isApproved: false);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanFormTuTongQuan(admin);
            var ketQua = await admin.DuyetDanhGia(reviewId);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal(nameof(AdminController.TongQuan), redirect.ActionName);
            Assert.Equal("Đã duyệt đánh giá.", admin.TempData["Success"] as string);
            Assert.True(await ctx.Reviews.AsNoTracking()
                .Where(r => r.Id == reviewId).Select(r => r.IsApproved).FirstAsync());
        }
        finally { DeleteDb(db); }
    }

    // ============ (e) DoiTrangThai tu TongQuan -> ve TongQuan ============

    [Fact]
    public async Task DoiTrangThai_TuTongQuan_RedirectVeTongQuan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP TT", 100_000, tonKho: 5);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "TQ-TT-1");

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanFormTuTongQuan(admin);
            var ketQua = await admin.DoiTrangThai(orderId, OrderStatus.DaXacNhan);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal(nameof(AdminController.TongQuan), redirect.ActionName);
            Assert.Equal(OrderStatus.DaXacNhan, await ctx.Orders.AsNoTracking()
                .Where(o => o.Id == orderId).Select(o => o.TrangThai).FirstAsync());
        }
        finally { DeleteDb(db); }
    }

    // ============ (f1) GuiThongBao tu TongQuan -> ve TongQuan ============

    [Fact]
    public async Task GuiThongBao_TuTongQuan_RedirectVeTongQuan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedProductAsync(services, "SP TB", 100_000, tonKho: 5);
            await SeedUserAsync(services, "user-1");

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanFormTuTongQuan(admin);
            var ketQua = await admin.GuiThongBao("all", "Khuyến mãi mới", "Giảm giá cuối tuần.");
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal(nameof(AdminController.TongQuan), redirect.ActionName);
            Assert.Contains("Đã gửi thông báo tới 1 khách hàng.",
                admin.TempData["Success"] as string ?? "");
            Assert.Equal(1, await ctx.Notifications.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    // ============ (f2) NhapKho tu TongQuan -> ve TongQuan ============

    [Fact]
    public async Task NhapKho_TuTongQuan_RedirectVeTongQuan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Kho", 100_000, tonKho: 10);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanFormTuTongQuan(admin);
            var ketQua = await admin.NhapKho(productId, 5, "Nhập test TongQuan");
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal(nameof(AdminController.TongQuan), redirect.ActionName);
            Assert.Equal(15, await ctx.Products.AsNoTracking()
                .Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync());
            Assert.Equal(1, await ctx.StockIns.CountAsync(s => s.ProductId == productId));
        }
        finally { DeleteDb(db); }
    }

    // ============ (g) Hoi quy: khong co tuTrang -> ve trang cu ============

    [Fact]
    public async Task DuyetDanhGia_KhongTuTrang_RedirectVeReviewsCu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Review 2", 100_000, tonKho: 10);
            await SeedUserAsync(services, "user-1");
            var reviewId = await SeedReviewAsync(services, productId, "user-1", isApproved: false);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanTempData(admin); // khong co form / khong co tuTrang
            var ketQua = await admin.DuyetDanhGia(reviewId);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal(nameof(AdminController.Reviews), redirect.ActionName);
            Assert.True(await ctx.Reviews.AsNoTracking()
                .Where(r => r.Id == reviewId).Select(r => r.IsApproved).FirstAsync());
        }
        finally { DeleteDb(db); }
    }

    // ============ Hoi quy: Dashboard + KhachHang sau khi tach helper ============

    [Fact]
    public async Task Dashboard_Va_KhachHang_SauExtractHelper_KhongDoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedFullAsync(services);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanTempData(admin);

            var dashKetQua = await admin.Dashboard();
            var dashView = Assert.IsType<ViewResult>(dashKetQua);
            Assert.Null(dashView.ViewName); // ve view "Dashboard" theo quy uoc
            var dash = Assert.IsType<DashboardViewModel>(dashView.Model);
            Assert.Equal(3, dash.TongDon);
            Assert.Equal(13, dash.TongSanPham);

            var tongQuanKetQua = await admin.TongQuan();
            var tongQuan = Assert.IsType<TongQuanViewModel>(
                Assert.IsType<ViewResult>(tongQuanKetQua).Model);
            Assert.Equal(tongQuan.Dashboard.TongDon, dash.TongDon);
            Assert.Equal(tongQuan.Dashboard.TongSanPham, dash.TongSanPham);
            Assert.Equal(tongQuan.Dashboard.DonMoi, dash.DonMoi);
            Assert.Equal(tongQuan.Dashboard.DoanhThuThang, dash.DoanhThuThang);

            var khKetQua = await admin.KhachHang();
            var khView = Assert.IsType<ViewResult>(khKetQua);
            Assert.Equal("Customers", khView.ViewName);
            var khach = Assert.IsType<List<(ApplicationUser User, decimal TongChiTieu, string Hang, int Diem)>>(khView.Model);
            Assert.Single(khach);
            Assert.Equal(tongQuan.KhachHang.Count, khach.Count);
            Assert.Equal(tongQuan.KhachHang[0].User.Email, khach[0].User.Email);
        }
        finally { DeleteDb(db); }
    }

    // ============ HTTP that: admin 200 / anonymous + khach bi day ve Login ============

    [Fact]
    public async Task Http_TongQuan_Admin200_AnDanhVaKhachBiDayVeLogin()
    {
        var db = TempDbPath();
        var jarAdmin = Path.Combine(Path.GetTempPath(), "tzjar-admin-" + Guid.NewGuid().ToString("N"));
        var jarKhach = Path.Combine(Path.GetTempPath(), "tzjar-khach-" + Guid.NewGuid().ToString("N"));
        var port = FreePort();
        var project = FindTechZoneProject();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dotnet = Path.Combine(home, ".dotnet", "dotnet");
        if (!File.Exists(dotnet)) dotnet = "dotnet";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            ArgumentList = { "run", "--project", project, "--urls", $"http://127.0.0.1:{port}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={db}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["DOTNET_ROOT"] = Path.Combine(home, ".dotnet");
        psi.Environment["PATH"] = psi.Environment["PATH"] + Path.PathSeparator + Path.Combine(home, ".dotnet");
        // DemoAccounts BAT chi cho test HTTP nay (mat khau chi dung cho test):
        // app seed admin + khach demo de login that qua form.
        psi.Environment["DemoAccounts__Enabled"] = "true";
        psi.Environment["DemoAccounts__AdminEmail"] = "admin@techzone.vn";
        psi.Environment["DemoAccounts__AdminPassword"] = "AdminTest123";
        psi.Environment["DemoAccounts__CustomerEmail"] = "khach@techzone.vn";
        psi.Environment["DemoAccounts__CustomerPassword"] = "KhachTest123";

        using var proc = Process.Start(psi)!;
        _ = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(150);
            string? homeCode = null;
            while (DateTime.UtcNow < deadline)
            {
                homeCode = await CurlStatusAsync(port, "/");
                if (homeCode == "200") break;
                if (proc.HasExited) break;
                await Task.Delay(2000);
            }
            Assert.Equal("200", homeCode);
            _out.WriteLine($"boot: / -> {homeCode} (port {port})");

            // (b) Anonymous GET /Admin/TongQuan -> 302 ve /Account/Login.
            var (codeAnon, redirectAnon) = await CurlStatusRedirectAsync(port, "/Admin/TongQuan");
            Assert.Equal("302", codeAnon);
            Assert.Contains("/Account/Login", redirectAnon);
            _out.WriteLine($"anonymous /Admin/TongQuan -> {codeAnon} {redirectAnon}");

            // (a) Login admin that -> GET /Admin/TongQuan 200, chua tieu de + SP that + gia that.
            await LoginAsync(port, jarAdmin, "admin@techzone.vn", "AdminTest123", "/Admin/Dashboard");
            var body = await CurlBodyAsync(port, "/Admin/TongQuan", jarAdmin);
            Assert.NotNull(body);
            foreach (var chuoi in new[]
            {
                "Trang tổng hợp", "Xiaomi 15", "Dashboard", "Sản phẩm",
                "Đơn hàng", "Khách hàng", "Voucher", "Combo", "Flash Sale", "Banner",
                "Đánh giá", "Kho", "Thông báo", "Đổi trả",
            })
                Assert.Contains(chuoi, body);
            // Gia that 12.990.000d: view dung ToString("N0") nen dau phan cach
            // tuy culture may chu (Program.cs khong cau hinh vi-VN; Dashboard
            // dung cung cach viet) — chap nhan ca "." va ",".
            Assert.True(body.Contains("12.990.000") || body.Contains("12,990,000"),
                "Khong thay gia that cua Xiaomi 15 (12.990.000 / 12,990,000) trong HTML TongQuan.");
            _out.WriteLine("admin /Admin/TongQuan -> 200, du tieu de + Xiaomi 15 + 12.990.000");

            // (c) Login khach (non-admin) -> GET /Admin/TongQuan 302 ve /Account/Login.
            await LoginAsync(port, jarKhach, "khach@techzone.vn", "KhachTest123", "/");
            var (codeKhach, redirectKhach) = await CurlStatusRedirectAsync(port, "/Admin/TongQuan", jarKhach);
            Assert.Equal("302", codeKhach);
            Assert.Contains("/Account/Login", redirectKhach);
            _out.WriteLine($"khach /Admin/TongQuan -> {codeKhach} {redirectKhach}");
        }
        finally
        {
            KillProc(proc);
            try
            {
                var err = await WithTimeout(stderrTask, TimeSpan.FromSeconds(5)) ?? "";
                if (proc.HasExited && proc.ExitCode != 0)
                    _out.WriteLine("app stderr (tail): " + (err.Length > 1000 ? err[^1000..] : err));
            }
            catch { /* best effort */ }
            foreach (var f in new[] { jarAdmin, jarKhach })
                try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
            DeleteDb(db);
        }
    }

    // ================= Helpers: controller test =================

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

    private static async Task SeedFullAsync(ServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
        }
        using (var scope = services.CreateScope())
            await SeedData.InitializeAsync(scope.ServiceProvider);
    }

    private static async Task<int> SeedProductAsync(ServiceProvider services, string ten, decimal gia, int tonKho)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        using var scope2 = services.CreateScope();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx2.Categories.FirstOrDefaultAsync(c => c.Slug == "test-cat");
        if (category is null)
        {
            category = new Category { Ten = "Test Cat", Slug = "test-cat" };
            ctx2.Categories.Add(category);
            await ctx2.SaveChangesAsync();
        }
        var brand = await ctx2.Brands.FirstOrDefaultAsync(b => b.Ten == "Test Brand");
        if (brand is null)
        {
            brand = new Brand { Ten = "Test Brand" };
            ctx2.Brands.Add(brand);
            await ctx2.SaveChangesAsync();
        }
        var product = new Product
        {
            Ten = ten,
            Gia = gia,
            CategoryId = category.Id,
            BrandId = brand.Id,
            TonKho = tonKho,
            LuotBan = 0,
        };
        ctx2.Products.Add(product);
        await ctx2.SaveChangesAsync();
        return product.Id;
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "test-user",
            NormalizedUserName = "TEST-USER",
            Email = "test@x.vn",
            NormalizedEmail = "TEST@X.VN",
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> SeedReviewAsync(ServiceProvider services,
        int productId, string userId, bool isApproved)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Sao = 5,
            NoiDung = "Review test TongQuan.",
            NgayDanhGia = DateTime.Now,
            IsApproved = isApproved,
        };
        ctx.Reviews.Add(review);
        await ctx.SaveChangesAsync();
        return review.Id;
    }

    private static async Task<int> DatDonThanhCongAsync(ServiceProvider services,
        int productId, int soLuong, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var ketQua = await checkout.DatHangAsync(new CheckoutInput
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, soLuong, 100_000) },
            IdempotencyKey = key,
        });
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
    }

    /// <summary>TempData trong bo nho cho test controller (khong can server that).</summary>
    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public Dictionary<string, object?> Store { get; } = new();
        public IDictionary<string, object?> LoadTempData(HttpContext context) => Store;
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static void GanTempData(Controller controller)
    {
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new FakeTempDataProvider());
    }

    /// <summary>Gia lap form POST tu trang TongQuan: hidden tuTrang=TongQuan.
    /// LUU Y: phai gan qua Request.Form setter — new FormFeature(IFormCollection)
    /// de Request null nen doc Request.Form nem NullReferenceException
    /// (FormFeature.ResolveHasInvalidAntiforgeryValidationFeature).</summary>
    private static void GanFormTuTongQuan(Controller controller)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(
            new Dictionary<string, StringValues> { ["tuTrang"] = "TongQuan" });
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new FakeTempDataProvider());
    }

    // ================= Helpers: HTTP test (theo pattern MigrationTests) =================

    private static string FindTechZoneProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "TechZone", "TechZone.csproj");
            if (File.Exists(candidate)) return candidate;
            var direct = Path.Combine(dir.FullName, "TechZone.csproj");
            if (File.Exists(direct)) return direct;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Khong tim thay TechZone.csproj tu " + AppContext.BaseDirectory);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void KillProc(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        try { proc.WaitForExit(5000); } catch { /* best effort */ }
    }

    private static async Task<string?> WithTimeout(Task<string> task, TimeSpan timeout)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout));
        return done == task ? await task : null;
    }

    private static async Task<string?> CurlRawAsync(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> CurlStatusAsync(int port, string path)
    {
        var raw = await CurlRawAsync("-s", "-o", "/dev/null", "-w", "%{http_code}",
            "--max-time", "10", $"http://127.0.0.1:{port}{path}");
        var code = raw?.Trim();
        return code is not null && code.Length == 3 ? code : null;
    }

    private static async Task<(string? Code, string Redirect)> CurlStatusRedirectAsync(
        int port, string path, string? cookieJar = null)
    {
        var args = new List<string> { "-s", "-o", "/dev/null", "-w", "%{http_code}|%{redirect_url}",
            "--max-time", "10" };
        if (cookieJar is not null) args.AddRange(new[] { "-b", cookieJar });
        args.Add($"http://127.0.0.1:{port}{path}");
        var raw = await CurlRawAsync(args.ToArray());
        if (raw is null) return (null, "");
        var parts = raw.Split('|', 2);
        return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
    }

    private static async Task<string?> CurlBodyAsync(int port, string path, string? cookieJar = null)
    {
        var args = new List<string> { "-s", "--max-time", "15" };
        if (cookieJar is not null) args.AddRange(new[] { "-b", cookieJar });
        args.Add($"http://127.0.0.1:{port}{path}");
        return await CurlRawAsync(args.ToArray());
    }

    /// <summary>Login that qua form /Account/Login (kem antiforgery token).</summary>
    private async Task LoginAsync(int port, string jar, string email, string password,
        string redirectChua)
    {
        var loginPage = await CurlRawAsync("-s", "-c", jar, "--max-time", "15",
            $"http://127.0.0.1:{port}/Account/Login");
        Assert.NotNull(loginPage);
        var token = Regex.Match(loginPage,
            "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "Khong lay duoc antiforgery token o trang Login.");

        // POST login (dung lai cookie jar: ca cookie antiforgery + session).
        var args = new List<string> { "-s", "-o", "/dev/null", "-w", "%{http_code}|%{redirect_url}",
            "--max-time", "15", "-b", jar, "-c", jar,
            "--data-urlencode", $"Email={email}",
            "--data-urlencode", $"MatKhau={password}",
            "--data-urlencode", $"__RequestVerificationToken={token}",
            $"http://127.0.0.1:{port}/Account/Login" };
        var raw = await CurlRawAsync(args.ToArray());
        Assert.NotNull(raw);
        var parts = raw.Split('|', 2);
        Assert.Equal("302", parts[0].Trim());
        Assert.Contains(redirectChua, parts.Length > 1 ? parts[1] : "");
    }

    // ================= Helpers: DB tam =================

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }
}
