using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// SEC-04 — GuestToken hardening + SEC-05 — Returns authorization.
///  - Sinh token: RandomNumberGenerator 16 byte (128 bit), hex 32 ky tu.
///  - Luu hash: SHA-256 hex 64 ky tu; verify bang FixedTimeEquals.
///  - Han dung 30 ngay; het han -> tu choi (khong dem sai).
///  - Rate limit DB: 5 lan sai lien tiep -> khoa tra cuu 15 phut.
///  - Token legacy plaintext (32 ky tu) van dung duoc; lan khop dau tien
///    nang cap len hash; backfill hash hang loat + migration an han.
///  - Returns.Details: khach khac -> 404 (SEC-05), chu don / Admin -> 200.
/// </summary>
[Collection("DbTests")]
public class GuestTokenHardeningTests
{
    private const string TokenHopLe = "abcdef0123456789abcdef0123456789";

    // ============ 1. Sinh token + hash ============

    [Fact]
    public void TaoGuestToken_128Bit_Hex32_KhacNhauMoiLan()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => OrderAccess.TaoGuestToken()).ToList();
        Assert.Equal(100, tokens.Distinct().Count());
        foreach (var t in tokens)
        {
            Assert.Equal(32, t.Length);
            Assert.Matches("^[0-9a-f]{32}$", t);
        }
    }

    [Fact]
    public void BamGuestToken_SHA256_64Hex_OnDinh_KhacPlaintext()
    {
        // Vector chuan: SHA-256("abc").
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            OrderAccess.BamGuestToken("abc"));
        var h1 = OrderAccess.BamGuestToken(TokenHopLe);
        Assert.Equal(64, h1.Length);
        Assert.Matches("^[0-9a-f]{64}$", h1);
        Assert.Equal(h1, OrderAccess.BamGuestToken(TokenHopLe));
        Assert.NotEqual(TokenHopLe, h1);
    }

    [Fact]
    public void KhopTokenQ_HashDungDuocXem_SaiVaDonKhacBiChan()
    {
        var don = new Order { UserId = null, GuestToken = OrderAccess.BamGuestToken(TokenHopLe) };
        Assert.True(OrderAccess.CoTheXemDonQ(don, null, laAdmin: false, guestToken: TokenHopLe));
        Assert.False(OrderAccess.CoTheXemDonQ(don, null, laAdmin: false, guestToken: "sai-token"));
        // Sai hoa-thuong cung bi chan (hash phan biet hoa/thuong).
        Assert.False(OrderAccess.CoTheXemDonQ(don, null, laAdmin: false,
            guestToken: TokenHopLe.ToUpperInvariant()));
        // Token cua don khac (hash khac) bi chan.
        var donKhac = new Order { UserId = null, GuestToken = OrderAccess.BamGuestToken("11111111111111111111111111111111") };
        Assert.False(OrderAccess.CoTheXemDonQ(don, null, laAdmin: false, guestToken: "11111111111111111111111111111111"));
        Assert.False(OrderAccess.CoTheXemDonQ(donKhac, null, laAdmin: false, guestToken: TokenHopLe));
    }

    // ============ 2. Han dung ============

    [Fact]
    public void HetHan_TokenHetHanBiChan_TokenConHanDuocXem()
    {
        var now = DateTime.Now;
        var hetHan = new Order
        {
            UserId = null,
            GuestToken = OrderAccess.BamGuestToken(TokenHopLe),
            GuestTokenHetHan = now.AddDays(-1),
        };
        Assert.False(OrderAccess.CoTheXemDonQ(hetHan, null, laAdmin: false, guestToken: TokenHopLe, now: now));
        // Het han thi XacMinhToken tu choi NHUNG khong dem sai.
        Assert.False(OrderAccess.XacMinhToken(hetHan, TokenHopLe, now));
        Assert.Equal(0, hetHan.GuestTokenSaiLan);

        var conHan = new Order
        {
            UserId = null,
            GuestToken = OrderAccess.BamGuestToken(TokenHopLe),
            GuestTokenHetHan = now.AddDays(30),
        };
        Assert.True(OrderAccess.CoTheXemDonQ(conHan, null, laAdmin: false, guestToken: TokenHopLe, now: now));
    }

    // ============ 3. Legacy plaintext + nang cap + backfill ============

    [Fact]
    public void LegacyPlaintext_VanVerifyDuoc_VaNangCapLenHash()
    {
        var don = new Order { UserId = null, GuestToken = TokenHopLe };
        Assert.True(OrderAccess.XacMinhToken(don, TokenHopLe, DateTime.Now));
        // Lan khop dau tien da ghi de plaintext bang hash.
        Assert.Equal(64, don.GuestToken!.Length);
        Assert.Equal(OrderAccess.BamGuestToken(TokenHopLe), don.GuestToken);
        // Verify lai bang duong hash van dung.
        Assert.True(OrderAccess.XacMinhToken(don, TokenHopLe, DateTime.Now));
    }

    [Fact]
    public async Task Backfill_HashHangLoat_ChiHashLegacy_AnHan30Ngay()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: TokenHopLe, legacy: true);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Don da hash san (khong phai legacy) phai duoc bo qua.
                ctx.Orders.Add(new Order
                {
                    UserId = null,
                    GuestToken = OrderAccess.BamGuestToken("22222222222222222222222222222222"),
                    GuestTokenHetHan = DateTime.Now.AddDays(30),
                    DiaChiGiao = "Da Hash, 0900000002, Ha Noi",
                });
                await ctx.SaveChangesAsync();
            }
            int soDon;
            using (var scope = services.CreateScope())
                soDon = await GuestTokenBackfill.ChayAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            Assert.Equal(1, soDon);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var don = (await ctx.Orders.FindAsync(orderId))!;
                Assert.Equal(OrderAccess.BamGuestToken(TokenHopLe), don.GuestToken);
                Assert.NotNull(don.GuestTokenHetHan);
                Assert.True(don.GuestTokenHetHan.Value > DateTime.Now.AddDays(29));
                // Chay lai: idempotent, khong hash them.
                Assert.Equal(0, await GuestTokenBackfill.ChayAsync(ctx));
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 4. Rate limit ============

    [Fact]
    public void RateLimit_5SaiKhoa15Phut_DungTokenCungBiChan_HetKhoaDuocXem()
    {
        var now = DateTime.Now;
        var don = new Order { UserId = null, GuestToken = OrderAccess.BamGuestToken(TokenHopLe) };
        for (var i = 0; i < 4; i++)
        {
            Assert.False(OrderAccess.XacMinhToken(don, "sai-token", now));
            Assert.False(OrderAccess.BiKhoaQ(don, now));
        }
        // Lan sai thu 5 -> khoa 15 phut, dem reset ve 0.
        Assert.False(OrderAccess.XacMinhToken(don, "sai-token", now));
        Assert.True(OrderAccess.BiKhoaQ(don, now));
        Assert.Equal(0, don.GuestTokenSaiLan);
        Assert.True(don.GuestTokenKhoaDen.HasValue
            && don.GuestTokenKhoaDen.Value <= now.AddMinutes(15)
            && don.GuestTokenKhoaDen.Value > now.AddMinutes(14));
        // Dang khoa: dung token that cung bi chan, khong dem them.
        Assert.False(OrderAccess.XacMinhToken(don, TokenHopLe, now));
        // Het khoa (16 phut sau): dung token lai duoc, dem/khoa reset.
        var sau = now.AddMinutes(16);
        Assert.True(OrderAccess.XacMinhToken(don, TokenHopLe, sau));
        Assert.Equal(0, don.GuestTokenSaiLan);
        Assert.Null(don.GuestTokenKhoaDen);
    }

    [Fact]
    public void RateLimit_DungTokenResetDemSai()
    {
        var now = DateTime.Now;
        var don = new Order { UserId = null, GuestToken = OrderAccess.BamGuestToken(TokenHopLe) };
        Assert.False(OrderAccess.XacMinhToken(don, "sai-1", now));
        Assert.False(OrderAccess.XacMinhToken(don, "sai-2", now));
        Assert.Equal(2, don.GuestTokenSaiLan);
        Assert.True(OrderAccess.XacMinhToken(don, TokenHopLe, now));
        Assert.Equal(0, don.GuestTokenSaiLan);
    }

    // ============ 5. Rate limit muc controller (TraCuu + Details POST) ============

    [Fact]
    public async Task TraCuu_BruteForce5Lan_KhoaDon_DungTokenVanBiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: TokenHopLe, legacy: true);
            for (var i = 0; i < 5; i++)
            {
                var loi = await LayLoiTraCuuAsync(services, db, orderId, "sai-token");
                Assert.Contains("mã tra cứu", loi);
            }
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var don = (await ctx.Orders.FindAsync(orderId))!;
                Assert.NotNull(don.GuestTokenKhoaDen);
                Assert.True(don.GuestTokenKhoaDen.Value > DateTime.Now);
            }
            // Dang khoa: dung token that cung chi thay loi chung.
            var loiDung = await LayLoiTraCuuAsync(services, db, orderId, TokenHopLe);
            Assert.Contains("mã tra cứu", loiDung);
            Assert.DoesNotContain("khóa", loiDung);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task DetailsPost_BruteForce5Lan_KhoaDon_DungTokenVanBiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: TokenHopLe, legacy: true);
            var ctrl = TaoOrdersController(services, db, principal: KhachVangLai());
            for (var i = 0; i < 5; i++)
            {
                var kq = Assert.IsType<ViewResult>(await ctrl.Details(orderId, "sai-token"));
                Assert.Equal("NhapMaTraCuu", kq.ViewName);
            }
            var kqDung = Assert.IsType<ViewResult>(await ctrl.Details(orderId, TokenHopLe));
            Assert.Equal("NhapMaTraCuu", kqDung.ViewName);
            Assert.Contains("mã tra cứu", (string)ctrl.ViewBag.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 6. SEC-05 — Returns.Details: Forbid -> NotFound ============

    [Fact]
    public async Task ReturnsDetails_KhachKhac404_ChuDon200_Admin200()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            await SeedUserAsync(services, "u2", "khac@test.vn");
            var (doiTraId, baoHanhId) = await SeedYeuCauAsync(services, "u1");

            // Doi tra: khach khac -> 404 (truoc day 403 Forbid).
            var ctrlKhac = TaoReturnsController(services, db, principal: NguoiDung("khac@test.vn"));
            Assert.IsType<NotFoundResult>(await ctrlKhac.Details(doiTraId, "doitra"));
            Assert.IsType<NotFoundResult>(await ctrlKhac.Details(baoHanhId, "baohanh"));
            // Chu don -> 200.
            var ctrlChu = TaoReturnsController(services, db, principal: NguoiDung("chu@test.vn"));
            Assert.IsType<ViewResult>(await ctrlChu.Details(doiTraId, "doitra"));
            Assert.IsType<ViewResult>(await ctrlChu.Details(baoHanhId, "baohanh"));
            // Admin -> 200.
            var ctrlAdmin = TaoReturnsController(services, db, principal: Admin());
            Assert.IsType<ViewResult>(await ctrlAdmin.Details(doiTraId, "doitra"));
            Assert.IsType<ViewResult>(await ctrlAdmin.Details(baoHanhId, "baohanh"));
            // Yeu cau khong ton tai van 404 nhu cu.
            Assert.IsType<NotFoundResult>(await ctrlChu.Details(999999, "doitra"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 7. Cap lai ma mo khoa + migration ============

    [Fact]
    public async Task CapLaiMaTraCuu_DonDangBiKhoa_MoKhoaVaCapMaMoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var orderId = await SeedDonVangLaiAsync(services, token: TokenHopLe, legacy: true);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var don = (await ctx.Orders.FindAsync(orderId))!;
                don.GuestTokenKhoaDen = DateTime.Now.AddMinutes(15);
                await ctx.SaveChangesAsync();
            }
            var ctrl = TaoAdminController(services, db);
            var ketQua = await ctrl.CapLaiMaTraCuu(orderId, "Nguyen Van A", "0900000000");
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.NotNull(ctrl.TempData["Success"]);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var don = (await ctx.Orders.FindAsync(orderId))!;
                Assert.Null(don.GuestTokenKhoaDen);
                Assert.Equal(0, don.GuestTokenSaiLan);
                var tokenMoi = Regex.Match((string)ctrl.TempData["Success"]!, "[0-9a-f]{32}").Value;
                Assert.Equal(OrderAccess.BamGuestToken(tokenMoi), don.GuestToken);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Migration_TokenCu_AnHan30Ngay_TokenCuVanDungDuoc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            // Dung DB o trang thai truoc SEC-04 (chi toi AddGuestToken),
            // seed don vang lai token plaintext cu, roi migrate len moi nhat.
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Migrate co dich: qua IMigrator (DatabaseFacade chi co
                // Migrate() khong tham so trong EF Core 8).
                ctx.GetService<IMigrator>().Migrate("20261106010000_AddGuestToken");
                // Seed don cu bang SQL tho: entity Order hien tai co them
                // 3 cot SEC-04 chua ton tai o schema cu nen INSERT qua EF
                // se loi "no such column".
                await ctx.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"Orders\" (\"TongTien\", \"PhiShip\", \"GiamGia\", \"TrangThai\", " +
                    "\"DiaChiGiao\", \"PhuongThucTT\", \"NgayDat\", \"TongVat\", \"GiamGiaCombo\", " +
                    "\"ComboSoBo\", \"DiemDaDung\", \"GiamGiaDiem\", \"GiamGiaHang\", \"DaThanhToan\", \"GuestToken\") " +
                    "VALUES (100000, 0, 0, 'Chờ xác nhận', 'Nguyen Van A, 0900000000, Ha Noi', " +
                    "'COD', datetime('now'), 0, 0, 0, 0, 0, 0, 0, {0})",
                    TokenHopLe);
            }
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                Assert.Empty(ctx.Database.GetPendingMigrations());
                Assert.Contains(ctx.Database.GetAppliedMigrations(),
                    m => m.EndsWith("_GuestTokenHardening"));
                var don = await ctx.Orders.FirstAsync();
                // Migration an han 30 ngay cho token cu (khong chet ngay).
                Assert.NotNull(don.GuestTokenHetHan);
                Assert.True(don.GuestTokenHetHan.Value > DateTime.Now.AddDays(29));
                // Token cu van dung duoc ngay sau migrate (duong doc kep),
                // va nang cap len hash o lan khop dau tien.
                Assert.True(OrderAccess.XacMinhToken(don, TokenHopLe, DateTime.Now));
                Assert.Equal(OrderAccess.BamGuestToken(TokenHopLe), don.GuestToken);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers (mau GuestTokenTests) =================

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider();
    }

    /// <summary>Seed don vang lai. legacy=true: luu plaintext 32 ky tu nhu truoc SEC-04.</summary>
    private static async Task<int> SeedDonVangLaiAsync(ServiceProvider services, string token, bool legacy = false)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var order = new Order
        {
            UserId = null,
            GuestToken = legacy ? token : OrderAccess.BamGuestToken(token),
            GuestTokenHetHan = legacy ? null : DateTime.Now.AddDays(30),
            DiaChiGiao = "Nguyen Van A, 0900000000, Ha Noi",
            TongTien = 100_000,
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

    private static async Task<(int DoiTraId, int BaoHanhId)> SeedYeuCauAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var category = new Category { Ten = "Dien thoai", Slug = "dien-thoai" };
        var brand = new Brand { Ten = "Hang Test" };
        ctx.Categories.Add(category);
        ctx.Brands.Add(brand);
        await ctx.SaveChangesAsync();
        var product = new Product { Ten = "SP Test", Gia = 100_000, CategoryId = category.Id, BrandId = brand.Id };
        ctx.Products.Add(product);
        var order = new Order { UserId = userId, DiaChiGiao = "Chu don, 0900000001, Ha Noi", TongTien = 100_000 };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        var doiTra = new ReturnRequest { OrderId = order.Id, ProductId = product.Id, UserId = userId, LyDo = "Loi", TrangThai = "Chờ duyệt" };
        var baoHanh = new WarrantyClaim { OrderId = order.Id, ProductId = product.Id, UserId = userId, MoTaLoi = "Hong", TrangThai = "Chờ duyệt" };
        ctx.ReturnRequests.Add(doiTra);
        ctx.WarrantyClaims.Add(baoHanh);
        await ctx.SaveChangesAsync();
        return (doiTra.Id, baoHanh.Id);
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

    private static OrdersController TaoOrdersController(ServiceProvider services, string dbPath, ClaimsPrincipal principal)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = principal };
        return new OrdersController(db, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
    }

    private static ReturnsController TaoReturnsController(ServiceProvider services, string dbPath, ClaimsPrincipal principal)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = principal };
        return new ReturnsController(db, new TestWebHostEnvironment(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
    }

    private static AdminController TaoAdminController(ServiceProvider services, string dbPath)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = Admin() };
        return new AdminController(db, new TestWebHostEnvironment(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
    }

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
