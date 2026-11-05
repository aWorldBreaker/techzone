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
/// Test Sprint 3 — B-7: Combo thanh thuc the DB.
/// CheckoutService tinh lai giam gia combo tu DB (khong tin client),
/// tu choi combo het han / da tat / het suat / thieu mon, tru suat bang
/// UPDATE co dieu kien (2 nguoi tranh suat cuoi -> chi 1 thang), hoan suat
/// khi huy don. Giong mau CheckoutTests: moi test 1 file DB tam (WAL).
/// </summary>
[Collection("DbTests")]
public class ComboTests
{
    private readonly ITestOutputHelper _out;

    public ComboTests(ITestOutputHelper output) => _out = output;

    // ============ Combo het han -> tu choi ============

    [Fact]
    public async Task Combo_HetHan_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b,
                ketThuc: DateTime.Now.AddDays(-1));

            var ketQua = await DatHangComboAsync(services, comboId, a, b, "EXP-1");

            Assert.False(ketQua.ThanhCong);
            Assert.Contains("hết hạn", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(0, await GetComboDaBanAsync(services, comboId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Combo da tat -> tu choi ============

    [Fact]
    public async Task Combo_DaTat_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b, isActive: false);

            var ketQua = await DatHangComboAsync(services, comboId, a, b, "OFF-1");

            Assert.False(ketQua.ThanhCong);
            Assert.Contains("đã tắt", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Combo vuot gioi han SL -> tu choi ============

    [Fact]
    public async Task Combo_VuotGioiHanSL_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b,
                gioiHanSL: 1, daBan: 1);

            var ketQua = await DatHangComboAsync(services, comboId, a, b, "LIMIT-1");

            Assert.False(ketQua.ThanhCong);
            Assert.Contains("hết suất", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(1, await GetComboDaBanAsync(services, comboId));
            Assert.Equal(5, await GetTonKhoAsync(services, a));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Gio thieu 1 mon cua combo -> tu choi ============

    [Fact]
    public async Task Combo_ThieuMon_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b);

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                ComboId = comboId,
                Lines = { new CheckoutLineInput(a, null, 1, 100_000) }, // thieu SP B
                IdempotencyKey = "MISS-1",
            });

            Assert.False(ketQua.ThanhCong);
            Assert.Contains("chưa đủ món", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Checkout combo dung gia: client gui sai van tinh dung ============

    [Fact]
    public async Task Checkout_ComboDungGia_ClientGuiSaiVanTinhDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b); // 10% tren 150.000

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                ComboId = comboId,
                GiamGiaCombo = 999_999, // client noi doi: phai bi tinh lai
                Lines =
                {
                    new CheckoutLineInput(a, null, 1, 100_000),
                    new CheckoutLineInput(b, null, 1, 50_000),
                },
                IdempotencyKey = "PRICE-OK-1",
            });

            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            var don = ketQua.DonHang!;
            Assert.Equal(comboId, don.ComboId);
            Assert.Equal(1, don.ComboSoBo);
            Assert.Equal(15_000, don.GiamGiaCombo); // 10% x (100.000 + 50.000)
            Assert.Equal(135_000, don.TongTien); // 150.000 - 15.000
            Assert.Equal(30_000, don.PhiShip); // duoi 2.000.000 sau giam
            Assert.Equal(1, await GetComboDaBanAsync(services, comboId));
            Assert.Equal(4, await GetTonKhoAsync(services, a));
            Assert.Equal(4, await GetTonKhoAsync(services, b));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 2 bo trong gio -> giam 2 lan ============

    [Fact]
    public async Task Checkout_ComboHaiBo_GiamHaiLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b);

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                ComboId = comboId,
                Lines =
                {
                    new CheckoutLineInput(a, null, 2, 100_000),
                    new CheckoutLineInput(b, null, 2, 50_000),
                },
                IdempotencyKey = "TWO-SETS-1",
            });

            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            Assert.Equal(2, ketQua.DonHang!.ComboSoBo);
            Assert.Equal(30_000, ketQua.DonHang.GiamGiaCombo); // 10% x 300.000
            Assert.Equal(270_000, ketQua.DonHang.TongTien);
            Assert.Equal(2, await GetComboDaBanAsync(services, comboId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Race: 2 nguoi tranh suat combo cuoi -> chi 1 thang ============

    [Fact]
    public async Task Checkout_ComboSuatCuoi_HaiNguoiDongThoi_MotThanhCong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b, gioiHanSL: 1, daBan: 0);

            var t1 = DatHangComboAsync(services, comboId, a, b, "RACE-C-1");
            var t2 = DatHangComboAsync(services, comboId, a, b, "RACE-C-2");
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/{results[0].Loi} kq2={results[1].ThanhCong}/{results[1].Loi}");

            Assert.Single(results, r => r.ThanhCong);
            Assert.Single(results, r => !r.ThanhCong);
            Assert.Contains(results, r => !r.ThanhCong && r.Loi != null && r.Loi.Contains("hết suất"));
            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(1, await GetComboDaBanAsync(services, comboId));
            Assert.Equal(4, await GetTonKhoAsync(services, a)); // chi tru 1 lan
            Assert.Equal(4, await GetTonKhoAsync(services, b));
            Assert.True(await GetTonKhoAsync(services, a) >= 0);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Huy don -> hoan suat combo ============

    [Fact]
    public async Task HuyDon_HoanSuatCombo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services, a, b, gioiHanSL: 5, daBan: 0);

            var ketQua = await DatHangComboAsync(services, comboId, a, b, "CANCEL-1");
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            Assert.Equal(1, await GetComboDaBanAsync(services, comboId));

            using (var scope = services.CreateScope())
            {
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var huy = await orders.HuyDonAsync(ketQua.DonHang!.Id);
                Assert.True(huy.ThanhCong, huy.Loi);
            }
            Assert.Equal(0, await GetComboDaBanAsync(services, comboId));
            Assert.Equal(5, await GetTonKhoAsync(services, a)); // ton cung duoc hoan
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

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

    /// <summary>2 SP: A 100.000d + B 50.000d, moi mon ton 5.</summary>
    private static async Task<(int, int)> SeedHaiSanPhamAsync(ServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        var a = await SeedProductAsync(services, "SP Combo A", 100_000, tonKho: 5);
        var b = await SeedProductAsync(services, "SP Combo B", 50_000, tonKho: 5);
        return (a, b);
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

    /// <summary>Combo A + B, giam 10% mac dinh, han 30 ngay, khong gioi han (neu khong truyen).</summary>
    private static async Task<int> SeedComboAsync(ServiceProvider services, int productA, int productB,
        decimal phanTram = 10, DateTime? ketThuc = null, bool isActive = true,
        int? gioiHanSL = null, int daBan = 0)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var combo = new Combo
        {
            Ten = "Combo Test A+B",
            PhanTramGiam = phanTram,
            BatDau = DateTime.Now.AddDays(-1),
            KetThuc = ketThuc ?? DateTime.Now.AddDays(30),
            IsActive = isActive,
            GioiHanSL = gioiHanSL,
            DaBan = daBan,
            Items = new List<ComboItem>
            {
                new() { ProductId = productA, SoLuong = 1 },
                new() { ProductId = productB, SoLuong = 1 },
            },
        };
        ctx.Combos.Add(combo);
        await ctx.SaveChangesAsync();
        return combo.Id;
    }

    private static async Task<CheckoutResult> DatHangComboAsync(ServiceProvider services,
        int comboId, int productA, int productB, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(new CheckoutInput
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            ComboId = comboId,
            Lines =
            {
                new CheckoutLineInput(productA, null, 1, 100_000),
                new CheckoutLineInput(productB, null, 1, 50_000),
            },
            IdempotencyKey = key,
        });
    }

    private static async Task<int> CountOrdersAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.CountAsync();
    }

    private static async Task<int> GetComboDaBanAsync(ServiceProvider services, int comboId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Combos.Where(c => c.Id == comboId).Select(c => c.DaBan).FirstAsync();
    }

    private static async Task<int> GetTonKhoAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync();
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
