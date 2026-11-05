using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test Sprint 2 — B-10 (VAT, NĐ 174/2025/NĐ-CP).
/// - Gia niem yet DA GOM VAT: tach thue ra, tong tien khong doi (khong cong them).
/// - Thue suat theo danh muc + ngay hieu luc (8% trong cua so 01/07/2025–
///   31/12/2026, 10% ngoai cua so / khong co rule); snapshot vao OrderDetail.
/// - Hoa don DN thieu thong tin -> tu choi; hoa don trong app la demo.
/// Cau hinh test (khong dung appsettings that): "test-cat-8" 8% trong cua so,
/// "test-cat-10" 10% moi luc. Nguon phap ly xem comment o VatService.
/// </summary>
[Collection("DbTests")]
public class VatTests
{
    private readonly ITestOutputHelper _out;

    public VatTests(ITestOutputHelper output) => _out = output;

    private static VatOptions TestOptions() => new()
    {
        GiaDaGomVat = true,
        ThueSuatMacDinh = 0.10m,
        Rules =
        {
            new VatRuleConfig
            {
                CategorySlug = "test-cat-8", ThueSuat = 0.08m,
                HieuLucTu = new DateTime(2025, 7, 1), HieuLucDen = new DateTime(2026, 12, 31),
                DaXacMinh = false, GhiChu = "NEEDS_TAX_REVIEW (test)",
            },
            new VatRuleConfig
            {
                CategorySlug = "test-cat-10", ThueSuat = 0.10m,
                DaXacMinh = false, GhiChu = "Nhóm 10% (test)",
            },
        },
    };

    private static VatService TaoVat() => new(Options.Create(TestOptions()));

    // ============ Tach VAT khoi gia da gom VAT: so tay tron ============

    [Fact]
    public void TachVat_8PhanTram_TinhDung()
    {
        // 1.080.000đ gom VAT 8% -> truoc thue 1.000.000, thue 80.000.
        var (truocThue, tienVat) = VatService.TachVat(1_080_000, 0.08m);
        Assert.Equal(1_000_000, truocThue);
        Assert.Equal(80_000, tienVat);
    }

    [Fact]
    public void TachVat_10PhanTram_TinhDung()
    {
        // 1.100.000đ gom VAT 10% -> truoc thue 1.000.000, thue 100.000.
        var (truocThue, tienVat) = VatService.TachVat(1_100_000, 0.10m);
        Assert.Equal(1_000_000, truocThue);
        Assert.Equal(100_000, tienVat);
    }

    // ============ Thue suat doi theo ngay hieu luc ============

    [Fact]
    public void LayThueSuat_TruocVaSauCuaSo_DungSuat()
    {
        var vat = TaoVat();
        // Truoc 01/07/2025: chua giam -> 10% (mac dinh).
        Assert.Equal(0.10m, vat.LayThueSuat("test-cat-8", new DateTime(2025, 6, 30)));
        // Trong cua so: 8%.
        Assert.Equal(0.08m, vat.LayThueSuat("test-cat-8", new DateTime(2025, 7, 1)));
        Assert.Equal(0.08m, vat.LayThueSuat("test-cat-8", new DateTime(2026, 12, 31)));
        // Sau 31/12/2026: het giam -> 10%.
        Assert.Equal(0.10m, vat.LayThueSuat("test-cat-8", new DateTime(2027, 1, 1)));
        // Danh muc khong co rule -> 10% mac dinh, ca trong cua so.
        Assert.Equal(0.10m, vat.LayThueSuat("khong-co-rule", new DateTime(2026, 1, 1)));
        Assert.Equal(0.10m, vat.LayThueSuat(null, new DateTime(2026, 1, 1)));
    }

    // ============ Hoa don DN: thieu thong tin -> tu choi ============

    [Fact]
    public void KiemTraHoaDon_ThieuThongTin_TuChoi()
    {
        var vat = TaoVat();
        Assert.Contains("thiếu", vat.KiemTraHoaDonDoanhNghiep(true, null, "0312345678", "HN")!);
        Assert.Contains("thiếu", vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "", "HN")!);
        Assert.Contains("thiếu", vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "0312345678", "  ")!);
        Assert.Contains("không hợp lệ", vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "123", "HN")!);
        Assert.Contains("không hợp lệ", vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "03123456789", "HN")!);
        Assert.Null(vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "0312345678", "HN"));
        Assert.Null(vat.KiemTraHoaDonDoanhNghiep(true, "Cty A", "0312345678-001", "HN"));
        Assert.Null(vat.KiemTraHoaDonDoanhNghiep(false, null, null, null));
        Assert.Contains("demo", VatService.GhiChuDemo);
    }

    // ============ Checkout: 8% + 10% chung don, tong khong doi ============

    [Fact]
    public async Task Checkout_HonHop8Va10_TongKhongDoi_SnapshotDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp8 = await SeedProductAsync(services, "SP 8%", 1_080_000, "test-cat-8", tonKho: 5);
            var sp10 = await SeedProductAsync(services, "SP 10%", 1_100_000, "test-cat-10", tonKho: 5);

            var ketQua = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                NgayDat = new DateTime(2026, 1, 15),
                Lines =
                {
                    new CheckoutLineInput(sp8, null, 1, 1_080_000),
                    new CheckoutLineInput(sp10, null, 1, 1_100_000),
                },
                IdempotencyKey = "VAT-MIX-1",
            });
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            var don = ketQua.DonHang!;
            _out.WriteLine($"TongTien={don.TongTien} TongVat={don.TongVat}");

            // Tong khach tra GIU NGUYEN (gia da gom VAT, khong cong them):
            // 1.080.000 + 1.100.000 = 2.180.000; phi ship 0 (don >= 2tr).
            Assert.Equal(2_180_000, don.TongTien);
            Assert.Equal(2_180_000, don.ThanhToan);
            // Snapshot: dong 8% -> thue 80.000; dong 10% -> thue 100.000.
            var d8 = don.ChiTiet.First(d => d.ProductId == sp8);
            var d10 = don.ChiTiet.First(d => d.ProductId == sp10);
            Assert.Equal(0.08m, d8.ThueSuatVat);
            Assert.Equal(80_000, d8.TienVat);
            Assert.Equal(0.10m, d10.ThueSuatVat);
            Assert.Equal(100_000, d10.TienVat);
            Assert.Equal(180_000, don.TongVat);

            // Doc lai tu DB: snapshot con nguyen + bang tinh cong ra tong.
            var donDocLai = await DocDonAsync(services, don.Id);
            Assert.Equal(180_000, donDocLai.TongVat);
            using var scope = services.CreateScope();
            var vat = scope.ServiceProvider.GetRequiredService<VatService>();
            var bang = vat.TinhVatDon(donDocLai);
            Assert.Equal(2_180_000, bang.TamTinhGoc);
            Assert.Equal(0, bang.TongGiam);
            Assert.Equal(2_000_000, bang.TruocThue);
            Assert.Equal(180_000, bang.TongVat);
            Assert.Equal(2_180_000, bang.TongThanhToan);
            Assert.Equal(2_180_000, donDocLai.ThanhToan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Snapshot giu suat cu khi cau hinh doi ============

    [Fact]
    public async Task Checkout_DoiSuatSauKhiDat_SnapshotGiuSuatCu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp = await SeedProductAsync(services, "SP 8%", 1_080_000, "test-cat-8", tonKho: 5);

            var ketQua = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                NgayDat = new DateTime(2026, 1, 15),
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-SNAP-1",
            });
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            var donId = ketQua.DonHang!.Id;

            // Gia su cau hinh doi: test-cat-8 tu 8% len 10% — don cu phai giu 8%.
            var don = await DocDonAsync(services, donId);
            var vatMoi = new VatService(Options.Create(new VatOptions
            {
                GiaDaGomVat = true,
                ThueSuatMacDinh = 0.10m,
                Rules =
                {
                    new VatRuleConfig
                    {
                        CategorySlug = "test-cat-8", ThueSuat = 0.10m,
                        HieuLucTu = new DateTime(2025, 7, 1), HieuLucDen = new DateTime(2026, 12, 31),
                    },
                },
            }));
            var bang = vatMoi.TinhVatDon(don);
            Assert.Equal(0.08m, bang.Lines[0].ThueSuat);
            Assert.Equal(80_000, bang.TongVat);
            // Sau giam 1.080.000 < 2tr -> phi ship 30.000, tong = 1.110.000.
            Assert.Equal(30_000, bang.PhiShip);
            Assert.Equal(1_110_000, bang.TongThanhToan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Don truoc ngay doi suat dung suat cu (10%), sau ngay dung suat moi (8%) ============

    [Fact]
    public async Task Checkout_TruocVaSauNgayDoiSuat_SnapshotKhacNhau()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp = await SeedProductAsync(services, "SP 8%", 1_080_000, "test-cat-8", tonKho: 5);

            var donCu = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                NgayDat = new DateTime(2025, 6, 15), // truoc 01/07/2025 -> 10%
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-DATE-1",
            });
            Assert.True(donCu.ThanhCong, donCu.Loi);
            Assert.Equal(0.10m, donCu.DonHang!.ChiTiet.First().ThueSuatVat);
            // Gia da gom VAT 10% -> thue = 1.080.000*10/110 = 98.182 (lam tron).
            Assert.Equal(98_182, donCu.DonHang!.TongVat);
            Assert.Equal(1_080_000, donCu.DonHang!.TongTien);

            var donMoi = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                NgayDat = new DateTime(2025, 7, 15), // sau 01/07/2025 -> 8%
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-DATE-2",
            });
            Assert.True(donMoi.ThanhCong, donMoi.Loi);
            Assert.Equal(0.08m, donMoi.DonHang!.ChiTiet.First().ThueSuatVat);
            Assert.Equal(80_000, donMoi.DonHang!.TongVat);
            Assert.Equal(1_080_000, donMoi.DonHang!.TongTien);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Checkout: giam gia roi moi tach VAT; tong van khop ============

    [Fact]
    public async Task Checkout_CoGiamGia_VatTinhTrenPhanSauGiam_TongKhop()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp = await SeedProductAsync(services, "SP 8%", 1_080_000, "test-cat-8", tonKho: 5);
            // Giam 80.000 bang voucher THAT (co dinh, khong gioi han hang/
            // danh muc): tu Sprint 2 checkout KHONG tin GiamGia client gui
            // len nua ma tinh lai tu DB — test cu gan GiamGia truc tiep se
            // bi bo qua, phai dung MaVoucher.
            await SeedVoucherAsync(services, "VATTEST80", 80_000);

            var ketQua = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                NgayDat = new DateTime(2026, 1, 15),
                MaVoucher = "VATTEST80", // voucher 80.000 -> sau giam 1.000.000
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-DISC-1",
            });
            Assert.True(ketQua.ThanhCong, ketQua.Loi);
            var don = ketQua.DonHang!;
            Assert.Equal(1_000_000, don.TongTien);
            // VAT tren 1.000.000 gom 8% -> 74.074 (1.000.000*8/108).
            Assert.Equal(74_074, don.TongVat);

            var donDocLai = await DocDonAsync(services, don.Id);
            using var scope = services.CreateScope();
            var vat = scope.ServiceProvider.GetRequiredService<VatService>();
            var bang = vat.TinhVatDon(donDocLai);
            Assert.Equal(1_080_000, bang.TamTinhGoc);
            Assert.Equal(80_000, bang.TongGiam);
            Assert.Equal(1_000_000, bang.SauGiam);
            Assert.Equal(925_926, bang.TruocThue);
            Assert.Equal(74_074, bang.TongVat);
            Assert.Equal(bang.TruocThue + bang.TongVat, bang.SauGiam);
            // Phi ship 30.000 (sau giam < 2tr): tong thanh toan = 1.030.000.
            Assert.Equal(30_000, bang.PhiShip);
            Assert.Equal(1_030_000, bang.TongThanhToan);
            Assert.Equal(1_030_000, donDocLai.ThanhToan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Checkout: tick VAT thieu thong tin -> tu choi, khong tao don ============

    [Fact]
    public async Task Checkout_XuatHoaDonThieuThongTin_TuChoi_KhongTaoDon()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var sp = await SeedProductAsync(services, "SP 8%", 1_080_000, "test-cat-8", tonKho: 5);

            var ketQua = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                XuatHoaDon = true, TenCongTy = "Cty A", MaSoThue = null, DiaChiCongTy = "HN",
                NgayDat = new DateTime(2026, 1, 15),
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-REJ-1",
            });
            Assert.False(ketQua.ThanhCong);
            Assert.Contains("mã số thuế", ketQua.Loi);
            Assert.Equal(0, await CountOrdersAsync(services));
            Assert.Equal(5, await GetTonKhoAsync(services, sp));

            // Du thong tin thi dat duoc, hoa don luu du ten/MST/dia chi.
            var ok = await CheckoutAsync(services, new CheckoutInput
            {
                HoTenNhan = "Test", SoDienThoai = "0900000000", DiaChi = "Test",
                XuatHoaDon = true, TenCongTy = "Cty A", MaSoThue = "0312345678", DiaChiCongTy = "HN",
                NgayDat = new DateTime(2026, 1, 15),
                Lines = { new CheckoutLineInput(sp, null, 1, 1_080_000) },
                IdempotencyKey = "VAT-REJ-2",
            });
            Assert.True(ok.ThanhCong, ok.Loi);
            Assert.Equal("Cty A", ok.DonHang!.VatTenCongTy);
            Assert.Equal("0312345678", ok.DonHang!.VatMaSoThue);
            Assert.Equal("HN", ok.DonHang!.VatDiaChiCongTy);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ Don cu khong snapshot: suy lai tu danh muc + ngay dat ============

    [Fact]
    public void TinhVatDon_DonCuKhongSnapshot_SuyLaiDungSuat()
    {
        var vat = TaoVat();
        var order = new Order
        {
            NgayDat = new DateTime(2026, 1, 15),
            PhiShip = 0,
            ChiTiet =
            {
                // Don cu: ThueSuatVat = 0 (chua snapshot) -> suy tu danh muc.
                new OrderDetail
                {
                    DonGia = 1_080_000, SoLuong = 1,
                    Product = new Product
                    {
                        Ten = "SP cu", Gia = 1_080_000,
                        Category = new Category { Ten = "Test 8", Slug = "test-cat-8" },
                    },
                },
            },
        };
        var bang = vat.TinhVatDon(order);
        Assert.Equal(0.08m, bang.Lines[0].ThueSuat);
        Assert.Equal(80_000, bang.TongVat);
        Assert.Equal(1_000_000, bang.TruocThue);
    }

    // Sprint 4 — QA P1: VAT don combo phai tinh tren so tien SAU khi tru
    // giam combo (truoc day TinhVatDon quen GiamGiaCombo nen tong + VAT
    // cao hon so tien khach tra that dung bang giam combo).
    [Fact]
    public void TinhVatDon_GiamGiaCombo_TruTruocKhiTachVat()
    {
        var vat = TaoVat();
        // Don 1 dong 13.798.400đ (8%), giam combo 1.408.000đ -> sau giam
        // 12.390.400đ; VAT = round(12.390.400 x 8/108) = 917.807đ
        // (truoc fix: VAT tren 13.798.400đ = 1.022.104đ, tong 13.798.400đ).
        var order = new Order
        {
            NgayDat = new DateTime(2026, 1, 15),
            PhiShip = 0,
            GiamGiaCombo = 1_408_000,
            ChiTiet =
            {
                new OrderDetail
                {
                    DonGia = 13_798_400, SoLuong = 1, ThueSuatVat = 0.08m,
                },
            },
        };
        var bang = vat.TinhVatDon(order);
        Assert.Equal(13_798_400, bang.TamTinhGoc);
        Assert.Equal(1_408_000, bang.TongGiam);
        Assert.Equal(12_390_400, bang.SauGiam);
        Assert.Equal(12_390_400, bang.TongThanhToan);
        Assert.Equal(917_807, bang.TongVat);
        Assert.Equal(11_472_593, bang.TruocThue);
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
        services.Configure<VatOptions>(o =>
        {
            o.GiaDaGomVat = true;
            o.ThueSuatMacDinh = 0.10m;
            o.Rules = TestOptions().Rules;
        });
        services.AddScoped<VatService>();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>(); // Sprint 2 merge: CheckoutService can ca 2
        services.AddScoped<NotificationService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>(); // Sprint 2 — Mang 4: OrderService can RefundService
        services.AddScoped<ReturnService>(); // Sprint 2 — Mang 4: duyet doi tra
        return services.BuildServiceProvider();
    }

    private static async Task<int> SeedProductAsync(ServiceProvider services, string ten, decimal gia,
        string categorySlug, int tonKho)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        var category = await ctx.Categories.FirstOrDefaultAsync(c => c.Slug == categorySlug);
        if (category is null)
        {
            category = new Category { Ten = categorySlug, Slug = categorySlug };
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
            Ten = ten, Gia = gia, CategoryId = category.Id, BrandId = brand.Id,
            TonKho = tonKho, LuotBan = 0,
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>Seed voucher giam tien co dinh (khong gioi han hang/danh muc).</summary>
    private static async Task SeedVoucherAsync(ServiceProvider services, string ma, decimal giaTri)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Vouchers.Add(new Voucher
        {
            Ma = ma, LoaiGiam = "Tien", GiaTri = giaTri, DieuKien = 0,
            NgayHetHan = new DateTime(2027, 12, 31), SoLuong = 10, DaDung = 0,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<CheckoutResult> CheckoutAsync(ServiceProvider services, CheckoutInput input)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(input);
    }

    private static async Task<Order> DocDonAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product).ThenInclude(p => p.Category)
            .FirstAsync(o => o.Id == orderId);
    }

    private static async Task<int> CountOrdersAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.CountAsync();
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
