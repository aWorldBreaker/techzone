using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Phase 3 — Authentication UX (Sprint 6 Continuation, Module 5).
/// Hop dong: Register trung email/username chi tra DUY NHAT thong bao chung
/// "Không thể tạo tài khoản với thông tin này. Vui lòng kiểm tra lại thông tin đã nhập."
/// — khong lo email, khong chua "đã tồn tại"/"đã đăng ký"/"trùng".
/// Login sai van "Email hoặc mật khẩu không đúng."; lockout hien theo
/// LockoutEnd that (xem LockoutTests); ReturnUrl chi redirect URL local.
/// Goi AccountController that tren DbContext SQLite tam, theo dung pattern
/// LockoutTests / DemoAccountsTests (khong dung Moq).
/// </summary>
[Collection("DbTests")]
public class AuthTests
{
    public const string ThongBaoTrungThongTin =
        "Không thể tạo tài khoản với thông tin này. Vui lòng kiểm tra lại thông tin đã nhập.";

    private const string Email = "auth@test.vn";
    private const string MatKhauDung = "MatKhau123";
    private const string MatKhauSai = "SaiMatKhau1";

    // ============ (a) Register trung email ============

    [Fact]
    public async Task Register_TrungEmail_ThongBaoChung_KhongLoEmail()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            // Dang ky lan 1 thanh cong.
            var (ctrl1, _) = TaoController(services);
            var lan1 = await ctrl1.Register(RegisterModel(Email));
            Assert.IsType<RedirectToActionResult>(lan1);

            // Dang ky lan 2 cung email -> that bai, ViewResult + thong bao chung DUY NHAT.
            var (ctrl2, _) = TaoController(services);
            var lan2 = await ctrl2.Register(RegisterModel(Email));
            Assert.IsType<ViewResult>(lan2);
            var loi = LayLoiModelState(ctrl2);
            Assert.Single(loi);
            Assert.Equal(ThongBaoTrungThongTin, loi[0]);
            // Khong tiet lo email trong loi; khong dung tu "da ton tai"/"da dang ky"/"trung".
            Assert.DoesNotContain(Email, loi[0]);
            Assert.DoesNotContain("đã tồn tại", loi[0]);
            Assert.DoesNotContain("đã đăng ký", loi[0]);
            Assert.DoesNotContain("trùng", loi[0]);
            Assert.DoesNotContain("already", loi[0]);
            // Khong tao user thu hai.
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Equal(1, await userManager.Users.CountAsync());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (b) Register trung username (UserName != Email) ============

    [Fact]
    public async Task Register_TrungUsername_ThongBaoChung_KhongLoEmail()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            // Seed user co UserName = email muc tieu nhung Email khac
            // (email van duy nhat) -> chi va cham o UserName khi Register.
            using (var scope = services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var seed = new ApplicationUser
                {
                    UserName = "trunguser@test.vn",
                    Email = "khac@test.vn",
                    EmailConfirmed = true,
                };
                var tao = await userManager.CreateAsync(seed, MatKhauDung);
                Assert.True(tao.Succeeded, string.Join("; ", tao.Errors.Select(e => e.Description)));
            }

            var (ctrl, _) = TaoController(services);
            var ketQua = await ctrl.Register(RegisterModel("trunguser@test.vn"));
            Assert.IsType<ViewResult>(ketQua);
            var loi = LayLoiModelState(ctrl);
            Assert.Single(loi);
            Assert.Equal(ThongBaoTrungThongTin, loi[0]);
            Assert.DoesNotContain("trunguser@test.vn", loi[0]);
            Assert.DoesNotContain("đã tồn tại", loi[0]);
            Assert.DoesNotContain("đã đăng ký", loi[0]);
            Assert.DoesNotContain("trùng", loi[0]);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (c) Register thanh cong ============

    [Fact]
    public async Task Register_ThanhCong_TaoUser_RoleKhach_TuDangNhap()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            var (ctrl, http) = TaoController(services);
            var ketQua = await ctrl.Register(RegisterModel(Email));
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Home", redirect.ControllerName);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(Email);
            Assert.NotNull(user);
            Assert.True(await userManager.IsInRoleAsync(user!, "Khach"));
            // Tu dang nhap: SignInAsync da ghi cookie xac thuc vao response.
            var setCookie = http.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains("Identity.Application", setCookie);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (d) Login sai 5 lan -> lockout + message theo LockoutEnd that ============

    [Fact]
    public async Task Login_NamLanSai_KhoaVaThongBaoTheoLockoutEndThat()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            await TaoUserAsync(services, Email);
            var signIn = LaySignInManager(services);
            for (var i = 0; i < 5; i++)
                await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);

            // Dang trong thoi gian khoa: mat khau DUNG cung bi tu choi,
            // message tinh tu LockoutEnd that (khong hard-code "5 phut").
            var (ctrl, _) = TaoController(services);
            var ketQua = await ctrl.Login(new LoginViewModel { Email = Email, MatKhau = MatKhauDung });
            Assert.IsType<ViewResult>(ketQua);
            var loi = LayLoiModelState(ctrl);
            Assert.Single(loi);
            string lockoutEndStr;
            DateTimeOffset? lockoutEnd;
            using (var scope = services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await userManager.FindByEmailAsync(Email))!;
                lockoutEnd = user.LockoutEnd;
            }
            Assert.NotNull(lockoutEnd);
            var kyVong = AccountController.ThongBaoKhoa(lockoutEnd, DateTimeOffset.UtcNow);
            Assert.Equal(kyVong, loi[0]);
            Assert.Contains("thử lại sau", loi[0]);
            lockoutEndStr = loi[0];
            Assert.DoesNotContain("liên hệ quản trị viên", lockoutEndStr);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (e) ReturnUrl ngoai domain -> khong redirect ra ngoai ============

    [Fact]
    public async Task Login_ReturnUrlNgoaiDomain_KhongRedirectRaNgoai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            await TaoUserAsync(services, Email);
            foreach (var returnUrl in new[]
            {
                "https://evil.example.com/phish",
                "http://evil.example.com/",
                "//evil.example.com/x",
            })
            {
                var (ctrl, _) = TaoController(services);
                var ketQua = await ctrl.Login(new LoginViewModel
                {
                    Email = Email, MatKhau = MatKhauDung, ReturnUrl = returnUrl,
                });
                // Khong redirect ra ngoai: ve Home/Index mac dinh, tuyet doi
                // khong phai RedirectResult toi URL ngoai.
                var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
                Assert.Equal("Index", redirect.ActionName);
                Assert.Equal("Home", redirect.ControllerName);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (f) Login dung + ReturnUrl local -> redirect dung ============

    [Fact]
    public async Task Login_Dung_ReturnUrlLocal_RedirectDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await MigrateAsync(services);
            await TaoRoleKhachAsync(services);
            await TaoUserAsync(services, Email);

            var (ctrl, _) = TaoController(services);
            var ketQua = await ctrl.Login(new LoginViewModel
            {
                Email = Email, MatKhau = MatKhauDung, ReturnUrl = "/Products",
            });
            var redirect = Assert.IsType<RedirectResult>(ketQua);
            Assert.Equal("/Products", redirect.Url);

            // ReturnUrl local dang query-string cung duoc chap nhan.
            var (ctrl2, _) = TaoController(services);
            var ketQua2 = await ctrl2.Login(new LoginViewModel
            {
                Email = Email, MatKhau = MatKhauDung, ReturnUrl = "/Cart/Index",
            });
            Assert.Equal("/Cart/Index", Assert.IsType<RedirectResult>(ketQua2).Url);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

    private static RegisterViewModel RegisterModel(string email) => new()
    {
        HoTen = "Test User",
        Email = email,
        SoDienThoai = "0900000000",
        MatKhau = MatKhauDung,
        NhapLaiMatKhau = MatKhauDung,
    };

    private static List<string> LayLoiModelState(Controller ctrl) =>
        ctrl.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddScoped<LoyaltyService>();
        // Login/Register dung Url.IsLocalUrl / RedirectToAction -> can UrlHelper.
        services.AddControllersWithViews();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private static async Task TaoRoleKhachAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Khach" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
    }

    private static async Task TaoUserAsync(ServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var ketQua = await userManager.CreateAsync(user, MatKhauDung);
        Assert.True(ketQua.Succeeded, string.Join("; ", ketQua.Errors.Select(e => e.Description)));
    }

    /// <summary>
    /// Controller that + HttpContext that (SignInManager doc qua
    /// IHttpContextAccessor) + Url helper that cho Url.IsLocalUrl.
    /// Tra ve ca HttpContext de test doc Set-Cookie sau Register.
    /// </summary>
    private static (AccountController, DefaultHttpContext) TaoController(ServiceProvider services)
    {
        var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            http,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        var urlHelper = sp.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(actionContext);
        var ctrl = new AccountController(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<SignInManager<ApplicationUser>>(),
            sp.GetRequiredService<LoyaltyService>(),
            new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext(actionContext),
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, new NoOpTempDataProvider()),
            Url = urlHelper,
        };
        // SignInManager doc HttpContext qua IHttpContextAccessor.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return (ctrl, http);
    }

    private static SignInManager<ApplicationUser> LaySignInManager(ServiceProvider services)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = services };
        var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
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
