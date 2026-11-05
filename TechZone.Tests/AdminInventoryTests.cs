using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
/// Test Sprint 3 — Admin + inventory (worker 4).
/// - AdminValidation: voucher am / % &gt; 100 / ngay het han qua khu / SoLuong
///   &lt;= 0; slot-item gia sale &lt;= 0 hoac &gt;= gia ban; combo ket thuc qua khu.
///   ThemVoucher/ThemSlotItem/ThemCombo qua controller: loi hien TempData,
///   khong luu; voucher hop le duoc luu va DaDung bi ep ve 0.
/// - Variant stock (quyet dinh b): TonKho bien the chi la thong tin hien thi
///   — mua hang co VariantId chi tru Product.TonKho, TonKho bien the khong doi.
/// - Seed: TonKho = ton sau khi tru don mau (Xiaomi 24, Buds 49, TUF 11,
///   WH-CH520 38); tong TonKho bien the = TonKho SP.
/// - FlashSale.DaBan: tang dung 1 lan khi tao don, hoan khi huy don.
/// - Admin doi trang thai qua OrderService: DaGiao -&gt; DaHuy va
///   DaHuy -&gt; DaXacNhan bi chan ngay o controller (TempData Error).
/// - ReturnsController.Create: don chua giao bi chan ngay luc tao yeu cau
///   (GET + POST); don DaGiao tao duoc.
/// DB test: file tam + MigrateAsync; KHONG dung techzone.db that.
/// </summary>
[Collection("DbTests")]
public class AdminInventoryTests
{
    private readonly ITestOutputHelper _out;

    public AdminInventoryTests(ITestOutputHelper output) => _out = output;

    // ============ 1. Voucher validation (pure) ============

    [Fact]
    public void Voucher_GiaTriAm_BiTuChoi()
    {
        var loi = AdminValidation.KiemTraVoucher(VoucherMau(giaTri: -100));
        Assert.NotNull(loi);
        Assert.Contains("lớn hơn 0", loi);
    }

    [Fact]
    public void Voucher_PhanTramVuot100_BiTuChoi()
    {
        Assert.NotNull(AdminValidation.KiemTraVoucher(VoucherMau("PhanTram", 150)));
        Assert.NotNull(AdminValidation.KiemTraVoucher(VoucherMau("PhanTram", 0)));
        Assert.Null(AdminValidation.KiemTraVoucher(VoucherMau("PhanTram", 100)));
    }

    [Fact]
    public void Voucher_NgayHetHanQuaKhu_BiTuChoi()
    {
        var v = VoucherMau();
        v.NgayHetHan = DateTime.Today.AddDays(-1);
        Assert.NotNull(AdminValidation.KiemTraVoucher(v));
        v.NgayHetHan = DateTime.Today;
        Assert.Null(AdminValidation.KiemTraVoucher(v));
    }

    [Fact]
    public void Voucher_DieuKienAm_SoLuongKhongDuong_BiTuChoi()
    {
        var v1 = VoucherMau();
        v1.DieuKien = -1;
        Assert.NotNull(AdminValidation.KiemTraVoucher(v1));
        var v2 = VoucherMau();
        v2.SoLuong = 0;
        Assert.NotNull(AdminValidation.KiemTraVoucher(v2));
        var v3 = VoucherMau();
        v3.SoLuong = -5;
        Assert.NotNull(AdminValidation.KiemTraVoucher(v3));
    }

    [Fact]
    public void Voucher_HopLe_VaFreeShip0Dong_DuocChapNhan()
    {
        Assert.Null(AdminValidation.KiemTraVoucher(VoucherMau()));
        var freeShip = VoucherMau(giaTri: 0);
        freeShip.FreeShip = true;
        Assert.Null(AdminValidation.KiemTraVoucher(freeShip));
        // Tien 0 ma khong phai freeship thi vo nghia.
        Assert.NotNull(AdminValidation.KiemTraVoucher(VoucherMau(giaTri: 0)));
    }

    private static Voucher VoucherMau(string loai = "Tien", decimal giaTri = 100_000) => new()
    {
        Ma = "TEST",
        LoaiGiam = loai,
        GiaTri = giaTri,
        DieuKien = 0,
        NgayHetHan = DateTime.Today.AddDays(30),
        SoLuong = 10,
        DaDung = 0,
    };

    // ============ 2. Slot-item validation (pure) ============

    [Fact]
    public void SlotItem_GiaSaleKhongReHonGiaBan_BiTuChoi()
    {
        Assert.NotNull(AdminValidation.KiemTraSlotItem(0, 100_000));
        Assert.NotNull(AdminValidation.KiemTraSlotItem(-1_000, 100_000));
        Assert.NotNull(AdminValidation.KiemTraSlotItem(100_000, 100_000));
        Assert.NotNull(AdminValidation.KiemTraSlotItem(120_000, 100_000));
        Assert.Null(AdminValidation.KiemTraSlotItem(80_000, 100_000));
    }

    // ============ 3. AdminController: ThemVoucher / ThemSlotItem / ThemCombo ============

    [Fact]
    public async Task Admin_ThemVoucherAm_LoiHienRo_KhongLuu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (loi, soLuong) = await ThemVoucherAsync(services,
                VoucherMau(giaTri: -100));
            Assert.NotNull(loi);
            Assert.Contains("lớn hơn 0", loi);
            Assert.Equal(0, soLuong);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_ThemVoucherPhanTram150_VaHetHanQuaKhu_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (loi1, n1) = await ThemVoucherAsync(services, VoucherMau("PhanTram", 150));
            Assert.NotNull(loi1);
            Assert.Contains("100", loi1);
            Assert.Equal(0, n1);

            var hetHan = VoucherMau();
            hetHan.NgayHetHan = DateTime.Today.AddDays(-1);
            var (loi2, n2) = await ThemVoucherAsync(services, hetHan);
            Assert.NotNull(loi2);
            Assert.Contains("quá khứ", loi2);
            Assert.Equal(0, n2);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_ThemVoucherHopLe_LuuVaEpDaDungVe0()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var voucher = VoucherMau("PhanTram", 10);
            voucher.DaDung = 5; // client gui len — controller phai ep ve 0
            var (loi, soLuong) = await ThemVoucherAsync(services, voucher);
            Assert.Null(loi);
            Assert.Equal(1, soLuong);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var daLuu = await ctx.Vouchers.FirstAsync(v => v.Ma == "TEST");
            Assert.Equal(0, daLuu.DaDung);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_ThemSlotItem_GiaBangGiaBan_BiTuChoi_GiaReHon_DuocLuu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Slot", 100_000, tonKho: 10);
            var slotId = await SeedSlotAsync(services);

            var (loi, n1) = await ThemSlotItemAsync(services, slotId, productId, 100_000);
            Assert.NotNull(loi);
            Assert.Contains("rẻ hơn giá bán", loi);
            Assert.Equal(0, n1);

            var (loi2, n2) = await ThemSlotItemAsync(services, slotId, productId, 80_000);
            Assert.Null(loi2);
            Assert.Equal(1, n2);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_ThemCombo_KetThucQuaKhu_BiTuChoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Combo", 100_000, tonKho: 10);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var admin = new AdminController(ctx, null!, orders);
            GanTempData(admin);
            var ketQua = await admin.ThemCombo("Combo het han", null, 10,
                DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1),
                null, productId, 1, null, 0);
            Assert.IsType<RedirectToActionResult>(ketQua);
            var loi = admin.TempData["Error"] as string;
            Assert.NotNull(loi);
            Assert.Contains("quá khứ", loi);
            Assert.Equal(0, await ctx.Combos.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    // ============ 4. Variant stock: chi hien thi, mua khong tru bien the ============

    [Fact]
    public async Task MuaHangCoVariant_ChiTruTonChinh_BienTheKhongDoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Variant", 100_000, tonKho: 10);
            var variantId = await SeedVariantAsync(services, productId, "Den / 256GB", 10_000, tonKho: 7);

            using var scope = services.CreateScope();
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var ketQua = await checkout.DatHangAsync(new CheckoutInput
            {
                HoTenNhan = "Test",
                SoDienThoai = "0900000000",
                DiaChi = "Test",
                Lines = { new CheckoutLineInput(productId, variantId, 1, 110_000) },
                IdempotencyKey = "VAR-1",
            });
            Assert.True(ketQua.ThanhCong, ketQua.Loi);

            using var scope2 = services.CreateScope();
            var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var sp = await ctx.Products.FirstAsync(p => p.Id == productId);
            var variant = await ctx.ProductVariants.FirstAsync(v => v.Id == variantId);
            Assert.Equal(9, sp.TonKho); // chi tru ton chinh
            Assert.Equal(7, variant.TonKho); // bien the khong doi (chi hien thi)
            Assert.Equal(1, sp.LuotBan);
        }
        finally { DeleteDb(db); }
    }

    // ============ 5. Seed: ton sau don mau + tong bien the = ton SP ============

    [Fact]
    public async Task Seed_TonKhoSauDonMau_VaTongBienTheKhopTonSP()
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
            using (var scope = services.CreateScope())
                await SeedData.InitializeAsync(scope.ServiceProvider);

            using var scope2 = services.CreateScope();
            var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var ton = await ctx2.Products.ToDictionaryAsync(p => p.Ten, p => p.TonKho);
            // Don mau: don1 Xiaomi x1 + Buds x1; don2 TUF x1; don3 WH-CH520 x2.
            Assert.Equal(24, ton["Xiaomi 15 5G"]);
            Assert.Equal(49, ton["Tai nghe True Wireless Xiaomi Buds 5"]);
            Assert.Equal(11, ton["Laptop ASUS TUF Gaming F15"]);
            Assert.Equal(38, ton["Tai nghe Sony WH-CH520"]);
            // Nhap lo dau tru don mau = ton hien tai.
            var nhapXiaomi = await ctx2.StockIns
                .Where(s => s.Product.Ten == "Xiaomi 15 5G").SumAsync(s => s.SoLuong);
            Assert.Equal(25, nhapXiaomi);
            // Tong TonKho bien the = TonKho SP (moi SP co bien the).
            var products = await ctx2.Products
                .Include(p => p.Variants).Where(p => p.Variants.Any()).ToListAsync();
            Assert.Equal(4, products.Count);
            foreach (var p in products)
                Assert.Equal(p.TonKho, p.Variants.Sum(v => v.TonKho));
        }
        finally { DeleteDb(db); }
    }

    // ============ 6. FlashSale.DaBan: +1 dung 1 lan khi tao don, hoan khi huy ============

    [Fact]
    public async Task FlashSale_DaBan_TangDungMotLanKhiTaoDon_HoanKhiHuy()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP DaBan", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);

            var ketQua = await CheckoutRiengAsync(services, productId, 1, "DABAN-1", 70_000);
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            Assert.Equal(1, await GetDaBanAsync(services, productId));
            Assert.Equal(1, await GetLuotBanAsync(services, productId));

            var huy = await HuyDonAsync(services, ketQua.DonHang!.Id);
            Assert.True(huy.ThanhCong, huy.Loi);
            Assert.Equal(0, await GetDaBanAsync(services, productId));
            Assert.Equal(0, await GetLuotBanAsync(services, productId));
        }
        finally { DeleteDb(db); }
    }

    // ============ 7. Admin doi trang thai: sai luat bi chan ngay o controller ============

    [Fact]
    public async Task Admin_DoiTrangThai_DaGiaoSangDaHuy_BiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP TT", 100_000, tonKho: 5);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "TT-1");
            foreach (var tt in new[] { OrderStatus.DaXacNhan, OrderStatus.DangDongGoi,
                OrderStatus.DangGiao, OrderStatus.DaGiao })
                Assert.True((await DoiTrangThaiAsync(services, orderId, tt)).ThanhCong);

            var (loi, trangThai) = await AdminDoiTrangThaiAsync(services, orderId, OrderStatus.DaHuy);
            Assert.NotNull(loi);
            Assert.Contains("Không thể chuyển", loi);
            Assert.Equal(OrderStatus.DaGiao, trangThai);
            Assert.Equal(4, await GetTonKhoAsync(services, productId)); // khong hoan ton
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_DoiTrangThai_DaHuyKhongHoiSinh()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP TT2", 100_000, tonKho: 5);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "TT-2");
            Assert.True((await HuyDonAsync(services, orderId)).ThanhCong);

            var (loi, trangThai) = await AdminDoiTrangThaiAsync(services, orderId, OrderStatus.DaXacNhan);
            Assert.NotNull(loi);
            Assert.Contains("Không thể chuyển", loi);
            Assert.Equal(OrderStatus.DaHuy, trangThai);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Admin_DoiTrangThai_HopLe_ThanhCong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP TT3", 100_000, tonKho: 5);
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "TT-3");

            var (loi, trangThai) = await AdminDoiTrangThaiAsync(services, orderId, OrderStatus.DaXacNhan);
            Assert.Null(loi);
            Assert.Equal(OrderStatus.DaXacNhan, trangThai);
        }
        finally { DeleteDb(db); }
    }

    // ============ 8. ReturnsController.Create: chan don chua giao ngay luc tao ============

    [Fact]
    public async Task DoiTra_DonChuaGiao_TaoYeuCauBiChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP DT", 100_000, tonKho: 5);
            await SeedUserAsync(services, "user-1");
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "DT-1", userId: "user-1");
            Assert.Equal(OrderStatus.ChoXacNhan, await GetTrangThaiAsync(services, orderId));

            // Ca GET hien form va POST luu deu phai chan.
            var (loiGet, _) = await ReturnsCreateGetAsync(services, orderId, productId);
            Assert.NotNull(loiGet);
            Assert.Contains("đang giao hoặc đã giao", loiGet);

            var (loiPost, soYeuCau) = await ReturnsCreatePostAsync(services, orderId, productId, "Muốn đổi");
            Assert.NotNull(loiPost);
            Assert.Contains("đang giao hoặc đã giao", loiPost);
            Assert.Equal(0, soYeuCau);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTra_DonDaGiao_TaoYeuCauDuoc()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP DT2", 100_000, tonKho: 5);
            await SeedUserAsync(services, "user-1");
            var orderId = await DatDonThanhCongAsync(services, productId, 1, "DT-2", userId: "user-1");
            foreach (var tt in new[] { OrderStatus.DaXacNhan, OrderStatus.DangDongGoi,
                OrderStatus.DangGiao, OrderStatus.DaGiao })
                Assert.True((await DoiTrangThaiAsync(services, orderId, tt)).ThanhCong);

            var (loiPost, soYeuCau) = await ReturnsCreatePostAsync(services, orderId, productId, "Hỏng nút nguồn");
            Assert.Null(loiPost);
            Assert.Equal(1, soYeuCau);
        }
        finally { DeleteDb(db); }
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
        services.AddScoped<NotificationService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static async Task<int> SeedProductAsync(ServiceProvider services, string ten, decimal gia, int tonKho)
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
        var product = new Product
        {
            Ten = ten,
            Gia = gia,
            CategoryId = category.Id,
            BrandId = brand.Id,
            TonKho = tonKho,
            LuotBan = 0,
        };
        ctx2.Products.Add(product);
        await ctx2.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<int> SeedVariantAsync(ServiceProvider services, int productId,
        string ten, decimal giaThem, int tonKho)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var variant = new ProductVariant
        {
            ProductId = productId,
            TenBienThe = ten,
            GiaThem = giaThem,
            TonKho = tonKho,
        };
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();
        return variant.Id;
    }

    private static async Task SeedFlashSaleAsync(ServiceProvider services, int productId,
        decimal giaSale, DateTime batDau, DateTime ketThuc, int gioiHan, int daBan)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.FlashSales.Add(new FlashSale
        {
            ProductId = productId,
            GiaSale = giaSale,
            BatDau = batDau,
            KetThuc = ketThuc,
            GioiHanSL = gioiHan,
            DaBan = daBan,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> SeedSlotAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slot = new FlashSaleSlot { GioBatDau = 0, GioKetThuc = 24, IsActive = true };
        ctx.FlashSaleSlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
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

    /// <summary>TempData trong bo nho cho test controller (khong can server that).</summary>
    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public Dictionary<string, object?> Store { get; } = new();
        public IDictionary<string, object?> LoadTempData(HttpContext context) => Store;
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static void GanTempData(Controller controller)
    {
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new FakeTempDataProvider());
    }

    private static void GanTempDataVaUser(Controller controller, string userName)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, userName) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new FakeTempDataProvider());
    }

    private static async Task<(string? Loi, int SoVoucher)> ThemVoucherAsync(
        ServiceProvider services, Voucher voucher)
    {
        using (var scope0 = services.CreateScope())
        {
            var ctx0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx0.Database.MigrateAsync();
        }
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        var admin = new AdminController(ctx, null!, orders);
        GanTempData(admin);
        var ketQua = await admin.ThemVoucher(voucher);
        Assert.IsType<RedirectToActionResult>(ketQua);
        var loi = admin.TempData["Error"] as string;
        var soLuong = await ctx.Vouchers.CountAsync(v => v.Ma == voucher.Ma.Trim().ToUpper());
        return (loi, soLuong);
    }

    private static async Task<(string? Loi, int SoItem)> ThemSlotItemAsync(
        ServiceProvider services, int slotId, int productId, decimal giaSale)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        var admin = new AdminController(ctx, null!, orders);
        GanTempData(admin);
        var ketQua = await admin.ThemSlotItem(slotId, productId, giaSale);
        Assert.IsType<RedirectToActionResult>(ketQua);
        var loi = admin.TempData["Error"] as string;
        var soItem = await ctx.FlashSaleSlotItems
            .CountAsync(i => i.SlotId == slotId && i.ProductId == productId);
        return (loi, soItem);
    }

    private static async Task<(string? Loi, string TrangThai)> AdminDoiTrangThaiAsync(
        ServiceProvider services, int orderId, string trangThaiMoi)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
        var admin = new AdminController(ctx, null!, orders);
        GanTempData(admin);
        var ketQua = await admin.DoiTrangThai(orderId, trangThaiMoi);
        var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
        Assert.Equal(nameof(AdminController.OrderDetails), redirect.ActionName);
        var loi = admin.TempData["Error"] as string;
        var trangThai = await ctx.Orders.AsNoTracking()
            .Where(o => o.Id == orderId).Select(o => o.TrangThai).FirstAsync();
        return (loi, trangThai);
    }

    private static async Task<(string? Loi, int SoYeuCau)> ReturnsCreatePostAsync(
        ServiceProvider services, int orderId, int productId, string lyDo)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doiTra = scope.ServiceProvider.GetRequiredService<ReturnService>();
        var controller = new ReturnsController(ctx, null!, doiTra);
        GanTempDataVaUser(controller, "test-user");
        var ketQua = await controller.Create(orderId, productId, lyDo, null);
        var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
        var loi = controller.TempData["Error"] as string;
        var soYeuCau = await ctx.ReturnRequests.CountAsync(r => r.OrderId == orderId);
        return (loi, soYeuCau);
    }

    private static async Task<(string? Loi, string? Action)> ReturnsCreateGetAsync(
        ServiceProvider services, int orderId, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doiTra = scope.ServiceProvider.GetRequiredService<ReturnService>();
        var controller = new ReturnsController(ctx, null!, doiTra);
        GanTempDataVaUser(controller, "test-user");
        var ketQua = await controller.Create(orderId, productId);
        var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
        var loi = controller.TempData["Error"] as string;
        return (loi, redirect.ActionName);
    }

    private static async Task<CheckoutResult> CheckoutRiengAsync(ServiceProvider services,
        int productId, int soLuong, string key, decimal donGia = 100_000, string? userId = null)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, soLuong, donGia) },
            IdempotencyKey = key,
        });
    }

    private static async Task<int> DatDonThanhCongAsync(ServiceProvider services,
        int productId, int soLuong, string key, string? userId = null)
    {
        var ketQua = await CheckoutRiengAsync(services, productId, soLuong, key, 100_000, userId);
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        return ketQua.DonHang!.Id;
    }

    private static async Task<DoiTrangThaiResult> DoiTrangThaiAsync(
        ServiceProvider services, int orderId, string trangThaiMoi)
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

    private static async Task<int> GetDaBanAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.FlashSales.Where(f => f.ProductId == productId)
            .Select(f => f.DaBan).FirstAsync();
    }

    private static async Task<int> GetLuotBanAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.LuotBan).FirstAsync();
    }

    private static async Task<int> GetTonKhoAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync();
    }

    private static async Task<string> GetTrangThaiAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.Where(o => o.Id == orderId).Select(o => o.TrangThai).FirstAsync();
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
