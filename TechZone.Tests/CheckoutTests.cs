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
/// Test Sprint 1 — B: checkout transaction + race/idempotency + bang chuyen trang thai.
/// Goi truc tiep CheckoutService/OrderService voi 2 DbContext/scope rieng tren
/// cung file DB tam (WAL mode) de mo phong 2 request dong thoi.
/// Muc tieu: ton kho khong bao gio am, voucher khong vuot luot, 1 key = 1 don,
/// chuyen trang thai sai bi tu choi, huy 2 lan chi hoan ton 1 lan.
/// </summary>
[Collection("DbTests")]
public class CheckoutTests
{
    private readonly ITestOutputHelper _out;

    public CheckoutTests(ITestOutputHelper output) => _out = output;

    // ============ Concurrency: 2 checkout tranh 1 don vi cuoi cung ============

    [Fact]
    public async Task Checkout_HaiDonDongThoi_MotThanhCong_TonKhongAm()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 1, voucherSoLuong: 0);

            var t1 = CheckoutRiengAsync(services, productId, 1, key: "RACE-1");
            var t2 = CheckoutRiengAsync(services, productId, 1, key: "RACE-2");
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/{results[0].Loi} kq2={results[1].ThanhCong}/{results[1].Loi}");

            Assert.Single(results, r => r.ThanhCong);
            Assert.Single(results, r => !r.ThanhCong);
            Assert.Contains(results, r => !r.ThanhCong && r.Loi != null && r.Loi.Contains("hết hàng"));
            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(0, await GetTonKhoAsync(services, productId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Concurrency: voucher 1 luot bi 2 nguoi dung dong thoi ============

    [Fact]
    public async Task Checkout_VoucherMotLuot_HaiNguoiDongThoi_MotThanhCong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 10, voucherSoLuong: 1);
            const string ma = "RACE-VOUCHER";

            var t1 = CheckoutRiengAsync(services, productId, 1, key: "V-1", maVoucher: ma, giamGia: 10_000);
            var t2 = CheckoutRiengAsync(services, productId, 1, key: "V-2", maVoucher: ma, giamGia: 10_000);
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

    // ============ Idempotency: cung key POST 2 lan -> 1 don ============

    [Fact]
    public async Task Checkout_CungIdempotencyKey_HaiLan_MotDon()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);

            var lan1 = await CheckoutRiengAsync(services, productId, 2, key: "IDEM-1");
            Assert.True(lan1.ThanhCong, lan1.Loi);
            var lan2 = await CheckoutRiengAsync(services, productId, 2, key: "IDEM-1");
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.LaDonCu, "Lan 2 cung key phai tra ve don cu.");
            Assert.Equal(lan1.DonHang!.Id, lan2.DonHang!.Id);

            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(3, await GetTonKhoAsync(services, productId)); // chi tru 1 lan
            Assert.Equal(1, await CountRowsAsync(services, "OrderStatusHistories"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Gia thay doi sau khi vao checkout -> tu choi ============

    [Fact]
    public async Task Checkout_GiaThayDoi_TuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);

            var input = BaseInput(productId, 1, key: "PRICE-1");
            input.Lines[0] = input.Lines[0] with { DonGia = 1 }; // gia cu, sai voi DB
            var ketQua = await DatHangAsync(services, input);

            Assert.False(ketQua.ThanhCong);
            Assert.Contains("đã thay đổi", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(5, await GetTonKhoAsync(services, productId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Atomicity: het hang giua chung -> khong don, khong tru gi ============

    [Fact]
    public async Task Checkout_HetHangGiuaChung_KhongTaoDon_KhongTruVoucher()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productA = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 5);
            var productB = await SeedProductAsync(services, "SP B", 50_000, tonKho: 2);
            const string ma = "ATOMIC-V";

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                MaVoucher = ma,
                GiamGia = 10_000,
                Lines =
                {
                    new CheckoutLineInput(productA, null, 1, 100_000),
                    new CheckoutLineInput(productB, null, 99, 50_000), // vuot ton
                },
                IdempotencyKey = "ATOMIC-1",
            });

            Assert.False(ketQua.ThanhCong);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(5, await GetTonKhoAsync(services, productA));
            Assert.Equal(2, await GetTonKhoAsync(services, productB));
            Assert.Equal(0, await GetVoucherDaDungAsync(services, ma));
            Assert.Equal(0, await CountRowsAsync(services, "PointTransactions"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Earn diem nam trong transaction + guard chong ghi trung ============

    [Fact]
    public async Task Checkout_TichDiem_EarnGhiMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0); // migrate + WAL
            var productId = await SeedProductAsync(services, "SP Earn", 1_000_000, tonKho: 5);
            await SeedUserAsync(services, "user-earn-1");

            var ketQua = await CheckoutRiengAsync(services, productId, 1,
                key: "EARN-1", userId: "user-earn-1", donGia: 1_000_000);
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            // TongTien = 1.000.000 -> Earn = 10 diem.
            Assert.Equal(10, await SumPointsAsync(services, "user-earn-1", "Earn"));
            Assert.Equal(1, await CountRowsAsync(services, "PointTransactions"));

            // Goi lai GhiDiemMuaHangAsync cho cung don: guard theo OrderId chan trung.
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                var order = await ctx.Orders.FirstAsync(o => o.Id == ketQua.DonHang!.Id);
                await loyalty.GhiDiemMuaHangAsync(order);
                await loyalty.GhiDiemMuaHangAsync(order);
            }
            Assert.Equal(10, await SumPointsAsync(services, "user-earn-1", "Earn"));
            Assert.Equal(1, await CountRowsAsync(services, "PointTransactions"));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Transition: DaHuy -> DaXacNhan bi tu choi ============

    [Fact]
    public async Task Transition_DaHuy_SangDaXacNhan_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "T-1");

            var huy = await DoiTrangThaiAsync(services, orderId, OrderStatus.DaHuy);
            Assert.True(huy.ThanhCong, huy.Loi);

            var hoiSinh = await DoiTrangThaiAsync(services, orderId, OrderStatus.DaXacNhan);
            Assert.False(hoiSinh.ThanhCong);
            Assert.Contains("Không thể chuyển", hoiSinh.Loi);
            Assert.Equal(OrderStatus.DaHuy, await GetTrangThaiAsync(services, orderId));
            Assert.Equal(5, await GetTonKhoAsync(services, productId)); // da hoan ton
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Transition: chuoi hop le day du, DaGiao -> DaHuy bi tu choi ============

    [Fact]
    public async Task Transition_DaGiao_SangDaHuy_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "T-2");

            foreach (var tt in new[] { OrderStatus.DaXacNhan, OrderStatus.DangDongGoi,
                OrderStatus.DangGiao, OrderStatus.DaGiao })
            {
                var kq = await DoiTrangThaiAsync(services, orderId, tt);
                Assert.True(kq.ThanhCong, $"Chuyen sang {tt} that bai: {kq.Loi}");
                Assert.Equal(tt, await GetTrangThaiAsync(services, orderId));
            }

            var huy = await DoiTrangThaiAsync(services, orderId, OrderStatus.DaHuy);
            Assert.False(huy.ThanhCong);
            Assert.Equal(OrderStatus.DaGiao, await GetTrangThaiAsync(services, orderId));
            Assert.Equal(4, await GetTonKhoAsync(services, productId)); // khong hoan ton
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Huy 2 lan chi hoan ton 1 lan ============

    [Fact]
    public async Task HuyDon_HaiLan_ChiHoanTonMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedMinimalAsync(services, tonKho: 5, voucherSoLuong: 0);
            var orderId = await DatDonThanhCongAsync(services, productId, 2, "T-3");
            Assert.Equal(3, await GetTonKhoAsync(services, productId));

            var lan1 = await HuyDonAsync(services, orderId);
            Assert.True(lan1.ThanhCong, lan1.Loi);
            Assert.False(lan1.DaOTrangThaiDich);
            Assert.Equal(5, await GetTonKhoAsync(services, productId));

            var lan2 = await HuyDonAsync(services, orderId);
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.DaOTrangThaiDich, "Huy lan 2 phai bao da huy truoc do.");
            Assert.Equal(5, await GetTonKhoAsync(services, productId)); // khong hoan lan 2
            Assert.Equal(OrderStatus.DaHuy, await GetTrangThaiAsync(services, orderId));
            Assert.Equal(1, await CountHistoryAsync(services, orderId, OrderStatus.DaHuy));
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
        services.AddScoped<PriceCalculationService>(); // Sprint 2 — B-5: CheckoutService can
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>(); // Sprint 2 — Mang 4: OrderService can RefundService
        services.AddScoped<ReturnService>(); // Sprint 2 — Mang 4: duyet doi tra
        // Sprint 2 — B-10: CheckoutService can VatService (cau hinh mac dinh:
        // khong rule -> 10%; test VAT dung BuildServices rieng o VatTests).
        services.Configure<VatOptions>(o => { o.GiaDaGomVat = true; o.ThueSuatMacDinh = 0.10m; });
        services.AddScoped<VatService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Seed toi thieu: 1 danh muc + 1 hang + 1 SP (gia 100.000) + 1 voucher.</summary>
    private static async Task<int> SeedMinimalAsync(ServiceProvider services, int tonKho, int voucherSoLuong)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            // WAL mode: cho phep 2 connection doc/ghi dong thoi khi test race.
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        var productId = await SeedProductAsync(services, "SP Test", 100_000, tonKho);
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
            ctx.Vouchers.Add(new Voucher
            {
                Ma = "ATOMIC-V",
                LoaiGiam = "Tien",
                GiaTri = 10_000,
                DieuKien = 0,
                NgayHetHan = DateTime.Now.AddDays(30),
                SoLuong = 5,
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

    private static CheckoutInput BaseInput(int productId, int soLuong, string key,
        string? maVoucher = null, decimal giamGia = 0, string? userId = null, decimal donGia = 100_000) => new()
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = maVoucher,
            GiamGia = giamGia,
            Lines = { new CheckoutLineInput(productId, null, soLuong, donGia) },
            IdempotencyKey = key,
        };

    /// <summary>Checkout tren 1 scope rieng (mo phong 1 request HTTP rieng).</summary>
    private static async Task<CheckoutResult> CheckoutRiengAsync(ServiceProvider services,
        int productId, int soLuong, string key, string? maVoucher = null,
        decimal giamGia = 0, string? userId = null, decimal donGia = 100_000)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(
            BaseInput(productId, soLuong, key, maVoucher, giamGia, userId, donGia));
    }

    private static async Task<CheckoutResult> DatHangAsync(ServiceProvider services, CheckoutInput input)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(input);
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

    private static async Task<int> CountRowsAsync(ServiceProvider services, string table)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return table switch
        {
            "OrderStatusHistories" => await ctx.OrderStatusHistories.CountAsync(),
            "PointTransactions" => await ctx.PointTransactions.CountAsync(),
            _ => throw new ArgumentException("Bang chua ho tro: " + table),
        };
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

    private static async Task<int> SumPointsAsync(ServiceProvider services, string userId, string loai)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.PointTransactions
            .Where(p => p.UserId == userId && p.Loai == loai)
            .SumAsync(p => p.Diem);
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
