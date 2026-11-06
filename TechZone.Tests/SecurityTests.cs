using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Test P0 Security (SEC-01/02/03):
/// - SEC-02: ma tra cuu GuestToken cho don khach vang lai (OrderAccess + CheckoutService).
/// - SEC-01: tai khoan demo doc tu cau hinh DemoAccounts (SeedData).
/// Chay tuan tu theo [Collection("DbTests")] giong cac test khac.
/// </summary>
[Collection("DbTests")]
public class SecurityTests
{
    // ============ SEC-02: OrderAccess.CoTheXemDonQ — don vang lai ============

    [Fact]
    public void GuestToken_DonVangLai_KhongToken_BiChan()
    {
        var order = new Order { UserId = null, GuestToken = "abcdef0123456789abcdef0123456789" };
        Assert.False(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: null));
        Assert.False(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: ""));
    }

    [Fact]
    public void GuestToken_DonVangLai_SaiToken_BiChan()
    {
        var order = new Order { UserId = null, GuestToken = "abcdef0123456789abcdef0123456789" };
        Assert.False(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: "sai-token"));
        // Token dung nhung sai hoa/thuong cung bi chan (so sanh Ordinal).
        Assert.False(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: "ABCDEF0123456789ABCDEF0123456789"));
    }

    [Fact]
    public void GuestToken_DonVangLai_DungToken_DuocXem()
    {
        var order = new Order { UserId = null, GuestToken = "abcdef0123456789abcdef0123456789" };
        Assert.True(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: "abcdef0123456789abcdef0123456789"));
        // Admin xem duoc ma khong can token.
        Assert.True(OrderAccess.CoTheXemDonQ(order, null, laAdmin: true, guestToken: null));
    }

    [Fact]
    public void GuestToken_TaoGuestToken_32KyTu_KhacNhauMoiLan()
    {
        var t1 = OrderAccess.TaoGuestToken();
        var t2 = OrderAccess.TaoGuestToken();
        Assert.Equal(32, t1.Length);
        Assert.Equal(32, t2.Length);
        Assert.NotEqual(t1, t2);
    }

    // ============ SEC-02: OrderAccess.CoTheXemDonQ — don cua tai khoan ============

    [Fact]
    public void DonCuaTaiKhoan_ChuDonDuocXem_NguoiKhacBiChan_AdminDuocXem()
    {
        var order = new Order { UserId = "u1", GuestToken = null };
        Assert.True(OrderAccess.CoTheXemDonQ(order, "u1", laAdmin: false, guestToken: null));
        Assert.False(OrderAccess.CoTheXemDonQ(order, "u2", laAdmin: false, guestToken: null));
        Assert.False(OrderAccess.CoTheXemDonQ(order, null, laAdmin: false, guestToken: null));
        Assert.True(OrderAccess.CoTheXemDonQ(order, "u2", laAdmin: true, guestToken: null));
        // Don cua tai khoan khong dung GuestToken (token la cua don vang lai).
        Assert.False(OrderAccess.CoTheXemDonQ(order, "u2", laAdmin: false, guestToken: "bat-ky-token-nao"));
    }

    // ============ SEC-02: CheckoutService sinh GuestToken ============

    [Fact]
    public async Task Checkout_DonVangLai_SinhGuestToken_DonTaiKhoan_KhongCo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 10);

            // Don vang lai (UserId = null) -> DB chi luu SHA-256 hash (64 ky
            // tu) cua ma tra cuu; ma goc (32 ky tu) tra ve qua GuestTokenMoi
            // de hien 1 lan (SEC-04); han 30 ngay ke tu ngay dat.
            var ketQuaVangLai = await CheckoutRiengAsync(services, productId, 1, key: "SEC-GUEST-1", userId: null);
            Assert.True(ketQuaVangLai.ThanhCong, ketQuaVangLai.Loi);
            Assert.NotNull(ketQuaVangLai.DonHang!.GuestToken);
            Assert.Equal(64, ketQuaVangLai.DonHang.GuestToken!.Length);
            Assert.NotNull(ketQuaVangLai.GuestTokenMoi);
            Assert.Equal(32, ketQuaVangLai.GuestTokenMoi!.Length);
            Assert.Equal(OrderAccess.BamGuestToken(ketQuaVangLai.GuestTokenMoi),
                ketQuaVangLai.DonHang.GuestToken);
            Assert.NotNull(ketQuaVangLai.DonHang.GuestTokenHetHan);
            Assert.True(ketQuaVangLai.DonHang.GuestTokenHetHan.Value
                > ketQuaVangLai.DonHang.NgayDat.AddDays(OrderAccess.HanTokenNgay).AddMinutes(-1));

            // Don cua tai khoan -> GuestToken null.
            await SeedUserAsync(services, "user-test-1");
            var ketQuaTaiKhoan = await CheckoutRiengAsync(services, productId, 1, key: "SEC-ACC-1", userId: "user-test-1");
            Assert.True(ketQuaTaiKhoan.ThanhCong, ketQuaTaiKhoan.Loi);
            Assert.Null(ketQuaTaiKhoan.DonHang!.GuestToken);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SEC-01: SeedData DemoAccounts ============

    [Fact]
    public async Task SeedData_DemoAccounts_Tat_KhongTaoTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "false",
            }).Build();
            var services = BuildServices(db, config);
            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);

            using var scope2 = services.CreateScope();
            var userManager = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Null(await userManager.FindByEmailAsync("admin@techzone.vn"));
            Assert.Null(await userManager.FindByEmailAsync("khach@techzone.vn"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task SeedData_DemoAccounts_Bat_TaoDuTaiKhoanDemo()
    {
        var db = TempDbPath();
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = "admin@techzone.vn",
                ["DemoAccounts:AdminPassword"] = "AdminTest123",
                ["DemoAccounts:CustomerEmail"] = "khach@techzone.vn",
                ["DemoAccounts:CustomerPassword"] = "KhachTest123",
            }).Build();
            var services = BuildServices(db, config);
            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);

            using var scope2 = services.CreateScope();
            var userManager = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync("admin@techzone.vn");
            var khach = await userManager.FindByEmailAsync("khach@techzone.vn");
            Assert.NotNull(admin);
            Assert.NotNull(khach);
            Assert.True(await userManager.IsInRoleAsync(admin!, "Admin"));
            Assert.True(await userManager.IsInRoleAsync(khach!, "Khach"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers (theo pattern CheckoutTests) =================

    private static ServiceProvider BuildServices(string dbPath, IConfiguration? config = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
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
        return services.BuildServiceProvider();
    }

    /// <summary>Seed toi thieu: 1 danh muc + 1 hang + 1 SP (gia 100.000).</summary>
    private static async Task<int> SeedMinimalAsync(ServiceProvider services, int tonKho)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Ten = "Test Cat", Slug = "test-cat" };
            ctx.Categories.Add(category);
            var brand = new Brand { Ten = "Test Brand" };
            ctx.Brands.Add(brand);
            await ctx.SaveChangesAsync();
            var product = new Product
            {
                Ten = "SP Test",
                Gia = 100_000,
                CategoryId = category.Id,
                BrandId = brand.Id,
                TonKho = tonKho,
                LuotBan = 0,
            };
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync();
            return product.Id;
        }
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

    private static async Task<CheckoutResult> CheckoutRiengAsync(ServiceProvider services,
        int productId, int soLuong, string key, string? userId = null)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, soLuong, 100_000) },
            IdempotencyKey = key,
        });
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
