using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Tests;

/// <summary>
/// SEC-03 Sign-off Phase B — automated tests cho lockout dang nhap.
/// Vong truoc CHUA co test tu dong (chi smoke that 5 lan sai -> LockoutEnd).
/// Harness: SignInManager/UserManager THAT tren DbContext SQLite tam, voi
/// dung IdentityOptions nhu Program.cs (5 lan sai / khoa 5 phut /
/// AllowedForNewUsers = true) + HttpContext that (DefaultHttpContext,
/// RequestServices = service provider) de PasswordSignInAsync chay duoc
/// ca nhanh SignInAsync (dang nhap thanh cong). Khong dung Moq — test
/// project khong co thu vien mock; SignInManager duoc resolve tu DI that.
/// </summary>
[Collection("DbTests")]
public class LockoutTests
{
    private const string Email = "lockout@test.vn";
    private const string MatKhauDung = "MatKhau123";
    private const string MatKhauSai = "SaiMatKhau1";

    [Fact]
    public async Task Lockout_NamLanSai_KhoaNamPhut()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var user = await TaoUserAsync(services);
            var signIn = LaySignInManager(services);

            Microsoft.AspNetCore.Identity.SignInResult ketQua = null!;
            for (var i = 0; i < 5; i++)
                ketQua = await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);

            // Lan sai thu 5 da kich lockout.
            Assert.True(ketQua.IsLockedOut);
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var userMoi = await userManager.FindByEmailAsync(Email);
            Assert.NotNull(userMoi);
            Assert.True(await userManager.IsLockedOutAsync(userMoi));
            // Identity RESET AccessFailedCount ve 0 ngay khi kich lockout
            // (UserManager.AccessFailedAsync: dat LockoutEnd + reset count) —
            // da xac minh bang raw SQL: count=0, LockoutEnd=now+5phut.
            // Nen khong assert count==5; cai can khang dinh la khoa + han khoa.
            Assert.Equal(0, await userManager.GetAccessFailedCountAsync(userMoi));
            // LockoutEnd ~5 phut tu bay gio (cho sai so chay test 1 phut).
            var lockoutEnd = userMoi.LockoutEnd!.Value;
            var conLai = lockoutEnd - DateTimeOffset.UtcNow;
            Assert.InRange(conLai.TotalMinutes, 4, 5.5);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Lockout_TrongThoiGianKhoa_MatKhauDung_VanBiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            var signIn = LaySignInManager(services);
            for (var i = 0; i < 5; i++)
                await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);

            // Dang trong thoi gian khoa: mat khau DUNG cung bi tu choi.
            var ketQua = await signIn.PasswordSignInAsync(Email, MatKhauDung, isPersistent: false, lockoutOnFailure: true);
            Assert.True(ketQua.IsLockedOut);
            Assert.False(ketQua.Succeeded);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Lockout_HetThoiGianKhoa_DangNhapLaiDuoc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            var signIn = LaySignInManager(services);
            for (var i = 0; i < 5; i++)
                await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);

            // Gia lap het thoi gian khoa: dua LockoutEnd ve qua khu (khong doi
            // dong ho he thong, khong cho 5 phut that).
            using (var scope = services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await userManager.FindByEmailAsync(Email))!;
                await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(-1));
            }

            // Request moi (scope/DbContext moi — giong request HTTP moi o
            // production): SignInManager cu giu entity user cu trong tracker
            // nen phai lay SignInManager moi, neu khong se doc LockoutEnd cu.
            signIn = LaySignInManager(services);

            // Dang nhap dung mat khau lai duoc (chay qua ca SignInAsync that).
            var ketQua = await signIn.PasswordSignInAsync(Email, MatKhauDung, isPersistent: false, lockoutOnFailure: true);
            Assert.True(ketQua.Succeeded);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Lockout_SoLanSaiReset_SauDangNhapThanhCong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            var signIn = LaySignInManager(services);
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            for (var i = 0; i < 3; i++)
                await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);
            var user = (await userManager.FindByEmailAsync(Email))!;
            Assert.Equal(3, await userManager.GetAccessFailedCountAsync(user));

            // Dang nhap thanh cong -> so lan sai reset ve 0. Doc lai bang
            // scope moi: UserManager cua SignInManager giu entity rieng,
            // doc tren entity cu (da load truoc khi reset) se thay so cu.
            var ketQua = await signIn.PasswordSignInAsync(Email, MatKhauDung, isPersistent: false, lockoutOnFailure: true);
            Assert.True(ketQua.Succeeded);
            using (var scope2 = services.CreateScope())
            {
                var userManager2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var userSau = (await userManager2.FindByEmailAsync(Email))!;
                Assert.Equal(0, await userManager2.GetAccessFailedCountAsync(userSau));
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Login_KhongPhanBiet_KhongCoUser_Vs_SaiMatKhau_CungThongBao()
    {
        // Chong do tai khoan (SEC-03): "khong co user" va "sai mat khau" phai
        // cho CUNG mot thong bao chung o AccountController.Login. Test o muc
        // controller that (goi Login truc tiep, doc ModelState).
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            var signIn = LaySignInManager(services);

            var loiKhongCoUser = await LayLoiLoginAsync(services, signIn, "khongco@test.vn", MatKhauSai);
            var loiSaiMatKhau = await LayLoiLoginAsync(services, signIn, Email, MatKhauSai);
            Assert.Equal("Email hoặc mật khẩu không đúng.", loiKhongCoUser);
            Assert.Equal(loiKhongCoUser, loiSaiMatKhau);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public void ThongBaoKhoa_KhoaNganHan_HienSoPhutConLai()
    {
        // Step 4 — helper tinh message tu LockoutEnd that (khong hard-code "5 phut").
        var now = new DateTimeOffset(2026, 11, 6, 7, 0, 0, TimeSpan.Zero);
        Assert.Equal("Tài khoản tạm khóa do đăng nhập sai nhiều lần, thử lại sau 5 phút.",
            AccountController.ThongBaoKhoa(now.AddMinutes(5), now));
        // Lam tron len: con 4 phut 10 giay -> "5 phút"; con < 1 phut -> "1 phút".
        Assert.Equal("Tài khoản tạm khóa do đăng nhập sai nhiều lần, thử lại sau 5 phút.",
            AccountController.ThongBaoKhoa(now.AddMinutes(4).AddSeconds(10), now));
        Assert.Equal("Tài khoản tạm khóa do đăng nhập sai nhiều lần, thử lại sau 1 phút.",
            AccountController.ThongBaoKhoa(now.AddSeconds(30), now));
        // Bien 60 phut van la khoa ngan; qua 60 phut la khoa dai.
        Assert.Contains("thử lại sau 60 phút.",
            AccountController.ThongBaoKhoa(now.AddMinutes(60), now));
        Assert.Contains("liên hệ quản trị viên",
            AccountController.ThongBaoKhoa(now.AddMinutes(61), now));
    }

    [Fact]
    public void ThongBaoKhoa_KhoaDaiHan_HienLienHeQuanTriVien_KhongNeuPhut()
    {
        // Khoa demo toi 2126: khong duoc noi "5 phut" hay bat ky so phut nao.
        var now = new DateTimeOffset(2026, 11, 6, 7, 0, 0, TimeSpan.Zero);
        var msg = AccountController.ThongBaoKhoa(new DateTimeOffset(2126, 11, 6, 0, 0, 0, TimeSpan.Zero), now);
        Assert.Equal("Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên để được hỗ trợ mở khóa.", msg);
        Assert.DoesNotContain("phút", msg);
        Assert.DoesNotContain("5 phút", msg);
        // LockoutEnd null / da het han cung roi ve nhanh lien he quan tri vien.
        Assert.Contains("liên hệ quản trị viên", AccountController.ThongBaoKhoa(null, now));
        Assert.Contains("liên hệ quản trị viên", AccountController.ThongBaoKhoa(now.AddMinutes(-1), now));
    }

    [Fact]
    public async Task Login_KhoaNganHan_HienSoPhutConLai()
    {
        // Khoa 5 phut that (5 lan sai) -> Login hien so phut con lai, khong
        // phai message lien he quan tri vien.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            var signIn = LaySignInManager(services);
            for (var i = 0; i < 5; i++)
                await signIn.PasswordSignInAsync(Email, MatKhauSai, isPersistent: false, lockoutOnFailure: true);

            var loi = await LayLoiLoginAsync(services, signIn, Email, MatKhauDung);
            Assert.NotNull(loi);
            Assert.Contains("thử lại sau", loi);
            Assert.Contains("phút", loi);
            Assert.DoesNotContain("liên hệ quản trị viên", loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Login_KhoaDaiHan_HienLienHeQuanTriVien()
    {
        // Gia lap khoa demo toi 2126-11-06 -> Login hien huong dan lien he
        // quan tri vien, tuyet doi khong co "5 phút".
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await TaoUserAsync(services);
            using (var scope = services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await userManager.FindByEmailAsync(Email))!;
                await userManager.SetLockoutEndDateAsync(user,
                    new DateTimeOffset(2126, 11, 6, 0, 0, 0, TimeSpan.Zero));
            }
            var signIn = LaySignInManager(services);

            var loi = await LayLoiLoginAsync(services, signIn, Email, MatKhauDung);
            Assert.Equal("Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên để được hỗ trợ mở khóa.", loi);
            Assert.DoesNotContain("phút", loi!);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

    /// <summary>IdentityOptions giong het Program.cs (SEC-03).</summary>
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
        return services.BuildServiceProvider();
    }

    private static async Task<ApplicationUser> TaoUserAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Email, Email = Email, EmailConfirmed = true };
        var ketQua = await userManager.CreateAsync(user, MatKhauDung);
        Assert.True(ketQua.Succeeded, string.Join("; ", ketQua.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>
    /// SignInManager that + HttpContext that de PasswordSignInAsync chay duoc.
    /// HttpContextAccessor la singleton trong DI nen gan HttpContext o day
    /// se duoc SignInManager doc.
    /// </summary>
    private static SignInManager<ApplicationUser> LaySignInManager(ServiceProvider services)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = services };
        // Scope KHONG dispose: SignInManager/UserManager phai song trong
        // suot luc test chay (dispose scope se dispose ca UserManager).
        var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
    }

    /// <summary>Goi AccountController.Login that, tra ve thong bao loi ModelState (null neu khong loi).</summary>
    private static async Task<string?> LayLoiLoginAsync(ServiceProvider services,
        SignInManager<ApplicationUser> signIn, string email, string matKhau)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // Login khong dung LoyaltyService / IWebHostEnvironment — truyen null!
        // co chu thich; chi dung cach nay cho action Login.
        var http = services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        var ctrl = new AccountController(userManager, signIn, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            // View(model) can TempData qua ITempDataDictionaryFactory (khong co
            // trong DI toi thieu cua test) — gan TempData truc tiep de tranh.
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, new NoOpTempDataProvider()),
        };
        var ketQua = await ctrl.Login(new LoginViewModel { Email = email, MatKhau = matKhau });
        Assert.IsType<ViewResult>(ketQua);
        return ctrl.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault();
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
