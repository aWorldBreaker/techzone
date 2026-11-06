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
/// Test P1 (2026-11-06): TỔNG GIỎ = TỔNG CHECKOUT khi không đổi dữ liệu.
/// Gốc lỗi: giỏ chỉ trừ voucher, checkout trừ thêm giảm hạng 2% trên toàn
/// bộ tạm tính (khách Vàng, vd Xiaomi 15 + TECHZONE500: giỏ 12.490.000đ
/// nhưng checkout 12.230.200đ). Từ nay cả hai đi qua cùng 1 hàm
/// PriceCalculationService.TinhTongDonAsync (voucher + combo + hạng + điểm
/// + phí ship), test này khóa parity đó cho từng hạng + từng mốc
/// Flash Sale / slot / voucher / combo / điểm: tổng đơn tính được phải
/// bằng Order.TongTien / Order.ThanhToan mà CheckoutService tạo ra.
/// </summary>
[Collection("DbTests")]
public class CartCheckoutParityTests
{
    private readonly ITestOutputHelper _out;

    public CartCheckoutParityTests(ITestOutputHelper output) => _out = output;

    // ============ Hạng Vàng, không voucher ============

    [Fact]
    public async Task Parity_Vang_Xiaomi15_KhongVoucher()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m); // > 20tr -> Vang

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang");

            Assert.Equal(12_990_000m, tong.TamTinh);
            Assert.Equal("Vàng", tong.Hang);
            Assert.Equal(259_800m, tong.GiamGiaHang); // 2% cua 12.990.000
            Assert.Equal(12_730_200m, tong.SauGiam);
            Assert.Equal(0m, tong.PhiShip); // hang Vang mien ship
            Assert.Equal(12_730_200m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-1");
            _out.WriteLine($"Vang: tamTinh={tong.TamTinh:N0} giamHang={tong.GiamGiaHang:N0} tong={tong.TongCong:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Ví dụ trong báo cáo lỗi: Vàng + TECHZONE500 ============

    [Fact]
    public async Task Parity_Vang_Voucher500k_ViDuBaoCao()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "TECHZONE500", "Tien", 500_000, 0);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m); // Vang

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", "TECHZONE500");

            // Gio CU chi tru voucher: 12.990.000 - 500.000 = 12.490.000 (so
            // user thay o gio). Tong DUNG sau khi them giam hang:
            // 12.990.000 - 500.000 - 259.800 (2% TOAN BO tam tinh) = 12.230.200.
            Assert.Equal(12_990_000m, tong.TamTinh);
            Assert.Equal(500_000m, tong.GiamGiaVoucher);
            Assert.Equal(259_800m, tong.GiamGiaHang);
            Assert.Equal(12_230_200m, tong.SauGiam);
            Assert.Equal(12_230_200m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-500K");
            _out.WriteLine($"Vang+TECHZONE500: gio-cu=12.490.000 tong-dung={tong.TongCong:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Hạng Bạc: không giảm hạng, parity vẫn giữ ============

    [Fact]
    public async Task Parity_Bac_KhongGiamHang()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-bac");
            await SeedOrderAsync(services, "user-bac", 10_000_000m); // 5-20tr -> Bac

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-bac");

            Assert.Equal("Bạc", tong.Hang);
            Assert.Equal(0m, tong.GiamGiaHang);
            Assert.Equal(12_990_000m, tong.SauGiam);
            Assert.Equal(0m, tong.PhiShip); // SauGiam >= 2tr
            Assert.Equal(12_990_000m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-bac", tong, "P1-BAC-1");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Hạng Đồng, đơn nhỏ: không giảm hạng, ship 30k ============

    [Fact]
    public async Task Parity_Dong_GiaThap_PhiShip30k()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP 100k", "Test Brand", 100_000, tonKho: 10);
            await SeedUserAsync(services, "user-dong");

            var lines = await LoadLinesAsync(services, (spId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-dong");

            Assert.Equal("Đồng", tong.Hang);
            Assert.Equal(0m, tong.GiamGiaHang);
            Assert.Equal(100_000m, tong.SauGiam);
            Assert.Equal(30_000m, tong.PhiShip); // < 2tr, khong mien ship
            Assert.Equal(130_000m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-dong", tong, "P1-DONG-1");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Hạng Bạch kim: vẫn giảm 2% như Vàng ============

    [Fact]
    public async Task Parity_BachKim_Giam2PhanTram()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-pt");
            await SeedOrderAsync(services, "user-pt", 60_000_000m); // >= 50tr -> Bach kim

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-pt");

            Assert.Equal("Bạch kim", tong.Hang);
            Assert.Equal(259_800m, tong.GiamGiaHang);
            Assert.Equal(12_730_200m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-pt", tong, "P1-PT-1");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Flash Sale: giảm hạng tính trên giá sale ============

    [Fact]
    public async Task Parity_Vang_FlashSale()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, xiaomiId, 11_990_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 10, daBan: 0);

            var lines = await LoadLinesTaiThoiDiemAsync(services, now, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", thoiDiem: now);

            Assert.Equal(11_990_000m, tong.TamTinh); // gia sale, khong phai 12.990.000
            Assert.Equal(239_800m, tong.GiamGiaHang); // 2% cua 11.990.000
            Assert.Equal(11_750_200m, tong.SauGiam);
            Assert.Equal(11_750_200m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-FLASH");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Slot theo giờ: giảm hạng tính trên giá slot ============

    [Fact]
    public async Task Parity_Vang_SlotTheoGio()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spId = await SeedProductAsync(services, "SP Slot", "Test Brand", 100_000, tonKho: 10);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);
            await SeedSlotItemAsync(services, spId, 80_000, gioBatDau: 0, gioKetThuc: 24);

            var lines = await LoadLinesAsync(services, (spId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang");

            Assert.Equal(80_000m, tong.TamTinh); // gia slot
            Assert.Equal(1_600m, tong.GiamGiaHang); // 2% cua 80.000
            Assert.Equal(78_400m, tong.SauGiam);
            Assert.Equal(0m, tong.PhiShip); // hang Vang mien ship
            Assert.Equal(78_400m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-SLOT");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Vàng + voucher chỉ áp 1 hãng: voucher theo phạm vi, hạng theo toàn bộ ============

    [Fact]
    public async Task Parity_Vang_VoucherXiaomi10_ChiGiamPhanXiaomi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, iphoneId) = await SeedXiaomiIphoneAsync(services);
            await SeedVoucherAsync(services, "XIAOMI10", "PhanTram", 10, 0, brandTen: "Xiaomi");
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);

            var lines = await LoadLinesAsync(services, (xiaomiId, 1), (iphoneId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", "XIAOMI10");

            Assert.Equal(41_980_000m, tong.TamTinh);
            Assert.Equal(1_299_000m, tong.GiamGiaVoucher); // 10% chi phan Xiaomi
            Assert.Equal(839_600m, tong.GiamGiaHang); // 2% TOAN BO tam tinh
            Assert.Equal(39_841_400m, tong.SauGiam);
            Assert.Equal(39_841_400m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-X10");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Vàng + combo 10% ============

    [Fact]
    public async Task Parity_Vang_Combo()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var a = await SeedProductAsync(services, "SP A", "Test Brand", 100_000, tonKho: 10);
            var b = await SeedProductAsync(services, "SP B", "Test Brand", 50_000, tonKho: 10);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);
            var comboId = await SeedComboAsync(services, a, b, phanTram: 10);

            var lines = await LoadLinesAsync(services, (a, 1), (b, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", comboId: comboId);

            Assert.Equal(150_000m, tong.TamTinh);
            Assert.Equal(15_000m, tong.GiamGiaCombo); // 10% cua 150.000
            Assert.Equal(3_000m, tong.GiamGiaHang); // 2% cua 150.000
            Assert.Equal(132_000m, tong.SauGiam);
            Assert.Equal(132_000m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-COMBO");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Vàng + đổi 100 điểm ============

    [Fact]
    public async Task Parity_Vang_Diem100()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);
            await SeedEarnAsync(services, "user-vang", 100);

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", diemDung: 100);

            Assert.Equal(259_800m, tong.GiamGiaHang);
            Assert.Equal(100_000m, tong.GiamGiaDiem);
            Assert.Equal(100, tong.DiemDungThucTe);
            Assert.Equal(12_630_200m, tong.SauGiam); // 12.990.000 - 259.800 - 100.000
            Assert.Equal(12_630_200m, tong.TongCong);

            await AssertParityVoiDonHangAsync(services, lines, "user-vang", tong, "P1-VANG-DIEM");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Voucher sai mã: giảm 0 nhưng tổng hạng vẫn đúng ============

    [Fact]
    public async Task TinhTongDon_VoucherSai_KhongLamHongTong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _) = await SeedXiaomiIphoneAsync(services);
            await SeedUserAsync(services, "user-vang");
            await SeedOrderAsync(services, "user-vang", 25_000_000m);

            var lines = await LoadLinesAsync(services, (xiaomiId, 1));
            var tong = await TinhTongDonAsync(services, lines, "user-vang", "KHONGTONTAI");

            Assert.Equal(0m, tong.GiamGiaVoucher);
            Assert.NotNull(tong.LoiVoucher);
            Assert.Equal(259_800m, tong.GiamGiaHang);
            Assert.Equal(12_730_200m, tong.TongCong);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

    /// <summary>
    /// Parity cốt lõi: đặt đơn thật qua CheckoutService với đúng các số
    /// từ hàm tổng thống nhất, rồi khóa Order.TongTien / ThanhToan /
    /// GiamGia* phải bằng các số đó (không đổi dữ liệu giữa 2 lần tính).
    /// </summary>
    private static async Task AssertParityVoiDonHangAsync(ServiceProvider services,
        List<CartLineViewModel> lines, string userId, KetQuaTongDon tong, string key)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var ketQua = await checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = tong.MaVoucher,
            GiamGia = tong.GiamGiaVoucher,
            GiamGiaHang = tong.GiamGiaHang,
            GiamGiaDiem = tong.GiamGiaDiem,
            DiemDung = tong.DiemDungThucTe,
            ComboId = tong.ComboId,
            GiamGiaCombo = tong.GiamGiaCombo,
            FreeShip = tong.FreeShip,
            Lines = lines.Select(l => new CheckoutLineInput(
                l.Product.Id, l.Variant?.Id, l.SoLuong, l.DonGia)).ToList(),
            IdempotencyKey = key,
        });
        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(tong.SauGiam, don.TongTien);
        Assert.Equal(tong.PhiShip, don.PhiShip);
        Assert.Equal(tong.TongCong, don.ThanhToan);
        Assert.Equal(tong.GiamGiaVoucher, don.GiamGia);
        Assert.Equal(tong.GiamGiaHang, don.GiamGiaHang);
        Assert.Equal(tong.GiamGiaCombo, don.GiamGiaCombo);
        Assert.Equal(tong.GiamGiaDiem, don.GiamGiaDiem);
    }

    private static async Task<KetQuaTongDon> TinhTongDonAsync(ServiceProvider services,
        List<CartLineViewModel> lines, string? userId, string? maVoucher = null,
        int diemDung = 0, int? comboId = null, DateTime? thoiDiem = null)
    {
        using var scope = services.CreateScope();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        return await gia.TinhTongDonAsync(lines, userId, maVoucher, diemDung, comboId, thoiDiem);
    }

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

    private static async Task SeedSlotItemAsync(ServiceProvider services, int productId,
        decimal giaSale, int gioBatDau, int gioKetThuc)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slot = new FlashSaleSlot { GioBatDau = gioBatDau, GioKetThuc = gioKetThuc, IsActive = true };
        ctx.FlashSaleSlots.Add(slot);
        await ctx.SaveChangesAsync();
        ctx.FlashSaleSlotItems.Add(new FlashSaleSlotItem
        {
            SlotId = slot.Id,
            ProductId = productId,
            GiaSale = giaSale,
        });
        await ctx.SaveChangesAsync();
    }

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

    /// <summary>Dòng giỏ với DonGia thực trả qua PriceCalculationService (sale/slot/gốc).</summary>
    private static async Task<List<CartLineViewModel>> LoadLinesAsync(ServiceProvider services,
        params (int ProductId, int SoLuong)[] items) =>
        await LoadLinesTaiThoiDiemAsync(services, null, items);

    private static async Task<List<CartLineViewModel>> LoadLinesTaiThoiDiemAsync(ServiceProvider services,
        DateTime? thoiDiem, params (int ProductId, int SoLuong)[] items)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        var lines = new List<CartLineViewModel>();
        foreach (var (productId, soLuong) in items)
        {
            var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
            var giaApDung = await gia.TinhGiaAsync(productId, 0, soLuong, thoiDiem);
            lines.Add(new CartLineViewModel
            {
                Product = product,
                DonGia = giaApDung.DonGia,
                GiaGoc = giaApDung.GiaGoc,
                TenKhuyenMai = giaApDung.TenKhuyenMai,
                SoLuong = soLuong
            });
        }
        return lines;
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
