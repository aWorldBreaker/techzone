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
/// Test Sprint 2 — Mang 4 (B-9): hoan tac day du khi huy don / doi tra
/// duoc duyet: thu Earn, tra Spend, hoan luot voucher, hoan luot ban,
/// hoan tien CHI khi da thu that; tat ca idempotent.
/// Luu y: bang Refunds chua co trong migration (xem Models/Refund.cs) nen
/// test doc/ghi hoan tien qua RefundService (SQL tho) — giong app that.
/// </summary>
[Collection("DbTests")]
public class RollbackTests
{
    private readonly ITestOutputHelper _out;

    public RollbackTests(ITestOutputHelper output) => _out = output;

    // ============ Huy don da thu: hoan tac day du ============

    [Fact]
    public async Task HuyDon_DaThu_HoanTacDayDu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 10);
            await SeedFlashSaleAsync(services, spId, 9_000_000, gioiHan: 5, daBan: 0);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");
            await SeedUserAsync(services, "user-1");
            await SeedEarnAsync(services, "user-1", 200);

            // Don: gia sale 9.000.000 - voucher 900.000 - diem 100.000 = 8.000.000.
            var orderId = await DatDonCoUuDaiAsync(services, "user-1", "MoMo", "RB-1", 100, "XIAOMI10",
                (spId, 1, 9_000_000));
            Assert.Equal(9, await GetTonKhoAsync(services, spId));
            Assert.Equal(1, await GetLuotBanAsync(services, spId));
            Assert.Equal(1, await GetDaBanAsync(services, spId));
            Assert.Equal(1, await GetVoucherDaDungAsync(services, "XIAOMI10"));
            Assert.Equal(180, await GetSoDuDiemAsync(services, "user-1")); // 200 - 100 + 80 Earn

            await SetDaThanhToanAsync(services, orderId, true);
            var kq = await HuyDonAsync(services, orderId);
            Assert.True(kq.ThanhCong, kq.Loi);

            Assert.Equal(10, await GetTonKhoAsync(services, spId));
            Assert.Equal(0, await GetLuotBanAsync(services, spId));
            Assert.Equal(0, await GetDaBanAsync(services, spId));
            Assert.Equal(0, await GetVoucherDaDungAsync(services, "XIAOMI10"));
            Assert.Equal(200, await GetSoDuDiemAsync(services, "user-1")); // ve dung 200 ban dau
            Assert.Equal(80, await SumLoaiAsync(services, "user-1", "EarnHoan", orderId));
            Assert.Equal(100, await SumLoaiAsync(services, "user-1", "SpendHoan", orderId));
            var hoan = await LayHoanAsync(services, orderId);
            Assert.Single(hoan);
            Assert.Equal(8_000_000m, hoan[0].SoTien);
            Assert.Equal("MoMo", hoan[0].PhuongThucTT);
            Assert.Equal("Đã hoàn", hoan[0].TrangThai);
            Assert.Null(hoan[0].ReturnRequestId);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Huy 2 lan: chi hoan tac 1 lan ============

    [Fact]
    public async Task HuyDon_HaiLan_ChiHoanTacMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-2");
            await SeedEarnAsync(services, "user-2", 200);

            var orderId = await DatDonCoUuDaiAsync(services, "user-2", "MoMo", "RB-2", 100, null,
                (spId, 1, 10_000_000));
            await SetDaThanhToanAsync(services, orderId, true);

            var lan1 = await HuyDonAsync(services, orderId);
            Assert.True(lan1.ThanhCong, lan1.Loi);
            Assert.False(lan1.DaOTrangThaiDich);
            var lan2 = await HuyDonAsync(services, orderId);
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.DaOTrangThaiDich);

            Assert.Equal(5, await GetTonKhoAsync(services, spId));
            Assert.Equal(200, await GetSoDuDiemAsync(services, "user-2"));
            Assert.Equal(1, await CountLoaiAsync(services, "user-2", "EarnHoan", orderId));
            Assert.Equal(1, await CountLoaiAsync(services, "user-2", "SpendHoan", orderId));
            Assert.Single(await LayHoanAsync(services, orderId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Huy don COD chua thu: KHONG co ban ghi hoan tien ============

    [Fact]
    public async Task HuyDon_CODChuaThu_KhongCoHoanTien_NhungVanHoanDiem()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-3");
            await SeedEarnAsync(services, "user-3", 200);

            var orderId = await DatDonCoUuDaiAsync(services, "user-3", "COD", "RB-3", 100, null,
                (spId, 1, 10_000_000));
            // DaThanhToan mac dinh false (COD chua thu) — khong set true.
            Assert.True((await HuyDonAsync(services, orderId)).ThanhCong);

            Assert.Empty(await LayHoanAsync(services, orderId)); // khong co giao dich hoan gia
            Assert.Equal(200, await GetSoDuDiemAsync(services, "user-3")); // diem van hoan tac
            Assert.Equal(5, await GetTonKhoAsync(services, spId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Doi tra duoc duyet (toan don): hoan tac day du + idempotent ============

    [Fact]
    public async Task DoiTra_Duyet_ToanDon_HoanTacDayDu_HaiLanMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-4");

            var orderId = await DatDonAsync(services, "user-4", "MoMo", "RB-4",
                (spId, 1, 10_000_000));
            Assert.Equal(100, await GetSoDuDiemAsync(services, "user-4")); // Earn 100
            await SetDaThanhToanAsync(services, orderId, true);
            await ChuyenDenDaGiaoAsync(services, orderId);

            var ycId = await TaoYeuCauAsync(services, orderId, spId, "user-4", "Lỗi màn hình");
            var kq = await DuyetAsync(services, ycId, "Đã duyệt");
            Assert.True(kq.ThanhCong, kq.Loi);
            Assert.Equal(10_000_000m, kq.SoTienHoan);

            Assert.Equal(5, await GetTonKhoAsync(services, spId));
            Assert.Equal(0, await GetLuotBanAsync(services, spId));
            Assert.Equal(0, await GetSoDuDiemAsync(services, "user-4")); // Earn 100 bi thu hoi
            var hoan = await LayHoanAsync(services, orderId);
            Assert.Single(hoan);
            Assert.Equal(10_000_000m, hoan[0].SoTien);
            Assert.Equal(ycId, hoan[0].ReturnRequestId);

            var lan2 = await DuyetAsync(services, ycId, "Đã duyệt");
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.DaXuLyTruocDo);
            Assert.Equal(0, await GetSoDuDiemAsync(services, "user-4"));
            Assert.Single(await LayHoanAsync(services, orderId));
            Assert.Equal(5, await GetTonKhoAsync(services, spId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Tu choi doi tra: khong hoan tac gi ============

    [Fact]
    public async Task DoiTra_TuChoi_KhongHoanTac()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-5");

            var orderId = await DatDonAsync(services, "user-5", "MoMo", "RB-5",
                (spId, 1, 10_000_000));
            await SetDaThanhToanAsync(services, orderId, true);
            await ChuyenDenDaGiaoAsync(services, orderId);

            var ycId = await TaoYeuCauAsync(services, orderId, spId, "user-5", "Không thích nữa");
            var kq = await DuyetAsync(services, ycId, "Từ chối");
            Assert.True(kq.ThanhCong, kq.Loi);

            Assert.Equal(4, await GetTonKhoAsync(services, spId)); // ton khong hoan
            Assert.Equal(100, await GetSoDuDiemAsync(services, "user-5")); // diem con nguyen
            Assert.Empty(await LayHoanAsync(services, orderId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Don chua giao / da huy: khong duyet doi tra duoc ============

    [Fact]
    public async Task DoiTra_DonChuaGiao_HoanDonDaHuy_KhongDuyetDuoc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 10);
            await SeedUserAsync(services, "user-6");

            // Don chua giao (ChoXacNhan): duyet doi tra phai that bai.
            var donChuaGiao = await DatDonAsync(services, "user-6", "COD", "RB-6A",
                (spId, 1, 10_000_000));
            var yc1 = await TaoYeuCauAsync(services, donChuaGiao, spId, "user-6", "Đổi màu");
            var kq1 = await DuyetAsync(services, yc1, "Đã duyệt");
            Assert.False(kq1.ThanhCong);
            Assert.Contains("đang giao", kq1.Loi);
            Assert.Equal("Chờ duyệt", await LayTrangThaiYeuCauAsync(services, yc1));
            Assert.Equal(9, await GetTonKhoAsync(services, spId)); // khong hoan tac

            // Don da huy: duyet doi tra phai that bai (don terminal, khong hoi sinh).
            var donDaHuy = await DatDonAsync(services, "user-6", "COD", "RB-6B",
                (spId, 1, 10_000_000));
            Assert.True((await HuyDonAsync(services, donDaHuy)).ThanhCong);
            var yc2 = await TaoYeuCauAsync(services, donDaHuy, spId, "user-6", "Đổi màu");
            var kq2 = await DuyetAsync(services, yc2, "Đã duyệt");
            Assert.False(kq2.ThanhCong);
            Assert.Equal("Chờ duyệt", await LayTrangThaiYeuCauAsync(services, yc2));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Doi tra 1 phan don (1 dong / 2 dong): ti le + khong hoan voucher ============

    [Fact]
    public async Task DoiTra_MotPhan_MotDong_TinhTheoTiLe_KhongHoanVoucher()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var xiaomiId = await SeedProductAsync(services, "Xiaomi 15 5G", "Xiaomi",
                12_990_000, tonKho: 25, giaKhuyenMai: 12_990_000);
            var iphoneId = await SeedProductAsync(services, "iPhone 15 Pro", "Apple",
                28_990_000, tonKho: 18, giaKhuyenMai: 28_990_000);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");
            await SeedUserAsync(services, "user-7");

            // Tam tinh 41.980.000 - voucher 1.299.000 = 40.681.000; Earn 406.
            var orderId = await DatDonCoUuDaiAsync(services, "user-7", "MoMo", "RB-7", 0, "XIAOMI10",
                (xiaomiId, 1, 12_990_000), (iphoneId, 1, 28_990_000));
            await SetDaThanhToanAsync(services, orderId, true);
            await ChuyenDenDaGiaoAsync(services, orderId);

            // Chi tra dong Xiaomi: ti le 12.990.000 / 41.980.000.
            var ycId = await TaoYeuCauAsync(services, orderId, xiaomiId, "user-7", "Đổi màu đen");
            var kq = await DuyetAsync(services, ycId, "Đã duyệt");
            Assert.True(kq.ThanhCong, kq.Loi);

            Assert.Equal(25, await GetTonKhoAsync(services, xiaomiId)); // dong tra hoan ton
            Assert.Equal(17, await GetTonKhoAsync(services, iphoneId)); // dong giu nguyen
            Assert.Equal(0, await GetLuotBanAsync(services, xiaomiId));
            Assert.Equal(1, await GetLuotBanAsync(services, iphoneId));
            Assert.Equal(1, await GetVoucherDaDungAsync(services, "XIAOMI10")); // tra 1 phan: khong hoan luot
            // Earn hoan = floor(406 * 12.990.000 / 41.980.000) = 125.
            Assert.Equal(406 - 125, await GetSoDuDiemAsync(services, "user-7"));
            Assert.Equal(125, await SumLoaiAsync(services, "user-7", "EarnHoan", orderId));
            // Hoan tien = round(40.681.000 * 12.990.000 / 41.980.000), khong hoan PhiShip (PhiShip = 0).
            var hoan = await LayHoanAsync(services, orderId);
            Assert.Single(hoan);
            Assert.Equal(Math.Round(40_681_000m * 12_990_000m / 41_980_000m), hoan[0].SoTien);
            _out.WriteLine($"hoan 1 phan = {hoan[0].SoTien:N0}d (du kien 12.588.046d)");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Tra het don (2 dong, 2 yeu cau): hoan voucher o lan duyet cuoi ============

    [Fact]
    public async Task DoiTra_TraHetDon_HoanVoucher_TongHoanDungTongTien()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var xiaomiId = await SeedProductAsync(services, "Xiaomi 15 5G", "Xiaomi",
                12_990_000, tonKho: 25, giaKhuyenMai: 12_990_000);
            var iphoneId = await SeedProductAsync(services, "iPhone 15 Pro", "Apple",
                28_990_000, tonKho: 18, giaKhuyenMai: 28_990_000);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");
            await SeedUserAsync(services, "user-8");

            var orderId = await DatDonCoUuDaiAsync(services, "user-8", "MoMo", "RB-8", 0, "XIAOMI10",
                (xiaomiId, 1, 12_990_000), (iphoneId, 1, 28_990_000));
            await SetDaThanhToanAsync(services, orderId, true);
            await ChuyenDenDaGiaoAsync(services, orderId);

            var yc1 = await TaoYeuCauAsync(services, orderId, xiaomiId, "user-8", "Đổi màu");
            Assert.True((await DuyetAsync(services, yc1, "Đã duyệt")).ThanhCong);
            Assert.Equal(1, await GetVoucherDaDungAsync(services, "XIAOMI10")); // chua tra het: chua hoan

            var yc2 = await TaoYeuCauAsync(services, orderId, iphoneId, "user-8", "Lỗi pin");
            Assert.True((await DuyetAsync(services, yc2, "Đã duyệt")).ThanhCong);
            Assert.Equal(0, await GetVoucherDaDungAsync(services, "XIAOMI10")); // tra het don: hoan luot

            var hoan = await LayHoanAsync(services, orderId);
            Assert.Equal(2, hoan.Count);
            var tongHoan = hoan.Sum(h => h.SoTien);
            Assert.True(Math.Abs(tongHoan - 40_681_000m) <= 1,
                $"Tong hoan {tongHoan:N0} phai bang TongTien 40.681.000 (+-1d lam tron)");
            // Earn hoan theo tung dong (floor): 125 + 280 = 405/406 — mat 1
            // diem do lam tron xuong tung dong, so du con 1 (ghi nhan, khong
            // phai loi: moi dong hoan dung ti le cua no).
            Assert.Equal(1, await GetSoDuDiemAsync(services, "user-8"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Earn da tieu het o don khac, huy don -> so du am (no diem) ============

    [Fact]
    public async Task HuyDon_EarnDaTieuHet_SoDuAm_TruDiemBiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp10M = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            var sp100K = await SeedProductAsync(services, "SP 100K", "Xiaomi", 100_000, tonKho: 5);
            await SeedUserAsync(services, "user-9");

            var donA = await DatDonAsync(services, "user-9", "COD", "RB-9A",
                (sp10M, 1, 10_000_000)); // Earn 100
            Assert.Equal(100, await GetSoDuDiemAsync(services, "user-9"));
            var donB = await DatDonCoUuDaiAsync(services, "user-9", "COD", "RB-9B", 100, null,
                (sp100K, 1, 100_000)); // tieu het 100 diem cua don A
            Assert.Equal(0, await GetSoDuDiemAsync(services, "user-9"));

            Assert.True((await HuyDonAsync(services, donA)).ThanhCong);
            // Thu hoi FULL 100 Earn du so du da ve 0 -> so du -100 (no diem,
            // don mua sau bu lai); SpendHoan cua don A = 0 (don A khong dung diem).
            Assert.Equal(-100, await GetSoDuDiemAsync(services, "user-9"));
            Assert.Equal(100, await SumLoaiAsync(services, "user-9", "EarnHoan", donA));
            Assert.Equal(0, await SumLoaiAsync(services, "user-9", "SpendHoan", donA));

            // So du am thi khong tieu diem duoc nua.
            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var kq = await checkout.DatHangAsync(new CheckoutInput
            {
                UserId = "user-9",
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                DiemDung = 10,
                GiamGiaDiem = 10_000,
                Lines = { new CheckoutLineInput(sp100K, null, 1, 100_000) },
                IdempotencyKey = "RB-9C",
            });
            Assert.False(kq.ThanhCong);
            Assert.Contains("điểm", kq.Loi);
            _ = donB; // don B van ton tai, khong huy trong test nay
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ DaGiao la terminal: khong huy duoc, khong hoan tac ============

    [Fact]
    public async Task HuyDon_DaGiao_KhongHuyDuoc_KhongHoanTac()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 10M", "Xiaomi", 10_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-10");

            var orderId = await DatDonAsync(services, "user-10", "COD", "RB-10",
                (spId, 1, 10_000_000));
            await ChuyenDenDaGiaoAsync(services, orderId);

            var kq = await HuyDonAsync(services, orderId);
            Assert.False(kq.ThanhCong);
            Assert.Equal(4, await GetTonKhoAsync(services, spId)); // khong hoan ton
            Assert.Equal(100, await GetSoDuDiemAsync(services, "user-10")); // khong thu Earn
            Assert.Empty(await LayHoanAsync(services, orderId));
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

    private static async Task<int> SeedCategoryAsync(ServiceProvider services)
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
        return category.Id;
    }

    private static async Task<int> SeedBrandAsync(ServiceProvider services, string ten)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var brand = await ctx.Brands.FirstOrDefaultAsync(b => b.Ten == ten);
        if (brand is null)
        {
            brand = new Brand { Ten = ten };
            ctx.Brands.Add(brand);
            await ctx.SaveChangesAsync();
        }
        return brand.Id;
    }

    private static async Task<int> SeedProductAsync(ServiceProvider services, string ten,
        string brandTen, decimal gia, int tonKho, decimal? giaKhuyenMai = null)
    {
        await EnsureDbAsync(services);
        var categoryId = await SeedCategoryAsync(services);
        var brandId = await SeedBrandAsync(services, brandTen);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Ten = ten,
            Gia = giaKhuyenMai.HasValue ? gia + 3_000_000 : gia,
            GiaKhuyenMai = giaKhuyenMai ?? gia,
            CategoryId = categoryId,
            BrandId = brandId,
            TonKho = tonKho,
            LuotBan = 0,
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    private static async Task SeedFlashSaleAsync(ServiceProvider services, int productId,
        decimal giaSale, int gioiHan, int daBan)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.FlashSales.Add(new FlashSale
        {
            ProductId = productId,
            GiaSale = giaSale,
            BatDau = DateTime.Today,
            KetThuc = DateTime.Today.AddDays(1).AddTicks(-1),
            GioiHanSL = gioiHan,
            DaBan = daBan,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedVoucherAsync(ServiceProvider services, string ma, string loaiGiam,
        decimal giaTri, decimal dieuKien, string? brandTen = null, int soLuong = 100)
    {
        await EnsureDbAsync(services);
        int? brandId = null;
        if (brandTen is not null) brandId = await SeedBrandAsync(services, brandTen);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Vouchers.Add(new Voucher
        {
            Ma = ma,
            LoaiGiam = loaiGiam,
            GiaTri = giaTri,
            DieuKien = dieuKien,
            NgayHetHan = DateTime.Now.AddDays(30),
            SoLuong = soLuong,
            DaDung = 0,
            BrandId = brandId,
        });
        await ctx.SaveChangesAsync();
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

    private static async Task SeedEarnAsync(ServiceProvider services, string userId, int diem)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PointTransactions.Add(new PointTransaction
        {
            UserId = userId,
            Diem = diem,
            Loai = "Earn",
            OrderId = null,
            NgayTao = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Dat don qua CheckoutService that. lines: (ProductId, SoLuong, DonGia).</summary>
    private static async Task<int> DatDonAsync(ServiceProvider services, string userId,
        string phuongThucTt, string key,
        params (int ProductId, int SoLuong, decimal DonGia)[] lines)
        => await DatDonCoUuDaiAsync(services, userId, phuongThucTt, key, 0, null, lines);

    private static async Task<int> DatDonCoUuDaiAsync(ServiceProvider services, string userId,
        string phuongThucTt, string key, int diemDung, string? maVoucher,
        params (int ProductId, int SoLuong, decimal DonGia)[] lines)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var input = new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            PhuongThucTT = phuongThucTt,
            DiemDung = diemDung,
            GiamGiaDiem = diemDung * 1_000m,
            MaVoucher = maVoucher,
            Lines = lines.Select(l => new CheckoutLineInput(l.ProductId, null, l.SoLuong, l.DonGia)).ToList(),
            IdempotencyKey = key,
        };
        var ketQua = await checkout.DatHangAsync(input);
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
    }

    private static async Task SetDaThanhToanAsync(ServiceProvider services, int orderId, bool value)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await ctx.Orders.FirstAsync(o => o.Id == orderId);
        order.DaThanhToan = value;
        await ctx.SaveChangesAsync();
    }

    private static async Task ChuyenDenDaGiaoAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        foreach (var tt in new[] { OrderStatus.DaXacNhan, OrderStatus.DangDongGoi, OrderStatus.DangGiao, OrderStatus.DaGiao })
        {
            var kq = await orders.DoiTrangThaiAsync(orderId, tt);
            Assert.True(kq.ThanhCong, kq.Loi);
        }
    }

    private static async Task<int> TaoYeuCauAsync(ServiceProvider services, int orderId,
        int productId, string userId, string lyDo)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var yc = new ReturnRequest
        {
            OrderId = orderId,
            ProductId = productId,
            UserId = userId,
            LyDo = lyDo,
            TrangThai = "Chờ duyệt",
            NgayTao = DateTime.Now,
        };
        ctx.ReturnRequests.Add(yc);
        await ctx.SaveChangesAsync();
        return yc.Id;
    }

    private static async Task<DuyetDoiTraResult> DuyetAsync(ServiceProvider services, int ycId, string trangThai)
    {
        using var scope = services.CreateScope();
        var doiTra = scope.ServiceProvider.GetRequiredService<ReturnService>();
        return await doiTra.DuyetDoiTraAsync(ycId, trangThai);
    }

    private static async Task<DoiTrangThaiResult> HuyDonAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        return await orders.HuyDonAsync(orderId);
    }

    private static async Task<int> GetTonKhoAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync();
    }

    private static async Task<int> GetLuotBanAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.LuotBan).FirstAsync();
    }

    private static async Task<int> GetDaBanAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.FlashSales.Where(f => f.ProductId == productId).Select(f => f.DaBan).FirstAsync();
    }

    private static async Task<int> GetVoucherDaDungAsync(ServiceProvider services, string ma)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Vouchers.Where(v => v.Ma == ma).Select(v => v.DaDung).FirstAsync();
    }

    private static async Task<int> GetSoDuDiemAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
        return await loyalty.TongDiemAsync(userId);
    }

    private static async Task<int> SumLoaiAsync(ServiceProvider services, string userId, string loai, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.PointTransactions
            .Where(p => p.UserId == userId && p.Loai == loai && p.OrderId == orderId)
            .SumAsync(p => p.Diem);
    }

    private static async Task<int> CountLoaiAsync(ServiceProvider services, string userId, string loai, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.PointTransactions
            .CountAsync(p => p.UserId == userId && p.Loai == loai && p.OrderId == orderId);
    }

    private static async Task<List<RefundRecord>> LayHoanAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var refund = scope.ServiceProvider.GetRequiredService<RefundService>();
        return await refund.LayTheoDonAsync(orderId);
    }

    private static async Task<string> LayTrangThaiYeuCauAsync(ServiceProvider services, int ycId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.ReturnRequests.Where(r => r.Id == ycId).Select(r => r.TrangThai).FirstAsync();
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
