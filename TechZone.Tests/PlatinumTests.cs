using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test Sprint 3 — B-6: hang Bach kim (>= 50.000.000d chi tieu hop le).
/// Chi tieu hop le = don khong huy, tru so tien da hoan theo tung don
/// (hoan 1 phan chi tru phan hoan; hoan het thi don dong gop 0).
/// Ghi chu: don ChoXacNhan van tinh (hanh vi cu — test demo 33.950.000d
/// phu thuoc vao day); chi loai khi that su DaHuy / da hoan tien.
/// </summary>
[Collection("DbTests")]
public class PlatinumTests
{
    private readonly ITestOutputHelper _out;

    public PlatinumTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Platinum_Dung50Tr_DatBachKim()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            await SeedOrderAsync(services, "user-pt", 50_000_000m);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(50_000_000m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Bạch kim", await loyalty.HangAsync("user-pt"));
            Assert.Equal("Bạch kim", LoyaltyService.HangTuChiTieu(50_000_000m));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_49999999_ChuaDat_VanVang()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            await SeedOrderAsync(services, "user-pt", 49_999_999m);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(49_999_999m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Vàng", await loyalty.HangAsync("user-pt"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_DonHuy_KhongTinh()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            await SeedOrderAsync(services, "user-pt", 50_000_000m);
            var donHuy = await SeedOrderAsync(services, "user-pt", 50_000_000m);

            Assert.True((await HuyDonAsync(services, donHuy)).ThanhCong);
            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(50_000_000m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Bạch kim", await loyalty.HangAsync("user-pt"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_ChiDonHuy_KhongCoChiTieu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            var donHuy = await SeedOrderAsync(services, "user-pt", 60_000_000m);

            Assert.True((await HuyDonAsync(services, donHuy)).ThanhCong);
            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(0m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Đồng", await loyalty.HangAsync("user-pt"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_DonHoanTienToanBo_KhongTinh()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            // Don DaGiao + da thanh toan, hoan het (60tr tien hang + 30k ship).
            var donId = await SeedOrderAsync(services, "user-pt", 60_000_000m,
                trangThai: OrderStatus.DaGiao, daThanhToan: true, phiShip: 30_000m);
            await SeedRefundAsync(services, donId, 60_030_000m);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(0m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Đồng", await loyalty.HangAsync("user-pt"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_DonHoanTienMotPhan_ChiTruPhanHoan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            var donId = await SeedOrderAsync(services, "user-pt", 60_000_000m,
                trangThai: OrderStatus.DaGiao, daThanhToan: true);
            await SeedRefundAsync(services, donId, 15_000_000m); // tra 1 phan

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(45_000_000m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Vàng", await loyalty.HangAsync("user-pt")); // 45tr < 50tr
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_SpendDiem_KhongGiamTongChi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-pt");
            var donId = await SeedOrderAsync(services, "user-pt", 55_000_000m);
            await SeedEarnAsync(services, "user-pt", 550, donId);
            await SeedSpendAsync(services, "user-pt", 200, donId);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(350, await loyalty.TongDiemAsync("user-pt")); // 550 - 200
            Assert.Equal(55_000_000m, await loyalty.TongChiTieuAsync("user-pt"));
            Assert.Equal("Bạch kim", await loyalty.HangAsync("user-pt"));
            // Quyen loi Vang van giu o hang Bach kim: giam 2% + free ship.
            Assert.Equal(1_100_000m, await loyalty.GiamGiaHangAsync("user-pt", 55_000_000m));
            Assert.True(await loyalty.LaHangVangAsync("user-pt"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_BienVang_GiuNguyen()
    {
        var db = TempDbPath();
        try
        {
            // Dung 20.000.000d = Bac (bien strict > nhu cu).
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-bac");
            await SeedOrderAsync(services, "user-bac", 20_000_000m);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                Assert.Equal("Bạc", await loyalty.HangAsync("user-bac"));
            }
            // 20.000.001d = Vang.
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 20_000_001m);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                Assert.Equal("Vàng", await loyalty.HangAsync("user-vang"));
            }
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_KhachDemo337Diem_VanVang()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-demo");
            var don1 = await SeedOrderAsync(services, "user-demo", 13_680_000m);
            var don2 = await SeedOrderAsync(services, "user-demo", 18_490_000m);
            var don3 = await SeedOrderAsync(services, "user-demo", 1_780_000m);
            await SeedEarnAsync(services, "user-demo", 136, don1);
            await SeedEarnAsync(services, "user-demo", 184, don2);
            await SeedEarnAsync(services, "user-demo", 17, don3);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(337, await loyalty.TongDiemAsync("user-demo"));
            Assert.Equal(33_950_000m, await loyalty.TongChiTieuAsync("user-demo"));
            Assert.Equal("Vàng", await loyalty.HangAsync("user-demo"));
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Platinum_CauHinh_NguongThayDoiDuoc()
    {
        var db = TempDbPath();
        try
        {
            // Dang ky IOptions rieng: moc Bach kim ha xuong 10tr.
            var services = BuildServices(db, o => o.PlatinumThreshold = 10_000_000m);
            await SeedUserAsync(services, "user-cfg");
            await SeedOrderAsync(services, "user-cfg", 12_000_000m);

            using var scope = services.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
            Assert.Equal(10_000_000m, loyalty.NguongBachKim);
            Assert.Equal("Bạch kim", await loyalty.HangAsync("user-cfg"));
            _out.WriteLine("Nguong Bach kim doc tu IOptions, khong hard-code.");
        }
        finally { DeleteDb(db); }
    }

    // ================= Helpers =================

    private static ServiceProvider BuildServices(string dbPath,
        Action<LoyaltyOptions>? cauHinhLoyalty = null)
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
        if (cauHinhLoyalty is not null)
            services.Configure<LoyaltyOptions>(cauHinhLoyalty);
        services.AddScoped<LoyaltyService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureDbAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await ctx.Users.AnyAsync(u => u.Id == userId)) return;
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "test-" + userId,
            NormalizedUserName = ("TEST-" + userId).ToUpperInvariant(),
            Email = userId + "@x.vn",
            NormalizedEmail = (userId + "@X.VN").ToUpperInvariant(),
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> SeedOrderAsync(ServiceProvider services, string userId,
        decimal tongTien, string trangThai = OrderStatus.ChoXacNhan,
        bool daThanhToan = false, decimal phiShip = 0)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order
        {
            UserId = userId,
            TrangThai = trangThai,
            DiaChiGiao = "Test",
            PhuongThucTT = "COD",
            NgayDat = DateTime.Now,
            TongTien = tongTien,
            PhiShip = phiShip,
            DaThanhToan = daThanhToan,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SeedRefundAsync(ServiceProvider services, int orderId, decimal soTien)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Refunds.Add(new RefundRecord
        {
            OrderId = orderId,
            ReturnRequestId = null,
            SoTien = soTien,
            PhuongThucTT = "COD",
            TrangThai = "Đã hoàn",
            NgayTao = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedEarnAsync(ServiceProvider services, string userId, int diem, int? orderId = null)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PointTransactions.Add(new PointTransaction
        {
            UserId = userId, Diem = diem, Loai = "Earn",
            OrderId = orderId, NgayTao = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedSpendAsync(ServiceProvider services, string userId, int diem, int? orderId = null)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PointTransactions.Add(new PointTransaction
        {
            UserId = userId, Diem = diem, Loai = "Spend",
            OrderId = orderId, NgayTao = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<DoiTrangThaiResult> HuyDonAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        return await orders.HuyDonAsync(orderId);
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
