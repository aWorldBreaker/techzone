using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
/// Test Sprint 2 — B-5: Flash Sale giá thật.
/// - Giá sale chỉ áp khi còn trong khung giờ + còn suất (biên start/end,
///   hết quota), qua PriceCalculationService duy nhất.
/// - 2 người tranh suất cuối: đúng 1 người được giá sale, DaBan không vượt.
/// - Lượt bán (DaBan/LuotBan) ghi 1 lần khi tạo đơn, hủy hoàn 1 lần
///   (hủy 2 lần chỉ hoàn 1 lần); thêm vào giỏ không ghi lượt bán.
/// - Giá giỏ (CartService) == giá checkout thu == snapshot OrderDetail.
/// DB test: file tạm + MigrateAsync (migration chung AddSprint2Financial
/// đã áp cột snapshot — GiaGoc/TenKhuyenMai/FlashSaleId).
/// </summary>
[Collection("DbTests")]
public class FlashSaleTests
{
    private readonly ITestOutputHelper _out;

    public FlashSaleTests(ITestOutputHelper output) => _out = output;

    // ============ Bien thoi gian: truoc / dung bat dau / dung ket thuc / sau ============

    [Fact]
    public async Task GiaSale_BienThoiGian_TruocTrongSau()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Bien", 100_000, tonKho: 10);
            var batDau = new DateTime(2026, 11, 5, 12, 0, 0);
            var ketThuc = new DateTime(2026, 11, 5, 20, 0, 0);
            await SeedFlashSaleAsync(services, productId, 70_000, batDau, ketThuc, gioiHan: 5, daBan: 0);

            var truoc = await TinhGiaAsync(services, productId, batDau.AddTicks(-1));
            var dungBatDau = await TinhGiaAsync(services, productId, batDau);
            var dungKetThuc = await TinhGiaAsync(services, productId, ketThuc);
            var sau = await TinhGiaAsync(services, productId, ketThuc.AddTicks(1));

            Assert.Equal(100_000, truoc.DonGia);
            Assert.False(truoc.LaGiaSale);
            Assert.Equal(70_000, dungBatDau.DonGia);
            Assert.True(dungBatDau.LaGiaSale);
            Assert.Equal(70_000, dungKetThuc.DonGia);
            Assert.True(dungKetThuc.LaGiaSale);
            Assert.Equal(100_000, sau.DonGia);
            Assert.False(sau.LaGiaSale);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Het quota: DaBan == GioiHanSL thi khong con gia sale ============

    [Fact]
    public async Task GiaSale_HetQuota_KhongConGiaSale()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP HetQuota", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 5);

            var gia = await TinhGiaAsync(services, productId, now);
            Assert.Equal(100_000, gia.DonGia);
            Assert.False(gia.LaGiaSale);
            Assert.True(gia.HetQuota);
            Assert.Null(gia.FlashSaleId);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Race: 2 nguoi tranh suat cuoi -> 1 duoc gia sale ============

    [Fact]
    public async Task GiaSale_HaiNguoiTranhSuatCuoi_MotDuocGiaSale()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Race", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 1, daBan: 0);

            var t1 = CheckoutRiengAsync(services, productId, 1, key: "FS-RACE-1", donGia: 70_000);
            var t2 = CheckoutRiengAsync(services, productId, 1, key: "FS-RACE-2", donGia: 70_000);
            var results = await Task.WhenAll(t1, t2);
            _out.WriteLine($"kq1={results[0].ThanhCong}/{results[0].Loi} kq2={results[1].ThanhCong}/{results[1].Loi}");

            Assert.Single(results, r => r.ThanhCong);
            Assert.Single(results, r => !r.ThanhCong);
            Assert.Contains(results, r => !r.ThanhCong && r.Loi != null && r.Loi.Contains("hết suất"));
            Assert.Equal(1, await CountOrdersAsync(services));
            Assert.Equal(1, await GetDaBanAsync(services, productId));
            Assert.Equal(1, await GetLuotBanAsync(services, productId));
            Assert.Equal(9, await GetTonKhoAsync(services, productId));
            var donThang = results.Single(r => r.ThanhCong).DonHang!;
            Assert.Equal(70_000, donThang.TongTien);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Huy don hoan luot ban, huy 2 lan chi hoan 1 lan ============

    [Fact]
    public async Task HuyDon_HoanLuotBan_HaiLanChiHoanMotLan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Huy", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);

            var ketQua = await CheckoutRiengAsync(services, productId, 2, key: "FS-HUY-1", donGia: 70_000);
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            var orderId = ketQua.DonHang!.Id;
            Assert.Equal(2, await GetDaBanAsync(services, productId));
            Assert.Equal(2, await GetLuotBanAsync(services, productId));
            Assert.Equal(8, await GetTonKhoAsync(services, productId));

            var lan1 = await HuyDonAsync(services, orderId);
            Assert.True(lan1.ThanhCong, lan1.Loi);
            Assert.False(lan1.DaOTrangThaiDich);
            Assert.Equal(0, await GetDaBanAsync(services, productId));
            Assert.Equal(0, await GetLuotBanAsync(services, productId));
            Assert.Equal(10, await GetTonKhoAsync(services, productId));

            var lan2 = await HuyDonAsync(services, orderId);
            Assert.True(lan2.ThanhCong, lan2.Loi);
            Assert.True(lan2.DaOTrangThaiDich);
            Assert.Equal(0, await GetDaBanAsync(services, productId));
            Assert.Equal(0, await GetLuotBanAsync(services, productId));
            Assert.Equal(10, await GetTonKhoAsync(services, productId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Gio == checkout == snapshot OrderDetail; them gio khong ghi luot ban ============

    [Fact]
    public async Task GiaGio_Checkout_Snapshot_KhopNhau()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP KhopGia", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);

            // Gio hang: them 1 cai, gia hien thi phai la gia sale.
            var giaGio = await GiaGioAsync(services, productId, 1);
            Assert.Equal(70_000, giaGio.DonGia);
            Assert.Equal(100_000, giaGio.GiaGoc);
            Assert.True(giaGio.LaGiaSale);
            Assert.Equal("Flash Sale", giaGio.TenKhuyenMai);
            // Them vao gio KHONG ghi luot ban.
            Assert.Equal(0, await GetDaBanAsync(services, productId));
            Assert.Equal(0, await GetLuotBanAsync(services, productId));

            // Checkout voi dung gia gio hien: thu dung 70.000.
            var ketQua = await CheckoutRiengAsync(services, productId, 1, key: "FS-KHOP-1", donGia: 70_000);
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            Assert.Equal(70_000, ketQua.DonHang!.TongTien);

            // Snapshot trong OrderDetail.
            var detail = await GetDetailAsync(services, ketQua.DonHang.Id);
            Assert.Equal(70_000, detail.DonGia);
            Assert.Equal(100_000, detail.GiaGoc);
            Assert.Equal("Flash Sale", detail.TenKhuyenMai);
            Assert.NotNull(detail.FlashSaleId);
            Assert.Equal(1, await GetDaBanAsync(services, productId));
            Assert.Equal(1, await GetLuotBanAsync(services, productId));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Slot: ap khi khong co FlashSale; khong pha quota da het ============

    [Fact]
    public async Task GiaSale_Slot_ChiKhiKhongCoFlashSale()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var spSlot = await SeedProductAsync(services, "SP Slot", 100_000, tonKho: 10);
            await SeedSlotItemAsync(services, spSlot, 80_000, gioBatDau: 0, gioKetThuc: 24);

            var giaSlot = await TinhGiaAsync(services, spSlot, new DateTime(2026, 11, 5, 10, 0, 0));
            Assert.Equal(80_000, giaSlot.DonGia);
            Assert.True(giaSlot.LaGiaSale);
            Assert.Null(giaSlot.FlashSaleId); // slot khong co quota de tru

            // SP co FlashSale da het quota + slot re hon: van thu gia goc,
            // slot khong duoc phep pha gioi han suat cua FlashSale.
            var spHet = await SeedProductAsync(services, "SP Slot Het", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, spHet, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 2, daBan: 2);
            await SeedSlotItemAsync(services, spHet, 60_000, gioBatDau: 0, gioKetThuc: 24);
            var giaHet = await TinhGiaAsync(services, spHet, now);
            Assert.Equal(100_000, giaHet.DonGia);
            Assert.True(giaHet.HetQuota);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ SALE-01: Details dung chung PriceCalculationService ============
    // Test o muc service: so sanh TinhGiaAsync / TinhGiaComboAsync (gia
    // Details hien gio dung) voi logic cu cua Details.cshtml
    // (flash?.GiaSale ?? GiaBan / cong GiaBan cho combo). Khong test
    // controller HTTP.

    [Fact]
    public async Task Details_SlotTheoGio_HienGiaSale_LogicCuBoQua()
    {
        // (a) Chi co slot theo gio, khong co dot FlashSale: logic cu
        // khong thay sale (chi nhin bang FlashSales), service tra gia slot.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Slot Details", 100_000, tonKho: 10);
            await SeedSlotItemAsync(services, productId, 80_000, gioBatDau: 0, gioKetThuc: 24);
            var thoiDiem = new DateTime(2026, 11, 5, 10, 0, 0);

            var giaMoi = await TinhGiaAsync(services, productId, thoiDiem);
            Assert.Equal(80_000, giaMoi.DonGia);
            Assert.True(giaMoi.LaGiaSale);
            Assert.Equal("Flash Sale theo khung giờ", giaMoi.TenKhuyenMai);

            var giaCu = await GiaCuDetailsAsync(services, productId, thoiDiem);
            Assert.Equal(100_000, giaCu);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Details_NhieuDotFlashSale_LayReNhat_LogicCuLayDotDau()
    {
        // (b) 2 dot FlashSale cung chay: service lay re nhat (60k);
        // logic cu FirstOrDefault khong sap xep lay dot tao truoc (70k).
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP Nhieu Dot", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);
            await SeedFlashSaleAsync(services, productId, 60_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);

            var giaMoi = await TinhGiaAsync(services, productId, now);
            Assert.Equal(60_000, giaMoi.DonGia);

            var giaCu = await GiaCuDetailsAsync(services, productId, now);
            Assert.Equal(70_000, giaCu);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Details_Combo_HienThiKhopCheckout()
    {
        // (c) Combo 10% gom SP 10.000.000 + SP 4.180.000 co Flash Sale
        // 4.080.000: tong bo thuc tra 14.080.000, giam 1.408.000 (khop
        // checkout). Logic cu cong GiaBan: tong 14.180.000, giam 1.418.000.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            var spChinh = await SeedProductAsync(services, "SP Combo Chinh", 10_000_000, tonKho: 10);
            var spKem = await SeedProductAsync(services, "SP Combo Kem", 4_180_000, tonKho: 10);
            await SeedFlashSaleAsync(services, spKem, 4_080_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 0);
            var comboId = await SeedComboAsync(services, "Combo test", 10,
                now.AddDays(-1), now.AddDays(1), (spChinh, 1), (spKem, 1));

            // Gia tung mon qua TinhGiaAsync (cach Details tinh gia mon hien thi).
            Assert.Equal(10_000_000, (await TinhGiaAsync(services, spChinh, now)).DonGia);
            Assert.Equal(4_080_000, (await TinhGiaAsync(services, spKem, now)).DonGia);

            // Tong bo + giam gia qua TinhGiaComboAsync (cung ham checkout dung).
            var lines = await LinesComboAsync(services, comboId);
            var ketQua = await TinhGiaComboAsync(services, comboId, lines, now);
            Assert.True(ketQua.HopLe, ketQua.Loi);
            Assert.Equal(1, ketQua.SoBo);
            Assert.Equal(14_080_000, ketQua.TongTienBo);
            Assert.Equal(1_408_000, ketQua.GiamGia);

            // Logic cu cong GiaBan lech ca tong bo lan tien giam.
            Assert.Equal(14_180_000, 10_000_000m + 4_180_000m);
            Assert.Equal(1_418_000, Math.Round((10_000_000m + 4_180_000m) * 10 / 100));
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ So luong vuot suat con lai: khong sale cho ca dong ============

    [Fact]
    public async Task GiaSale_SoLuongVuotSuatConLai_TuChoiCheckout()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var productId = await SeedProductAsync(services, "SP VuotSuat", 100_000, tonKho: 10);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, productId, 70_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 5, daBan: 4); // chi con 1 suat

            var gia = await TinhGiaAsync(services, productId, now, soLuong: 2);
            Assert.Equal(100_000, gia.DonGia);
            Assert.True(gia.HetQuota);

            var ketQua = await CheckoutRiengAsync(services, productId, 2, key: "FS-VUOT-1", donGia: 70_000);
            Assert.False(ketQua.ThanhCong);
            Assert.Contains("hết suất", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(4, await GetDaBanAsync(services, productId));
            Assert.Equal(10, await GetTonKhoAsync(services, productId));
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
        services.AddScoped<VatService>(); // Sprint 2 merge: CheckoutService can VatService
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>(); // Sprint 2 — Mang 4: OrderService can RefundService
        services.AddScoped<ReturnService>(); // Sprint 2 — Mang 4: duyet doi tra
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

    private static async Task<int> SeedComboAsync(ServiceProvider services, string ten,
        decimal phanTramGiam, DateTime batDau, DateTime ketThuc,
        params (int ProductId, int SoLuong)[] items)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var combo = new Combo
        {
            Ten = ten,
            PhanTramGiam = phanTramGiam,
            BatDau = batDau,
            KetThuc = ketThuc,
            IsActive = true,
            Items = items.Select(i => new ComboItem
            {
                ProductId = i.ProductId,
                SoLuong = i.SoLuong,
            }).ToList(),
        };
        ctx.Combos.Add(combo);
        await ctx.SaveChangesAsync();
        return combo.Id;
    }

    private static async Task<List<CartLineViewModel>> LinesComboAsync(
        ServiceProvider services, int comboId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var combo = await ctx.Combos.AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .FirstAsync(c => c.Id == comboId);
        return combo.Items.Select(i => new CartLineViewModel
        {
            Product = i.Product,
            Variant = null,
            SoLuong = i.SoLuong,
        }).ToList();
    }

    private static async Task<KetQuaCombo> TinhGiaComboAsync(ServiceProvider services,
        int comboId, List<CartLineViewModel> lines, DateTime thoiDiem)
    {
        using var scope = services.CreateScope();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        return await gia.TinhGiaComboAsync(comboId, lines, thoiDiem);
    }

    /// <summary>
    /// Mo phong logic cu cua Details.cshtml: flash?.GiaSale ?? GiaBan.
    /// OrderBy(Id) mo hinh FirstOrDefault khong sap xep (thuc te tra ve
    /// theo thu tu chen) de test xac dinh.
    /// </summary>
    private static async Task<decimal> GiaCuDetailsAsync(ServiceProvider services,
        int productId, DateTime thoiDiem)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await ctx.Products.AsNoTracking()
            .Include(p => p.FlashSales).FirstAsync(p => p.Id == productId);
        var flash = product.FlashSales
            .OrderBy(f => f.Id).FirstOrDefault(f => f.DangChay(thoiDiem));
        return flash?.GiaSale ?? product.GiaBan;
    }

    private static async Task<GiaApDungResult> TinhGiaAsync(ServiceProvider services,
        int productId, DateTime thoiDiem, int soLuong = 1)
    {
        using var scope = services.CreateScope();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        return await gia.TinhGiaAsync(productId, 0, soLuong, thoiDiem);
    }

    /// <summary>Gia gio hang that qua CartService (session gia).</summary>
    private static async Task<CartLineViewModel> GiaGioAsync(ServiceProvider services, int productId, int soLuong)
    {
        using var scope = services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext();
        http.Features.Set<ISessionFeature>(new FakeSessionFeature { Session = new FakeSession() });
        accessor.HttpContext = http;
        var cart = scope.ServiceProvider.GetRequiredService<CartService>();
        cart.Add(productId, null, soLuong);
        var vm = await cart.GetViewModelAsync();
        return vm.Lines.Single();
    }

    private static async Task<CheckoutResult> CheckoutRiengAsync(ServiceProvider services,
        int productId, int soLuong, string key, decimal donGia = 100_000)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(new CheckoutInput
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, soLuong, donGia) },
            IdempotencyKey = key,
        });
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

    private static async Task<OrderDetail> GetDetailAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.OrderDetails.AsNoTracking().FirstAsync(d => d.OrderId == orderId);
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    /// <summary>Session trong bo nho cho test CartService (khong can server that).</summary>
    private sealed class FakeSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
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
