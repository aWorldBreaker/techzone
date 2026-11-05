using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test Sprint 4 — QA concurrency bo sung (worker 3).
/// (a) 2 admin doi trang thai cung 1 don cung luc: cung dich -> chi 1 lich su,
///     cung huy -> chi hoan ton 1 lan (khong double-apply).
/// (b) 2 user KHAC NHAU dung chung 1 voucher chi con 1 luot -> chi 1 nguoi duoc
///     (bien the cua Checkout_VoucherMotLuot_... hien co, von dung chung user null).
/// Moi test dung DB tam rieng (WAL), 2 scope/DbContext rieng mo phong 2 request.
/// </summary>
[Collection("DbTests")]
public class ConcurrencyQaTests
{
    private readonly ITestOutputHelper _out;

    public ConcurrencyQaTests(ITestOutputHelper output) => _out = output;

    // ============ (a1) 2 admin cung xac nhan 1 don cung luc ============

    [Fact]
    public async Task DoiTrangThai_HaiAdminCungLuc_CungDich_ChiMotLichSu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);
            var orderId = await DatDonThanhCongAsync(services, productId, 2, "QA-TT-1");

            var t1 = DoiTrangThaiAsync(services, orderId, OrderStatus.DaXacNhan);
            var t2 = DoiTrangThaiAsync(services, orderId, OrderStatus.DaXacNhan);
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/daDich={results[0].DaOTrangThaiDich}/{results[0].Loi} " +
                           $"kq2={results[1].ThanhCong}/daDich={results[1].DaOTrangThaiDich}/{results[1].Loi}");

            // Ca 2 deu thanh cong (1 that + 1 idempotent), trang thai cuoi dung,
            // NHUNG chi 1 dong lich su DaXacNhan duoc ghi — khong double-apply.
            Assert.All(results, r => Assert.True(r.ThanhCong, r.Loi));
            Assert.Single(results, r => r.DaOTrangThaiDich);
            Assert.Equal(OrderStatus.DaXacNhan, await GetTrangThaiAsync(services, orderId));
            Assert.Equal(1, await CountHistoryAsync(services, orderId, OrderStatus.DaXacNhan));
            Assert.Equal(3, await GetTonKhoAsync(services, productId)); // xac nhan khong doi ton
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (a2) 2 admin cung huy 1 don cung luc ============

    [Fact]
    public async Task HuyDon_HaiAdminCungLuc_ChiHoanTonMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);
            var orderId = await DatDonThanhCongAsync(services, productId, 2, "QA-HUY-1");
            Assert.Equal(3, await GetTonKhoAsync(services, productId));

            var t1 = HuyDonAsync(services, orderId);
            var t2 = HuyDonAsync(services, orderId);
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/daDich={results[0].DaOTrangThaiDich}/{results[0].Loi} " +
                           $"kq2={results[1].ThanhCong}/daDich={results[1].DaOTrangThaiDich}/{results[1].Loi}");

            Assert.All(results, r => Assert.True(r.ThanhCong, r.Loi));
            Assert.Single(results, r => r.DaOTrangThaiDich);
            Assert.Equal(OrderStatus.DaHuy, await GetTrangThaiAsync(services, orderId));
            Assert.Equal(5, await GetTonKhoAsync(services, productId)); // hoan dung 2, khong hoan 4
            Assert.Equal(1, await CountHistoryAsync(services, orderId, OrderStatus.DaHuy));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (b) 2 user khac nhau, 1 voucher con 1 luot ============

    [Fact]
    public async Task Checkout_VoucherMotLuot_HaiUserKhacNhau_MotThanhCong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 10, voucherSoLuong: 1);
            const string ma = "RACE-VOUCHER";
            await SeedUserAsync(services, "qa-user-a", "qa-a");
            await SeedUserAsync(services, "qa-user-b", "qa-b");

            var t1 = CheckoutRiengAsync(services, productId, 1, key: "QA-V-1",
                maVoucher: ma, giamGia: 10_000, userId: "qa-user-a");
            var t2 = CheckoutRiengAsync(services, productId, 1, key: "QA-V-2",
                maVoucher: ma, giamGia: 10_000, userId: "qa-user-b");
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/{results[0].Loi} kq2={results[1].ThanhCong}/{results[1].Loi}");

            Assert.Single(results, r => r.ThanhCong);
            Assert.Single(results, r => !r.ThanhCong);
            Assert.Contains(results, r => !r.ThanhCong && r.Loi != null && r.Loi.Contains("hết lượt"));
            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(1, await GetVoucherDaDungAsync(services, ma));
            Assert.Equal(9, await GetTonKhoAsync(services, productId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers (theo pattern CheckoutTests) =================

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

    private static async Task<int> SeedMinimalAsync(ServiceProvider services, int tonKho, int voucherSoLuong)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        var productId = await SeedProductAsync(services, "SP QA", 100_000, tonKho);
        if (voucherSoLuong > 0)
        {
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ctx.Vouchers.Add(new Voucher
            {
                Ma = "RACE-VOUCHER",
                LoaiGiam = "Tien",
                GiaTri = 10_000,
                DieuKien = 0,
                NgayHetHan = DateTime.Now.AddDays(30),
                SoLuong = voucherSoLuong,
                DaDung = 0,
            });
            await ctx.SaveChangesAsync();
        }
        return productId;
    }

    private static async Task<int> SeedProductAsync(ServiceProvider services, string ten, decimal gia, int tonKho)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == "test-cat");
        if (category is null)
        {
            category = new Category { Ten = "Test Cat", Slug = "test-cat" };
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();
        }
        var brand = await ctx.Brands.FirstOrDefaultAsync(b => b.Ten == "Test Brand");
        if (brand is null)
        {
            brand = new Brand { Ten = "Test Brand" };
            ctx.Brands.Add(brand);
            await ctx.SaveChangesAsync();
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
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId, string userName)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName + "@x.vn",
            NormalizedEmail = (userName + "@X.VN").ToUpperInvariant(),
        });
        await ctx.SaveChangesAsync();
    }

    private static CheckoutInput BaseInput(int productId, int soLuong, string key,
        string? maVoucher = null, decimal giamGia = 0, string? userId = null) => new()
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = maVoucher,
            GiamGia = giamGia,
            Lines = { new CheckoutLineInput(productId, null, soLuong, 100_000) },
            IdempotencyKey = key,
        };

    private static async Task<CheckoutResult> CheckoutRiengAsync(ServiceProvider services,
        int productId, int soLuong, string key, string? maVoucher = null,
        decimal giamGia = 0, string? userId = null)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(
            BaseInput(productId, soLuong, key, maVoucher, giamGia, userId));
    }

    private static async Task<int> DatDonThanhCongAsync(ServiceProvider services, int productId, int soLuong, string key)
    {
        var ketQua = await CheckoutRiengAsync(services, productId, soLuong, key);
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
    }

    private static async Task<DoiTrangThaiResult> DoiTrangThaiAsync(ServiceProvider services, int orderId, string trangThaiMoi)
    {
        using var scope = services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        return await orders.DoiTrangThaiAsync(orderId, trangThaiMoi);
    }

    private static async Task<DoiTrangThaiResult> HuyDonAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        return await orders.HuyDonAsync(orderId);
    }

    private static async Task<int> CountOrdersAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.CountAsync();
    }

    private static async Task<int> CountHistoryAsync(ServiceProvider services, int orderId, string trangThai)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.OrderStatusHistories
            .CountAsync(h => h.OrderId == orderId && h.TrangThai == trangThai);
    }

    private static async Task<int> GetTonKhoAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync();
    }

    private static async Task<int> GetVoucherDaDungAsync(ServiceProvider services, string ma)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Vouchers.Where(v => v.Ma == ma).Select(v => v.DaDung).FirstAsync();
    }

    private static async Task<string> GetTrangThaiAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.Where(o => o.Id == orderId).Select(o => o.TrangThai).FirstAsync();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-qa-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }
}
