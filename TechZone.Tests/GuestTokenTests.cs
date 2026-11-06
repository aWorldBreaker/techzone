using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// SEC-02 Sign-off Phase B — ma tran GuestToken day du.
/// Phan da co trong SecurityTests (KHONG lap lai o day, chi liet ke):
///  - Don vang lai: khong token / sai token (ke ca sai hoa-thuong) bi chan;
///    dung token duoc xem; Admin duoc xem khong can token.
///  - Don tai khoan: chu don duoc xem, nguoi khac bi chan, Admin duoc xem;
///    GuestToken khong dung cho don tai khoan.
///  - TaoGuestToken: 32 ky tu, khac nhau moi lan.
///  - Checkout: don vang lai sinh token, don tai khoan khong co token.
/// Phan BO SUNG o day (ma tran con thieu):
///  - Token cua DON KHAC bi chan (ke ca token hop le cua don khac).
///  - Don cu khong co token (GuestToken null — khong backfill) thi bat ky
///    token nao cung bi chan.
///  - Muc controller (that, khong phai chi OrderAccess): Details, HoaDon,
///    TraCuu, CapLaiMaTraCuu — ke ca oracle 403/404 va message TraCuu.
/// </summary>
[Collection("DbTests")]
public class GuestTokenTests
{
    // ============ OrderAccess — bo sung: token cua don khac ============

    [Fact]
    public void GuestToken_TokenCuaDonKhac_BiChan()
    {
        var donA = new Order { UserId = null, GuestToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
        var donB = new Order { UserId = null, GuestToken = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
        // Token hop le nhung la cua don B -> khong xem duoc don A.
        Assert.False(OrderAccess.CoTheXemDonQ(donA, null, laAdmin: false, guestToken: donB.GuestToken));
        Assert.False(OrderAccess.CoTheXemDonQ(donB, null, laAdmin: false, guestToken: donA.GuestToken));
        // Sanity: token dung cua chinh don van duoc.
        Assert.True(OrderAccess.CoTheXemDonQ(donA, null, laAdmin: false, guestToken: donA.GuestToken));
    }

    [Fact]
    public void GuestToken_DonCuKhongCoToken_BatKyTokenNaoCungBiChan()
    {
        // Don cu (truoc khi co GuestToken) co GuestToken null: KHONG backfill
        // theo lenh user — bat ky token nao dua vao cung bi chan.
        var donCu = new Order { UserId = null, GuestToken = null };
        Assert.False(OrderAccess.CoTheXemDonQ(donCu, null, laAdmin: false, guestToken: "abcdef0123456789abcdef0123456789"));
        Assert.False(OrderAccess.CoTheXemDonQ(donCu, null, laAdmin: false, guestToken: null));
        // Admin van xem duoc (de xu ly don cu / cap lai ma khi khach yeu cau).
        Assert.True(OrderAccess.CoTheXemDonQ(donCu, null, laAdmin: true, guestToken: null));
    }

    // ============ Orders.Details — muc controller + oracle ============
    // SEC-04: GET khong con nhan token qua URL. GET tra CUNG mot form nhap
    // ma (NhapMaTraCuu) cho moi truong hop chua duoc phep xem — don vang
    // lai, don cua tai khoan khac, don khong ton tai — de khong lo don co
    // ton tai hay thuoc loai nao. Ma gui bang POST Details.

    [Fact]
    public async Task Details_DonVangLai_GetHienFormNhapMa_PostSaiTokenLoiChung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: "abcdef0123456789abcdef0123456789");
            var ctrl = TaoOrdersController(services, db, principal: KhachVangLai());
            // GET khong token -> form nhap ma (khong phai NotFound — form
            // dung chung cho ca don khong ton tai de chong oracle).
            var get = Assert.IsType<ViewResult>(await ctrl.Details(orderId));
            Assert.Equal("NhapMaTraCuu", get.ViewName);
            var getKhongCo = Assert.IsType<ViewResult>(await ctrl.Details(999999));
            Assert.Equal("NhapMaTraCuu", getKhongCo.ViewName);
            // POST sai token -> cung form + loi chung (khong lo don ton tai).
            var postSai = Assert.IsType<ViewResult>(await ctrl.Details(orderId, "sai-token"));
            Assert.Equal("NhapMaTraCuu", postSai.ViewName);
            Assert.Contains("mã tra cứu", (string)ctrl.ViewBag.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Details_DonVangLai_DungToken_TraView_AdminKhongCanToken()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: "abcdef0123456789abcdef0123456789");
            var ctrlKhach = TaoOrdersController(services, db, principal: KhachVangLai());
            // SEC-04: ma gui bang POST, khong qua URL.
            var ketQua = await ctrlKhach.Details(orderId, "abcdef0123456789abcdef0123456789");
            var view = Assert.IsType<ViewResult>(ketQua);
            Assert.Equal("Details", view.ViewName);
            Assert.Equal(orderId, ((Order)view.Model!).Id);

            var ctrlAdmin = TaoOrdersController(services, db, principal: Admin());
            Assert.IsType<ViewResult>(await ctrlAdmin.Details(orderId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Details_DonTaiKhoan_ChuDonDuocXem_KhachKhacVaVangLaiBiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            await SeedUserAsync(services, "u2", "khac@test.vn");
            var orderId = await SeedDonTaiKhoanAsync(services, "u1");

            var ctrlChu = TaoOrdersController(services, db, principal: NguoiDung("chu@test.vn"));
            var viewChu = Assert.IsType<ViewResult>(await ctrlChu.Details(orderId));
            Assert.Equal("Details", viewChu.ViewName);

            // SEC-04: khach khac / vang lai deu thay CUNG form nhap ma nhu
            // don vang lai — khong phan biet bang NotFound nua.
            var ctrlKhac = TaoOrdersController(services, db, principal: NguoiDung("khac@test.vn"));
            var viewKhac = Assert.IsType<ViewResult>(await ctrlKhac.Details(orderId));
            Assert.Equal("NhapMaTraCuu", viewKhac.ViewName);

            var ctrlVangLai = TaoOrdersController(services, db, principal: KhachVangLai());
            var viewVangLai = Assert.IsType<ViewResult>(await ctrlVangLai.Details(orderId));
            Assert.Equal("NhapMaTraCuu", viewVangLai.ViewName);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Orders.HoaDon — muc controller ============

    [Fact]
    public async Task HoaDon_DonVangLai_SaiToken_TraNotFound_DungToken_TraView()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services,
                token: "abcdef0123456789abcdef0123456789", coVat: true);
            var ctrl = TaoOrdersController(services, db, principal: KhachVangLai());
            // SEC-04: GET hien form nhap ma; ma gui bang POST.
            var get = Assert.IsType<ViewResult>(await ctrl.HoaDon(orderId));
            Assert.Equal("NhapMaTraCuu", get.ViewName);
            var postSai = Assert.IsType<ViewResult>(await ctrl.HoaDon(orderId, "sai-token"));
            Assert.Equal("NhapMaTraCuu", postSai.ViewName);
            var postDung = Assert.IsType<ViewResult>(await ctrl.HoaDon(orderId, "abcdef0123456789abcdef0123456789"));
            Assert.Equal("HoaDon", postDung.ViewName);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task HoaDon_DonKhongCoThongTinVat_DungToken_VeDetails()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services,
                token: "abcdef0123456789abcdef0123456789", coVat: false);
            var ctrl = TaoOrdersController(services, db, principal: KhachVangLai());
            // Dung token nhung don khong co thong tin VAT -> ve view Details
            // (SEC-04: tra view truc tiep, khong redirect kem token nhu cu).
            var ketQua = await ctrl.HoaDon(orderId, "abcdef0123456789abcdef0123456789");
            var view = Assert.IsType<ViewResult>(ketQua);
            Assert.Equal("Details", view.ViewName);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Returns.TraCuu — muc controller + message chung ============

    [Fact]
    public async Task TraCuu_DonVangLai_DungToken_HienDon_SaiToken_MessageChung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: "abcdef0123456789abcdef0123456789");

            var ctrlDung = TaoReturnsController(services, db, principal: KhachVangLai());
            var viewDung = Assert.IsType<ViewResult>(await ctrlDung.TraCuu(orderId, "abcdef0123456789abcdef0123456789"));
            Assert.Equal(orderId, ((Order)viewDung.Model!).Id);

            // Sai token, thieu token, don khong ton tai: CUNG mot message chung,
            // khong tiet lo don co ton tai hay don thuoc loai nao.
            var loiSai = await LayLoiTraCuuAsync(services, db, orderId, "sai-token");
            var loiThieu = await LayLoiTraCuuAsync(services, db, orderId, null);
            var loiKhongCo = await LayLoiTraCuuAsync(services, db, 999999, null);
            Assert.Equal(loiSai, loiThieu);
            Assert.Equal(loiSai, loiKhongCo);
            Assert.Contains("mã tra cứu", loiSai);
            Assert.DoesNotContain("vãng lai", loiSai);
            Assert.DoesNotContain("tài khoản khác", loiSai);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task TraCuu_DonTaiKhoan_NguoiKhac_MessageChung_NhuDonKhongTonTai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var orderId = await SeedDonTaiKhoanAsync(services, "u1");

            // Khach vang lai (chua dang nhap) tra cuu don cua tai khoan khac:
            // message phai GIONG het truong hop don khong ton tai (khong lo
            // "don nay thuoc tai khoan khac" nhu truoc day).
            var loiKhac = await LayLoiTraCuuAsync(services, db, orderId, null);
            var loiKhongCo = await LayLoiTraCuuAsync(services, db, 999999, null);
            Assert.Equal(loiKhongCo, loiKhac);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Admin.CapLaiMaTraCuu ============

    [Fact]
    public async Task CapLaiMaTraCuu_DonVangLai_MaMoiCoHieuLuc_MaCuHetHieuLuc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var tokenCu = "abcdef0123456789abcdef0123456789";
            var orderId = await SeedDonVangLaiAsync(services, token: tokenCu);

            var ctrl = TaoAdminController(services, db);
            // SEC-04: cap lai can doi chieu ho ten + SDT tren don
            // (seed: "Nguyen Van A, 0900000000, Ha Noi").
            var ketQua = await ctrl.CapLaiMaTraCuu(orderId, "Nguyen Van A", "0900000000");
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("OrderDetails", redirect.ActionName);
            var thongBao = (string?)ctrl.TempData["Success"];
            Assert.NotNull(thongBao);

            // Ma goc moi chi co trong thong bao (DB chi luu hash).
            var tokenMoi = System.Text.RegularExpressions.Regex
                .Match(thongBao!, "[0-9a-f]{32}").Value;
            Assert.Equal(32, tokenMoi.Length);
            Assert.NotEqual(tokenCu, tokenMoi);
            Order? donSauDb;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                donSauDb = await ctx.Orders.FindAsync(orderId);
            }
            // DB luu SHA-256 hash (64 ky tu) cua ma moi + han 30 ngay.
            Assert.NotNull(donSauDb!.GuestToken);
            Assert.Equal(64, donSauDb.GuestToken!.Length);
            Assert.Equal(OrderAccess.BamGuestToken(tokenMoi), donSauDb.GuestToken);
            Assert.NotNull(donSauDb.GuestTokenHetHan);
            Assert.True(donSauDb.GuestTokenHetHan.Value > DateTime.Now.AddDays(29));
            // Ma cu het hieu luc ngay, ma moi co hieu luc.
            var donSau = new Order { UserId = null, GuestToken = donSauDb.GuestToken, GuestTokenHetHan = donSauDb.GuestTokenHetHan };
            Assert.False(OrderAccess.CoTheXemDonQ(donSau, null, laAdmin: false, guestToken: tokenCu));
            Assert.True(OrderAccess.CoTheXemDonQ(donSau, null, laAdmin: false, guestToken: tokenMoi));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task CapLaiMaTraCuu_SaiThongTinDoiChieu_KhongCapLai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var tokenCu = "abcdef0123456789abcdef0123456789";
            var orderId = await SeedDonVangLaiAsync(services, token: tokenCu);

            // SDT sai -> khong cap lai, token cu van nguyen.
            var ctrlSaiSdt = TaoAdminController(services, db);
            Assert.IsType<RedirectToActionResult>(
                await ctrlSaiSdt.CapLaiMaTraCuu(orderId, "Nguyen Van A", "0911111111"));
            Assert.NotNull(ctrlSaiSdt.TempData["Error"]);

            // Ten sai (khong phan biet hoa/thuong nhung phai dung ten) -> khong cap.
            var ctrlSaiTen = TaoAdminController(services, db);
            Assert.IsType<RedirectToActionResult>(
                await ctrlSaiTen.CapLaiMaTraCuu(orderId, "Tran Thi B", "0900000000"));
            Assert.NotNull(ctrlSaiTen.TempData["Error"]);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(tokenCu, (await ctx.Orders.FindAsync(orderId))!.GuestToken);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task CapLaiMaTraCuu_DonTaiKhoan_KhongCapLaiDuoc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var orderId = await SeedDonTaiKhoanAsync(services, "u1");

            var ctrl = TaoAdminController(services, db);
            var ketQua = await ctrl.CapLaiMaTraCuu(orderId, "Chu don", "0900000001");
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.NotNull(ctrl.TempData["Error"]);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await ctx.Orders.FindAsync(orderId))!.GuestToken);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task CapLaiMaTraCuu_DonKhongTonTai_TraNotFound()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            // Chua seed don nao: van phai Migrate de bang Orders ton tai.
            using (var scope = services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            var ctrl = TaoAdminController(services, db);
            Assert.IsType<NotFoundResult>(await ctrl.CapLaiMaTraCuu(999999, "Ai Do", "0900000000"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public void CapLaiMaTraCuu_ChiAdminDuocGoi_AttributeBatBuoc()
    {
        // [Authorize] khong chay khi goi truc tiep method trong test — runtime
        // moi ep buoc. Test nay khoa chac attribute con ton tai o cap class
        // AdminController (Roles = "Admin"); neu ai do go attribute thi test do.
        var attr = typeof(AdminController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();
        Assert.NotNull(attr);
        Assert.Equal("Admin", attr.Roles);
    }

    // ================= Helpers =================

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider();
    }

    private static async Task<int> SeedDonVangLaiAsync(ServiceProvider services, string token, bool coVat = false)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var order = new Order
        {
            UserId = null,
            GuestToken = token,
            DiaChiGiao = "Nguyen Van A, 0900000000, Ha Noi",
            TongTien = 100_000,
            VatTenCongTy = coVat ? "Cong ty Test" : null,
            VatMaSoThue = coVat ? "0123456789" : null,
            VatDiaChiCongTy = coVat ? "Ha Noi" : null,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<int> SeedDonTaiKhoanAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var order = new Order
        {
            UserId = userId,
            GuestToken = null,
            DiaChiGiao = "Chu don, 0900000001, Ha Noi",
            TongTien = 200_000,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId, string userName)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
        });
        await ctx.SaveChangesAsync();
    }

    private static ClaimsPrincipal KhachVangLai() =>
        new(new ClaimsIdentity(authenticationType: null));

    private static ClaimsPrincipal NguoiDung(string userName) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }, "Test"));

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin@techzone.vn"),
            new Claim(ClaimTypes.Role, "Admin"),
        }, "Test"));

    private static ControllerContext TaoContext(ClaimsPrincipal principal)
    {
        var http = new DefaultHttpContext { User = principal };
        return new ControllerContext { HttpContext = http };
    }

    private static OrdersController TaoOrdersController(ServiceProvider services, string dbPath, ClaimsPrincipal principal)
    {
        // Details/HoaDon chi dung _db (+ OrderAccess): cac service khac truyen
        // null! co chu thich — khong duoc dung cach nay cho action can chung.
        // Scope KHONG dispose: DbContext phai song trong suot luc controller chay.
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = principal };
        var ctrl = new OrdersController(db, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            // HoaDon (don khong VAT) ghi TempData["Error"] — gan truc tiep
            // vi DI toi thieu khong co ITempDataDictionaryFactory.
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
        return ctrl;
    }

    private static ReturnsController TaoReturnsController(ServiceProvider services, string dbPath, ClaimsPrincipal principal)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = principal };
        var ctrl = new ReturnsController(db, new TestWebHostEnvironment(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
        return ctrl;
    }

    private static AdminController TaoAdminController(ServiceProvider services, string dbPath)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = Admin() };
        var ctrl = new AdminController(db, new TestWebHostEnvironment(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
        return ctrl;
    }

    /// <summary>Goi TraCuu va lay message loi (null neu khong co loi — tuc la hien duoc don).</summary>
    private static async Task<string?> LayLoiTraCuuAsync(ServiceProvider services, string dbPath, int maDon, string? token)
    {
        var ctrl = TaoReturnsController(services, dbPath, principal: KhachVangLai());
        var ketQua = await ctrl.TraCuu(maDon, token);
        Assert.IsType<ViewResult>(ketQua);
        return (string?)ctrl.TempData["Error"];
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

/// <summary>Stub IWebHostEnvironment toi thieu cho test controller (khong can web root that).</summary>
internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "TechZone.Tests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>ITempDataProvider no-op: TempData chi can song trong dictionary o bo nho de test doc.</summary>
internal sealed class NoOpTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
    public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
}
