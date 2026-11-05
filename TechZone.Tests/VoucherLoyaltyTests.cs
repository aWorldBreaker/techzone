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
/// Test Sprint 2 — Mang 1 + Mang 5: voucher tinh dung pham vi (P1-1),
/// Spend chong race, voucher rollback khi huy don, nhat quan diem/hang.
/// </summary>
[Collection("DbTests")]
public class VoucherLoyaltyTests
{
    private readonly ITestOutputHelper _out;

    public VoucherLoyaltyTests(ITestOutputHelper output) => _out = output;

    // ============ P1-1: XIAOMI10 chi giam 10% phan Xiaomi ============

    [Fact]
    public async Task Voucher_Xiaomi10_GioHonHop_ChiGiamPhanXiaomi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");

            var lines = await LoadLinesAsync(services, (xiaomiId, 1), (iphoneId, 1));
            var tamTinh = lines.Sum(l => l.ThanhTien); // 12.990.000 + 28.990.000
            Assert.Equal(41_980_000m, tamTinh);

            var ketQua = await KiemTraAsync(services, "XIAOMI10", null, tamTinh, lines);
            Assert.True(ketQua.HopLe, ketQua.Loi);
            Assert.Equal(12_990_000m, ketQua.TamTinhDuDieuKien);
            Assert.Equal(1_299_000m, ketQua.GiamGia); // 10% cua 12.990.000, KHONG phai 4.198.000
            _out.WriteLine($"tamTinh={tamTinh:N0} duDieuKien={ketQua.TamTinhDuDieuKien:N0} giam={ketQua.GiamGia:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_Xiaomi10_ChiIPhone_TuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (_, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");

            var lines = await LoadLinesAsync(services, (iphoneId, 1));
            var ketQua = await KiemTraAsync(services, "XIAOMI10", null, lines.Sum(l => l.ThanhTien), lines);
            Assert.False(ketQua.HopLe);
            Assert.Contains("Xiaomi", ketQua.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_HetHan_TuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "HETHAN", "PhanTram", 10, 0,
                brandTen: "Xiaomi", hetHan: DateTime.Now.AddDays(-1));

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var ketQua = await KiemTraAsync(services, "HETHAN", null, lines.Sum(l => l.ThanhTien), lines);
            Assert.False(ketQua.HopLe);
            Assert.Contains("hết hạn", ketQua.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_Vang5_KhachThuong_TuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "VÀNG5", "PhanTram", 5, 0, minTier: "Vàng");
            await SeedUserAsync(services, "user-thuong");

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var ketQua = await KiemTraAsync(services, "VÀNG5", "user-thuong",
                lines.Sum(l => l.ThanhTien), lines);
            Assert.False(ketQua.HopLe);
            Assert.Contains("Vàng", ketQua.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_Vang5_KhachVang_GiamDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "VÀNG5", "PhanTram", 5, 0, minTier: "Vàng");
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m); // > 20tr -> Vang

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var ketQua = await KiemTraAsync(services, "VÀNG5", "user-vang",
                lines.Sum(l => l.ThanhTien), lines);
            Assert.True(ketQua.HopLe, ketQua.Loi);
            Assert.Equal(649_500m, ketQua.GiamGia); // 5% cua 12.990.000
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_MotLuot_DaDungHet_TuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "MOTLUOT", "Tien", 100_000, 0,
                soLuong: 1, daDung: 1);

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var ketQua = await KiemTraAsync(services, "MOTLUOT", null, lines.Sum(l => l.ThanhTien), lines);
            Assert.False(ketQua.HopLe);
            Assert.Contains("hết lượt", ketQua.Loi);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Voucher_TienCoDinh_CapOPhanDuDieuKien()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            // SP Xiaomi re 300.000 + iPhone: voucher Tien 500.000 chi ap Xiaomi
            // -> giam toi da 300.000 (phan du dieu kien), khong phai 500.000.
            var xiaomiRe = await SeedProductAsync(services, "Xiaomi Re", "Xiaomi", 300_000, tonKho: 5);
            var (_, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI500", "Tien", 500_000, 0, brandTen: "Xiaomi");

            var lines = await LoadLinesAsync(services, (xiaomiRe, 1), (iphoneId, 1));
            var ketQua = await KiemTraAsync(services, "XIAOMI500", null, lines.Sum(l => l.ThanhTien), lines);
            Assert.True(ketQua.HopLe, ketQua.Loi);
            Assert.Equal(300_000m, ketQua.TamTinhDuDieuKien);
            Assert.Equal(300_000m, ketQua.GiamGia);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Checkout end-to-end: GiamGia sai gui len bi sua ve dung ============

    [Fact]
    public async Task Checkout_Xiaomi10_GioHonHop_GiamDungPhanXiaomi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                MaVoucher = "XIAOMI10",
                GiamGia = 4_198_000, // gia tri SAI theo kieu cu (10% toan bo gio)
                Lines =
                {
                    new CheckoutLineInput(xiaomiId, null, 1, 12_990_000),
                    new CheckoutLineInput(iphoneId, null, 1, 28_990_000),
                },
                IdempotencyKey = "S2-VOUCHER-1",
            });

            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            Assert.Equal(1_299_000m, ketQua.DonHang!.GiamGia);
            Assert.Equal(40_681_000m, ketQua.DonHang.TongTien); // 41.980.000 - 1.299.000
            Assert.Equal(1, await GetVoucherDaDungAsync(services, "XIAOMI10"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Race Spend: 2 checkout cung tranh 100 diem ============

    [Fact]
    public async Task Spend_HaiCheckoutDongThoi_MotThanhCong_DiemKhongAm()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Diem", "Test Brand", 100_000, tonKho: 10);
            await SeedUserAsync(services, "user-diem");
            await SeedEarnAsync(services, "user-diem", 100);

            var t1 = CheckoutDiemRiengAsync(services, productId, "user-diem", "RACE-DIEM-1");
            var t2 = CheckoutDiemRiengAsync(services, productId, "user-diem", "RACE-DIEM-2");
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/{results[0].Loi} kq2={results[1].ThanhCong}/{results[1].Loi}");

            Assert.Single(results, r => r.ThanhCong);
            Assert.Single(results, r => !r.ThanhCong);
            Assert.Contains(results, r => !r.ThanhCong && r.Loi != null && r.Loi.Contains("điểm"));
            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(1, await CountSpendAsync(services, "user-diem"));
            Assert.Equal(0, await GetSoDuDiemAsync(services, "user-diem")); // khong am
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Spend_TruDiem_HaiLanCungDon_ChiMotDong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Diem 2", "Test Brand", 100_000, tonKho: 5);
            await SeedUserAsync(services, "user-diem-2");
            await SeedEarnAsync(services, "user-diem-2", 50);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "S2-SPEND-1");

            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                var lan1 = await loyalty.TruDiemAsync("user-diem-2", 30, orderId);
                Assert.True(lan1.ThanhCong, lan1.Loi);
                var lan2 = await loyalty.TruDiemAsync("user-diem-2", 30, orderId);
                Assert.True(lan2.ThanhCong, lan2.Loi); // idempotent: da tru roi
            }
            Assert.Equal(1, await CountSpendAsync(services, "user-diem-2"));
            Assert.Equal(20, await GetSoDuDiemAsync(services, "user-diem-2")); // 50 - 30, khong tru 2 lan
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Voucher rollback khi huy don: huy 2 lan chi hoan 1 luot ============

    [Fact]
    public async Task Voucher_HoanLuot_HuyDon_HaiLan_ChiHoanMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");

            var orderId = await DatDonVoucherAsync(services, xiaomiId, iphoneId, "S2-ROLLBACK-1");
            Assert.Equal(1, await GetVoucherDaDungAsync(services, "XIAOMI10"));
            Assert.Equal(24, await GetTonKhoAsync(services, xiaomiId));

            var lan1 = await HuyDonAsync(services, orderId);
            Assert.True(lan1.ThanhCong, lan1.Loi);
            Assert.False(lan1.DaOTrangThaiDich);
            Assert.Equal(0, await GetVoucherDaDungAsync(services, "XIAOMI10"));
            Assert.Equal(25, await GetTonKhoAsync(services, xiaomiId));

            var lan2 = await HuyDonAsync(services, orderId);
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.DaOTrangThaiDich, "Huy lan 2 phai bao da huy truoc do.");
            Assert.Equal(0, await GetVoucherDaDungAsync(services, "XIAOMI10")); // khong hoan am / 2 lan
            Assert.Equal(25, await GetTonKhoAsync(services, xiaomiId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Nhat quan diem/hang khach demo 337 diem / Gold sau huy ============

    [Fact]
    public async Task Loyalty_KhachDemo337Diem_HuyDon_HangTinhLaiDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "user-demo");
            // 3 don mau nhu seed: 13.680.000 + 18.490.000 + 1.780.000 = 33.950.000
            // Earn: 136 + 184 + 17 = 337 diem, hang Vang (> 20tr).
            var don1 = await SeedOrderAsync(services, "user-demo", 13_680_000m);
            var don2 = await SeedOrderAsync(services, "user-demo", 18_490_000m);
            var don3 = await SeedOrderAsync(services, "user-demo", 1_780_000m);
            await SeedEarnAsync(services, "user-demo", 136, don1);
            await SeedEarnAsync(services, "user-demo", 184, don2);
            await SeedEarnAsync(services, "user-demo", 17, don3);

            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                Assert.Equal(337, await loyalty.TongDiemAsync("user-demo"));
                Assert.Equal("Vàng", await loyalty.HangAsync("user-demo"));
                Assert.Equal(33_950_000m, await loyalty.TongChiTieuAsync("user-demo"));
            }

            // Huy don #3 (1.780.000): chi tieu con 32.170.000 -> van Vang.
            Assert.True((await HuyDonAsync(services, don3)).ThanhCong);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                Assert.Equal(32_170_000m, await loyalty.TongChiTieuAsync("user-demo"));
                Assert.Equal("Vàng", await loyalty.HangAsync("user-demo"));
            }

            // Huy don #2 (18.490.000): chi tieu con 13.680.000 -> xuong Bac.
            Assert.True((await HuyDonAsync(services, don2)).ThanhCong);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                Assert.Equal(13_680_000m, await loyalty.TongChiTieuAsync("user-demo"));
                Assert.Equal("Bạc", await loyalty.HangAsync("user-demo"));
            }
            // Luu y: diem Earn cua don da huy van con 337 cho den khi Mang 4
            // them thu hoi Earn / tra Spend vao nhanh huy don (OrderService).
            _out.WriteLine("Hang tinh lai dung sau huy (Vang -> Bac); thu hoi Earn la phan Mang 4.");
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
        services.AddScoped<PriceCalculationService>(); // Sprint 2 merge: CheckoutService can
        services.AddScoped<VatService>(); // Sprint 2 merge: CheckoutService can VatService
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>(); // Sprint 2 — Mang 4: OrderService can RefundService
        services.AddScoped<ReturnService>(); // Sprint 2 — Mang 4: duyet doi tra
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

    /// <summary>Xiaomi 15 (GiaBan 12.990.000) + iPhone 15 Pro (GiaBan 28.990.000) nhu seed that.</summary>
    private static async Task<(int XiaomiId, int IphoneId)> SeedXiaomiIphoneAsync(ServiceProvider services)
    {
        var xiaomiId = await SeedProductAsync(services, "Xiaomi 15 5G", "Xiaomi",
            12_990_000, tonKho: 25, giaKhuyenMai: 12_990_000);
        var iphoneId = await SeedProductAsync(services, "iPhone 15 Pro", "Apple",
            28_990_000, tonKho: 18, giaKhuyenMai: 28_990_000);
        return (xiaomiId, iphoneId);
    }

    private static async Task SeedVoucherAsync(ServiceProvider services, string ma, string loaiGiam,
        decimal giaTri, decimal dieuKien, string? brandTen = null, string? minTier = null,
        int soLuong = 100, int daDung = 0, DateTime? hetHan = null)
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
            NgayHetHan = hetHan ?? DateTime.Now.AddDays(30),
            SoLuong = soLuong,
            DaDung = daDung,
            BrandId = brandId,
            MinTier = minTier,
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

    private static async Task SeedEarnAsync(ServiceProvider services, string userId, int diem, int? orderId = null)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PointTransactions.Add(new PointTransaction
        {
            UserId = userId,
            Diem = diem,
            Loai = "Earn",
            OrderId = orderId,
            NgayTao = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> SeedOrderAsync(ServiceProvider services, string userId, decimal tongTien)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order
        {
            UserId = userId,
            TrangThai = OrderStatus.ChoXacNhan,
            DiaChiGiao = "Test",
            PhuongThucTT = "COD",
            NgayDat = DateTime.Now,
            TongTien = tongTien,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<List<CartLineViewModel>> LoadLinesAsync(ServiceProvider services,
        params (int ProductId, int SoLuong)[] items)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = new List<CartLineViewModel>();
        foreach (var (productId, soLuong) in items)
        {
            var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
            // DonGia phai gan tay: tu Sprint 2 (B-5) DonGia khong con tu tinh
            // tu GiaBan nua; de 0 thi voucher tinh phan du dieu kien = 0.
            lines.Add(new CartLineViewModel { Product = product, DonGia = product.GiaBan, SoLuong = soLuong });
        }
        return lines;
    }

    private static async Task<VoucherResult> KiemTraAsync(ServiceProvider services, string ma,
        string? userId, decimal tamTinh, List<CartLineViewModel> lines)
    {
        using var scope = services.CreateScope();
        var voucher = scope.ServiceProvider.GetRequiredService<VoucherService>();
        return await voucher.KiemTraAsync(ma, userId, tamTinh, lines);
    }

    private static async Task<CheckoutResult> CheckoutDiemRiengAsync(ServiceProvider services,
        int productId, string userId, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            DiemDung = 100,
            GiamGiaDiem = 100_000,
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = key,
        });
    }

    private static async Task<int> DatDonThanhCongAsync(ServiceProvider services, int productId, int soLuong, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var ketQua = await checkout.DatHangAsync(new CheckoutInput
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, soLuong, 100_000) },
            IdempotencyKey = key,
        });
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
    }

    private static async Task<int> DatDonVoucherAsync(ServiceProvider services,
        int xiaomiId, int iphoneId, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var ketQua = await checkout.DatHangAsync(new CheckoutInput
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = "XIAOMI10",
            GiamGia = 1_299_000,
            Lines =
            {
                new CheckoutLineInput(xiaomiId, null, 1, 12_990_000),
                new CheckoutLineInput(iphoneId, null, 1, 28_990_000),
            },
            IdempotencyKey = key,
        });
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
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

    private static async Task<int> CountSpendAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.PointTransactions.CountAsync(p => p.UserId == userId && p.Loai == "Spend");
    }

    private static async Task<int> GetSoDuDiemAsync(ServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
        return await loyalty.TongDiemAsync(userId);
    }

    private static async Task<int> GetVoucherDaDungAsync(ServiceProvider services, string ma)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Vouchers.Where(v => v.Ma == ma).Select(v => v.DaDung).FirstAsync();
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
