using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test hoi quy Sprint 6 Module 3 — P1: /Products?SapXep=gia-tang|gia-giam
/// tra HTTP 500 tren SQLite ("SQLite does not support expressions of type
/// 'decimal' in ORDER BY", ProductsController.cs:91).
/// Goi TRUC TIEP ProductsController.Index tren DB SQLite tam (khong qua
/// HTTP) — Index dung chung query sort + phan trang voi request that.
/// Gia hieu dung = GiaKhuyenMai ?? Gia (Product.GiaBan).
/// </summary>
[Collection("DbTests")]
public class ProductsSortTests
{
    private readonly ITestOutputHelper _out;

    public ProductsSortTests(ITestOutputHelper output) => _out = output;

    // gia-tang: thu tu tang dan theo gia hieu dung, gom ca SP co
    // GiaKhuyenMai null (lay Gia) va SP co gia khuyen mai (lay GiaKhuyenMai).
    [Fact]
    public async Task SapXep_GiaTang_ThuTuTangDanTheoGiaHieuDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedAsync(services,
                ("A-gia-500k-khong-KM", 500_000, null),
                ("B-gia-1000k-KM-300k", 1_000_000, 300_000),
                ("C-gia-200k-khong-KM", 200_000, null),
                ("D-gia-900k-KM-800k", 900_000, 800_000));

            var ketQua = await GoiIndexAsync(services, "gia-tang", trang: 1);

            // Gia hieu dung: C 200k, B 300k, A 500k, D 800k.
            Assert.Equal(new[] { "C-gia-200k-khong-KM", "B-gia-1000k-KM-300k",
                "A-gia-500k-khong-KM", "D-gia-900k-KM-800k" },
                ketQua.Select(p => p.Ten).ToArray());
            Assert.Equal(new decimal[] { 200_000, 300_000, 500_000, 800_000 },
                ketQua.Select(p => p.GiaBan).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // gia-giam: thu tu giam dan theo gia hieu dung.
    [Fact]
    public async Task SapXep_GiaGiam_ThuTuGiamDanTheoGiaHieuDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedAsync(services,
                ("A-gia-500k-khong-KM", 500_000, null),
                ("B-gia-1000k-KM-300k", 1_000_000, 300_000),
                ("C-gia-200k-khong-KM", 200_000, null),
                ("D-gia-900k-KM-800k", 900_000, 800_000));

            var ketQua = await GoiIndexAsync(services, "gia-giam", trang: 1);

            Assert.Equal(new[] { "D-gia-900k-KM-800k", "A-gia-500k-khong-KM",
                "B-gia-1000k-KM-300k", "C-gia-200k-khong-KM" },
                ketQua.Select(p => p.Ten).ToArray());
            Assert.Equal(new decimal[] { 800_000, 500_000, 300_000, 200_000 },
                ketQua.Select(p => p.GiaBan).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Sort phai xay ra TRUOC Skip/Take: 13 SP (SoMoiTrang = 12), trang 1
    // gia-tang phai la 12 gia thap nhat theo thu tu, trang 2 chi con gia
    // cao nhat. Neu sort sau khi materialize + phan trang thi trang 2 sai.
    [Fact]
    public async Task SapXep_GiaTang_PhanTrang_SortTruocSkipTake()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var seed = Enumerable.Range(1, 13)
                .Select(i => ($"SP-{i:00}-{(1300 - (i - 1) * 100)}k", (1300 - (i - 1) * 100) * 1000m, (decimal?)null))
                .ToArray();
            // Seed nguoc gia (1300k -> 100k) de thu tu insert khac thu tu sort.
            await SeedAsync(services, seed);

            var trang1 = await GoiIndexAsync(services, "gia-tang", trang: 1);
            var trang2 = await GoiIndexAsync(services, "gia-tang", trang: 2);

            Assert.Equal(12, trang1.Count);
            Assert.Equal(Enumerable.Range(1, 12).Select(i => i * 100_000m).ToArray(),
                trang1.Select(p => p.GiaBan).ToArray());
            Assert.Single(trang2);
            Assert.Equal(1_300_000m, trang2[0].GiaBan);
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
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider services,
        params (string Ten, decimal Gia, decimal? GiaKhuyenMai)[] sanPham)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        using var scope2 = services.CreateScope();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx2.Categories.FirstOrDefaultAsync(c => c.Slug == "test-cat");
        if (category is null)
        {
            category = new Category { Ten = "Test Cat", Slug = "test-cat" };
            ctx2.Categories.Add(category);
            await ctx2.SaveChangesAsync();
        }
        var brand = await ctx2.Brands.FirstOrDefaultAsync(b => b.Ten == "Test Brand");
        if (brand is null)
        {
            brand = new Brand { Ten = "Test Brand" };
            ctx2.Brands.Add(brand);
            await ctx2.SaveChangesAsync();
        }
        foreach (var (ten, gia, km) in sanPham)
        {
            ctx2.Products.Add(new Product
            {
                Ten = ten,
                Gia = gia,
                GiaKhuyenMai = km,
                CategoryId = category.Id,
                BrandId = brand.Id,
                TonKho = 10,
                LuotBan = 0,
            });
        }
        await ctx2.SaveChangesAsync();
    }

    private static async Task<List<Product>> GoiIndexAsync(ServiceProvider services,
        string sapXep, int trang)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var ctrl = new ProductsController(
            sp.GetRequiredService<AppDbContext>(),
            new TestWebHostEnvironment(),
            sp.GetRequiredService<PriceCalculationService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var ketQua = await ctrl.Index(new ProductFilterViewModel { SapXep = sapXep, Trang = trang });
        var view = Assert.IsType<ViewResult>(ketQua);
        var filter = Assert.IsType<ProductFilterViewModel>(view.Model);
        return filter.KetQua;
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
