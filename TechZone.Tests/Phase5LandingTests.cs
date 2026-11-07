using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test Phase 5 — Landing (Sprint 6 Continuation, PHASE CUỐI).
/// Theo mẫu FlashSaleTests: controller thật + SQLite tạm (WAL), không Moq.
/// Kiểm chứng: hero đọc từ DB (đổi giá seed → ViewBag.Hero đổi theo),
/// voucher band đọc từ bảng Vouchers (hết hạn / hết lượt → ẩn),
/// DB trống không lỗi (Hero/Voucher null, các list rỗng).
/// Tồn kho trên card là logic trong Razor (_ProductCard) nên không test
/// bằng unit test — kiểm chứng bằng smoke curl HTML app copy (xem báo cáo).
/// </summary>
[Collection("DbTests")]
public class Phase5LandingTests
{
    private readonly ITestOutputHelper _out;

    public Phase5LandingTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Hero_DoiGiaSeed_ThiViewBagHeroDoiTheo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var heroId = await SeedHeroAsync(services,
                gia: 15_990_000, giaKm: 12_990_000, tonKho: 24);
            Assert.Equal(1, heroId); // hero lay cung Id 1 nhu controller

            decimal giaBanLan1;
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoHomeController(services, scope);
                Assert.IsType<ViewResult>(await ctrl.Index());
                var hero = Assert.IsType<Product>(ctrl.ViewBag.Hero);
                Assert.Equal("Xiaomi 15 5G", hero.Ten);
                Assert.Equal(15_990_000m, hero.Gia);
                Assert.Equal(12_990_000m, hero.GiaBan);
                Assert.Equal("Snapdragon 8 Elite", hero.Specs!.Chip);
                giaBanLan1 = hero.GiaBan;
            }

            // Doi gia seed tren DB -> hero phai doi theo (khong ghi tay).
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sp = await ctx.Products.FirstAsync(p => p.Id == 1);
                sp.GiaKhuyenMai = 11_990_000;
                await ctx.SaveChangesAsync();
            }
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoHomeController(services, scope);
                await ctrl.Index();
                var hero = Assert.IsType<Product>(ctrl.ViewBag.Hero);
                Assert.Equal(11_990_000m, hero.GiaBan);
                Assert.NotEqual(giaBanLan1, hero.GiaBan);
            }
            _out.WriteLine("Hero dong: 12.990.000 -> 11.990.000 theo DB.");
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task VoucherBand_HienVoucherConHieuLuc_HetHanThiAn()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedHeroAsync(services, gia: 15_990_000, giaKm: 12_990_000, tonKho: 24);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                ctx.Vouchers.Add(new Voucher
                {
                    Ma = "TECHZONE500", LoaiGiam = "Tien", GiaTri = 500_000,
                    DieuKien = 5_000_000, NgayHetHan = DateTime.Now.AddDays(30),
                    SoLuong = 100, DaDung = 1,
                });
                ctx.Vouchers.Add(new Voucher
                {
                    Ma = "WELCOME100", LoaiGiam = "Tien", GiaTri = 100_000,
                    DieuKien = 1_000_000, NgayHetHan = DateTime.Now.AddDays(30),
                    SoLuong = 200, DaDung = 0,
                });
                await ctx.SaveChangesAsync();
            }
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoHomeController(services, scope);
                await ctrl.Index();
                // Muc giam lon nhat thang: TECHZONE500 (500k > 100k).
                var vc = Assert.IsType<Voucher>(ctrl.ViewBag.VoucherNoiBat);
                Assert.Equal("TECHZONE500", vc.Ma);
                Assert.Equal(500_000m, vc.GiaTri);
            }

            // Het han ca 2 voucher -> band an (VoucherNoiBat null).
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var v in await ctx.Vouchers.ToListAsync())
                    v.NgayHetHan = DateTime.Now.AddDays(-1);
                await ctx.SaveChangesAsync();
            }
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoHomeController(services, scope);
                await ctrl.Index();
                Assert.Null(ctrl.ViewBag.VoucherNoiBat);
            }
            _out.WriteLine("Voucher het han -> band an.");
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task VoucherBand_HetLuotThiAn()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedHeroAsync(services, gia: 15_990_000, giaKm: 12_990_000, tonKho: 24);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                ctx.Vouchers.Add(new Voucher
                {
                    Ma = "HETLUOT", LoaiGiam = "Tien", GiaTri = 50_000,
                    DieuKien = 0, NgayHetHan = DateTime.Now.AddDays(30),
                    SoLuong = 5, DaDung = 5, // DaDung == SoLuong
                });
                await ctx.SaveChangesAsync();
            }
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoHomeController(services, scope);
                await ctrl.Index();
                Assert.Null(ctrl.ViewBag.VoucherNoiBat);
            }
            _out.WriteLine("Voucher het luot -> band an.");
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Home_DbTrong_KhongLoi_HeroVaVoucherNull()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
            }
            using var scope2 = services.CreateScope();
            var ctrl = TaoHomeController(services, scope2);
            Assert.IsType<ViewResult>(await ctrl.Index());
            Assert.Null(ctrl.ViewBag.Hero);
            Assert.Null(ctrl.ViewBag.VoucherNoiBat);
            Assert.Empty((List<FlashSale>)ctrl.ViewBag.FlashSale);
            Assert.Empty((List<Product>)ctrl.ViewBag.NoiBat);
            Assert.Empty((List<Product>)ctrl.ViewBag.BanChay);
            Assert.Empty((List<Category>)ctrl.ViewBag.DanhMuc);
            _out.WriteLine("DB trong: Index khong loi, empty states co du lieu rong de render.");
        }
        finally { DeleteDb(db); }
    }

    // ================= Helpers (mẫu FlashSaleTests) =================

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddDistributedMemoryCache();
        services.AddSession();
        // ViewBag doc qua TempData factory — can dang ky MVC (mau AuthTests).
        services.AddControllersWithViews();
        return services.BuildServiceProvider();
    }

    /// <summary>Seed SP Id 1 (Xiaomi 15) kem Specs — tra ve Id (== 1 tren DB moi).</summary>
    private static async Task<int> SeedHeroAsync(ServiceProvider services,
        decimal gia, decimal? giaKm, int tonKho)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        using var scope2 = services.CreateScope();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Ten = "Điện thoại", Slug = "dien-thoai" };
        var brand = new Brand { Ten = "Xiaomi" };
        ctx2.Categories.Add(category);
        ctx2.Brands.Add(brand);
        await ctx2.SaveChangesAsync();
        var sp = new Product
        {
            Ten = "Xiaomi 15 5G", Gia = gia, GiaKhuyenMai = giaKm,
            CategoryId = category.Id, BrandId = brand.Id,
            TonKho = tonKho, LuotBan = 128, LaNoiBat = true,
            MoTa = "Flagship Xiaomi: chip Snapdragon 8 Elite, camera Leica 50MP.",
        };
        ctx2.Products.Add(sp);
        await ctx2.SaveChangesAsync();
        ctx2.ProductSpecs.Add(new ProductSpecs
        {
            ProductId = sp.Id, Chip = "Snapdragon 8 Elite", Ram = "12GB",
            Rom = "256GB", Pin = "5400mAh", ManHinh = "6.7\" AMOLED 120Hz",
            Camera = "50MP",
        });
        await ctx2.SaveChangesAsync();
        return sp.Id;
    }

    private static HomeController TaoHomeController(ServiceProvider services, IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctrl = new HomeController(db);
        var http = new DefaultHttpContext { RequestServices = services };
        http.Session = new FakeSession();
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public string Id => "test-session";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
