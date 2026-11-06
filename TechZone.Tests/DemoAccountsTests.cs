using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Test SEC-01 vong Sign-off (khong sua SecurityTests.cs — file cua worker khac):
/// - DemoAccounts mac dinh TAT (khong co cau hinh / Production mac dinh /
///   Enabled=false / thieu mat khau) thi SeedData KHONG tao tai khoan demo.
/// - Chi khi Enabled=true MOT CACH CHU DONG + du email/mat khau (qua cau hinh
///   — o app that la bien moi truong DemoAccounts__*, o test la in-memory)
///   thi seed tao du 2 tai khoan demo. Production bat chu dong van seed
///   (co chu y) — ke ca canh bao LOUD trong SeedData.
/// - Register: Production khong tu xac nhan email; Development thi co.
/// Mat khau trong file nay CHI DUNG CHO TEST (AdminTest123/KhachTest123,
/// MatKhauCu*/MatKhauMoi*), khong phai mat khau demo that.
/// SEC-06 (vong Sign-off): bo sung — Production + Enabled=false tuong minh
/// van khong seed; thieu mat khau Admin thi chi tao tai khoan Khach; tai
/// khoan demo da ton tai thi seed KHONG reset mat khau cua no; canh bao LOUD
/// khi DB con tai khoan demo cu trong khi seed tat (va khong canh bao khi
/// khong co tai khoan demo cu).
/// Chay tuan tu theo [Collection("DbTests")] giong cac test khac.
/// </summary>
[Collection("DbTests")]
public class DemoAccountsTests
{
    private const string AdminEmail = "admin@techzone.vn";
    private const string KhachEmail = "khach@techzone.vn";
    private const string AdminTestPassword = "AdminTest123";
    private const string KhachTestPassword = "KhachTest123";

    // ============ SeedData: mac dinh tat ============

    [Fact]
    public async Task Seed_KhongCoCauHinh_MacDinhTat_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db, config: null, envName: Environments.Development);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Seed_Production_MacDinh_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            // Production + khong bat chu dong (khong co section) -> khong seed.
            var services = BuildServices(db, config: null, envName: Environments.Production);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Seed_DemoAccounts_Tat_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "false",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                ["DemoAccounts:AdminPassword"] = AdminTestPassword,
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
                ["DemoAccounts:CustomerPassword"] = KhachTestPassword,
            });
            var services = BuildServices(db, config, Environments.Development);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SeedData: thieu mat khau ============

    [Fact]
    public async Task Seed_ThieuMatKhauKhach_ChiTaoTaiKhoanAdmin()
    {
        var db = TempDbPath();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                ["DemoAccounts:AdminPassword"] = AdminTestPassword,
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
                // Khong co CustomerPassword -> bo qua tai khoan khach.
            });
            var services = BuildServices(db, config, Environments.Development);
            await SeedAsync(services);
            Assert.NotNull(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Seed_ThieuCaHaiMatKhau_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
            });
            var services = BuildServices(db, config, Environments.Development);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SeedData: du cau hinh ============

    [Fact]
    public async Task Seed_DuCauHinh_TaoDuTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db, FullConfig(), Environments.Development);
            await SeedAsync(services);
            var admin = await FindUserAsync(services, AdminEmail);
            var khach = await FindUserAsync(services, KhachEmail);
            Assert.NotNull(admin);
            Assert.NotNull(khach);
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await userManager.IsInRoleAsync(admin!, "Admin"));
            Assert.True(await userManager.IsInRoleAsync(khach!, "Khach"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Seed_Production_BatChuDong_TaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            // Production nhung bat chu dong co chu y (Enabled=true + du mat
            // khau qua cau hinh/env) thi van seed — SeedData log canh bao LOUD.
            var services = BuildServices(db, FullConfig(), Environments.Production);
            await SeedAsync(services);
            Assert.NotNull(await FindUserAsync(services, AdminEmail));
            Assert.NotNull(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SEC-06: Production + Enabled=false tuong minh ============

    [Fact]
    public async Task Seed_Production_EnabledFalse_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            // Production + Enabled=false TUONG MINH (khong chi "khong set")
            // + du mat khau -> van khong seed.
            var config = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "false",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                ["DemoAccounts:AdminPassword"] = AdminTestPassword,
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
                ["DemoAccounts:CustomerPassword"] = KhachTestPassword,
            });
            var services = BuildServices(db, config, Environments.Production);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.Null(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SEC-06: thieu mat khau Admin (doi xung voi thieu Khach) ============

    [Fact]
    public async Task Seed_ThieuMatKhauAdmin_ChiTaoTaiKhoanKhach()
    {
        var db = TempDbPath();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                // Khong co AdminPassword -> bo qua tai khoan admin.
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
                ["DemoAccounts:CustomerPassword"] = KhachTestPassword,
            });
            var services = BuildServices(db, config, Environments.Development);
            await SeedAsync(services);
            Assert.Null(await FindUserAsync(services, AdminEmail));
            Assert.NotNull(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SEC-06: tai khoan demo da ton tai -> khong reset mat khau ============

    [Fact]
    public async Task Seed_TaiKhoanDemoDaTonTai_KhongResetMatKhau()
    {
        var db = TempDbPath();
        try
        {
            // Tao truoc 2 tai khoan demo voi mat khau CU (mo phong DB dev
            // da seed tu truoc, giong techzone.db hien tai).
            var servicesCu = BuildServices(db, config: null, envName: Environments.Development);
            await MigrateAsync(servicesCu);
            using (var scope = servicesCu.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
                foreach (var role in new[] { "Admin", "Khach" })
                    if (!await roleManager.RoleExistsAsync(role))
                        await roleManager.CreateAsync(new IdentityRole(role));
                var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var adminCu = new ApplicationUser { UserName = AdminEmail, Email = AdminEmail, HoTen = "Quản trị TechZone", SoDienThoai = "0901234567", EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(adminCu, "MatKhauCuAdmin1")).Succeeded);
                await userManager.AddToRoleAsync(adminCu, "Admin");
                var khachCu = new ApplicationUser { UserName = KhachEmail, Email = KhachEmail, HoTen = "Nguyễn Văn An", SoDienThoai = "0909123456", EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(khachCu, "MatKhauCuKhach1")).Succeeded);
                await userManager.AddToRoleAsync(khachCu, "Khach");
            }
            // Seed lai voi Enabled=true + mat khau MOI: tai khoan cu phai
            // giu nguyen mat khau (SeedData chi tao moi, khong doi mat khau
            // cua user da ton tai) va giu nguyen role.
            var configMoi = Config(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = AdminEmail,
                ["DemoAccounts:AdminPassword"] = "MatKhauMoiAdmin2",
                ["DemoAccounts:CustomerEmail"] = KhachEmail,
                ["DemoAccounts:CustomerPassword"] = "MatKhauMoiKhach2",
            });
            var services = BuildServices(db, configMoi, Environments.Development);
            await SeedAsync(services);
            using var scope2 = services.CreateScope();
            var um = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await um.FindByEmailAsync(AdminEmail);
            var khach = await um.FindByEmailAsync(KhachEmail);
            Assert.NotNull(admin);
            Assert.NotNull(khach);
            Assert.True(await um.CheckPasswordAsync(admin!, "MatKhauCuAdmin1"));
            Assert.False(await um.CheckPasswordAsync(admin!, "MatKhauMoiAdmin2"));
            Assert.True(await um.CheckPasswordAsync(khach!, "MatKhauCuKhach1"));
            Assert.False(await um.CheckPasswordAsync(khach!, "MatKhauMoiKhach2"));
            Assert.True(await um.IsInRoleAsync(admin!, "Admin"));
            Assert.True(await um.IsInRoleAsync(khach!, "Khach"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SEC-06: canh bao LOUD ve tai khoan demo cu ============

    [Fact]
    public async Task CanhBao_TaiKhoanDemoCuTonTai_KhiSeedTat_LogCanhBaoLoud()
    {
        var db = TempDbPath();
        var logs = new List<string>();
        try
        {
            // DB chi co tai khoan demo cu, seed TAT -> SeedData phai log
            // CANH BAO BAO MAT ke ten ca 2 email (khong tu xoa tai khoan).
            var servicesCu = BuildServices(db, config: null, envName: Environments.Development);
            await MigrateAsync(servicesCu);
            using (var scope = servicesCu.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
                foreach (var role in new[] { "Admin", "Khach" })
                    if (!await roleManager.RoleExistsAsync(role))
                        await roleManager.CreateAsync(new IdentityRole(role));
                var um = sp.GetRequiredService<UserManager<ApplicationUser>>();
                Assert.True((await um.CreateAsync(new ApplicationUser { UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true }, "MatKhauCuAdmin1")).Succeeded);
                Assert.True((await um.CreateAsync(new ApplicationUser { UserName = KhachEmail, Email = KhachEmail, EmailConfirmed = true }, "MatKhauCuKhach1")).Succeeded);
            }
            var services = BuildServices(db, config: null, envName: Environments.Development, captureLogs: logs);
            await SeedAsync(services);
            Assert.Contains(logs, l => l.Contains("CANH BAO BAO MAT") && l.Contains("tai khoan demo van ton tai"));
            Assert.Contains(logs, l => l.Contains("admin@techzone.vn") && l.Contains("khach@techzone.vn"));
            // Canh bao xong tai khoan van ton tai (khong tu xoa).
            Assert.NotNull(await FindUserAsync(services, AdminEmail));
            Assert.NotNull(await FindUserAsync(services, KhachEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task CanhBao_KhongCoTaiKhoanDemoCu_KhongLogCanhBaoLoud()
    {
        var db = TempDbPath();
        var logs = new List<string>();
        try
        {
            // DB moi, khong co tai khoan demo cu -> khong co canh bao LOUD
            // (chi co warning thuong "DemoAccounts dang TAT").
            var services = BuildServices(db, config: null, envName: Environments.Development, captureLogs: logs);
            await SeedAsync(services);
            Assert.DoesNotContain(logs, l => l.Contains("CANH BAO BAO MAT"));
            Assert.Null(await FindUserAsync(services, AdminEmail));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Register: EmailConfirmed theo moi truong ============

    [Fact]
    public void EmailConfirmedRule_Production_False_Development_True()
    {
        Assert.False(AccountController.NenTuXacNhanEmail(new FakeHostEnvironment(Environments.Production)));
        Assert.True(AccountController.NenTuXacNhanEmail(new FakeHostEnvironment(Environments.Development)));
    }

    [Fact]
    public async Task Register_Production_KhongTuXacNhanEmail()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db, config: null, envName: Environments.Production);
            await MigrateAsync(services);
            var user = await RegisterAsync(services, "prod-user@techzone.vn");
            Assert.False(user.EmailConfirmed);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Register_Development_TuXacNhanEmail()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db, config: null, envName: Environments.Development);
            await MigrateAsync(services);
            var user = await RegisterAsync(services, "dev-user@techzone.vn");
            Assert.True(user.EmailConfirmed);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

    private static IConfiguration FullConfig() => Config(new Dictionary<string, string?>
    {
        ["DemoAccounts:Enabled"] = "true",
        ["DemoAccounts:AdminEmail"] = AdminEmail,
        ["DemoAccounts:AdminPassword"] = AdminTestPassword,
        ["DemoAccounts:CustomerEmail"] = KhachEmail,
        ["DemoAccounts:CustomerPassword"] = KhachTestPassword,
    });

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider BuildServices(string dbPath, IConfiguration? config, string envName, List<string>? captureLogs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (captureLogs is not null)
            services.AddSingleton<ILoggerProvider>(new CaptureLoggerProvider(captureLogs));
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(envName));
        if (config is not null)
            services.AddSingleton(config);
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
        services.AddScoped<VoucherService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        services.Configure<VatOptions>(o => { o.GiaDaGomVat = true; o.ThueSuatMacDinh = 0.10m; });
        services.AddScoped<VatService>();
        // Register can IUrlHelperFactory de RedirectToAction trong Register chay duoc.
        services.AddControllersWithViews();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        await SeedData.InitializeAsync(scope.ServiceProvider);
    }

    private static async Task MigrateAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private static async Task<ApplicationUser?> FindUserAsync(ServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    /// <summary>Goi action Register that (kem HttpContext toi thieu de SignInAsync chay duoc).</summary>
    private static async Task<ApplicationUser> RegisterAsync(ServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        // Register goi AddToRoleAsync("Khach") — app that tao role trong
        // SeedData truoc; o day tao role truc tiep cho test.
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Khach" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        var controller = new AccountController(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<SignInManager<ApplicationUser>>(),
            sp.GetRequiredService<LoyaltyService>(),
            sp.GetRequiredService<IHostEnvironment>() as Microsoft.AspNetCore.Hosting.IWebHostEnvironment
                ?? new FakeWebHostEnvironment(sp.GetRequiredService<IHostEnvironment>().EnvironmentName));
        controller.ControllerContext = new ControllerContext(
            new ActionContext(
                new DefaultHttpContext { RequestServices = sp },
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()));
        // SignInManager doc HttpContext qua IHttpContextAccessor (khong qua
        // ControllerContext) nen phai gan ca o day.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = controller.HttpContext;
        var result = await controller.Register(new RegisterViewModel
        {
            HoTen = "Test User",
            Email = email,
            SoDienThoai = "0900000000",
            MatKhau = "Matkhau123",
            NhapLaiMatKhau = "Matkhau123",
        });
        Assert.IsType<RedirectToActionResult>(result);
        var created = await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        Assert.NotNull(created);
        return created!;
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    /// <summary>
    /// ILoggerProvider gom log vao List de test canh bao LOUD (SEC-06).
    /// Ghi dang "Level:Category:message" de assert theo noi dung.
    /// </summary>
    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _logs;
        public CaptureLoggerProvider(List<string> logs) => _logs = logs;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_logs, categoryName);
        public void Dispose() { }

        private sealed class CaptureLogger : ILogger
        {
            private readonly List<string> _logs;
            private readonly string _category;
            public CaptureLogger(List<string> logs, string category) { _logs = logs; _category = category; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_logs) _logs.Add($"{logLevel}:{_category}:{formatter(state, exception)}");
            }
        }
    }

    /// <summary>Moi truong gia cho test (IHostEnvironment).</summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "TechZone.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>IWebHostEnvironment gia — AccountController can kieu nay.</summary>
    private sealed class FakeWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "TechZone.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
