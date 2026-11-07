using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
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
/// Test Phase 4 — Product &amp; Cart (Sprint 6 Continuation, Module 7).
/// Theo mẫu ComboTests / CartCheckoutParityTests / CheckoutTamperingTests:
/// controller thật + SQLite tạm (WAL), không Moq. Mỗi test 1 file DB tạm.
/// Kiểm chứng: parity giá variant (data-gia = giá server khi thêm giỏ, cả
/// flash sale), combo 3 món, Remove có TempData đúng câu, DiemSeTich =
/// TongCong/100000 làm tròn xuống, checkout cũ→mới (GiaCu/GiaMoi/ChenhLech),
/// và quy tắc guest KHÔNG tích điểm (căn cứ sửa text guest ở Cart/Index).
/// </summary>
[Collection("DbTests")]
public class Phase4ProductCartTests
{
    private readonly ITestOutputHelper _out;

    public Phase4ProductCartTests(ITestOutputHelper output) => _out = output;

    // ============ 1. Parity giá variant (không flash sale) ============

    [Fact]
    public async Task Variant_Xiaomi512GB_DataGia_BangGiaServerKhiThemGio()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, iphoneId, _, var512Xiaomi, var512Iphone) =
                await SeedXiaomiIphoneVoiVariantAsync(services);
            var now = DateTime.Now;
            Dictionary<int, GiaApDungResult> giaTheoVariant;
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoProductsController(services, scope);
                var ketQua = await ctrl.Details(xiaomiId);
                var view = Assert.IsType<ViewResult>(ketQua);
                giaTheoVariant = Assert.IsType<Dictionary<int, GiaApDungResult>>(
                    ctrl.ViewBag.GiaTheoVariant);
                Assert.IsType<Product>(view.Model);
            }

            // Xiaomi 15: 12.990.000đ + 512GB (+3.000.000đ) = 15.990.000đ.
            Assert.Equal(15_990_000m, giaTheoVariant[var512Xiaomi].DonGia);
            // QA Phase 4 (fix): data-gia-goc = giá NIÊM YẾT + GiaThem
            // (Gia 15.990.000 + 3.000.000), không phải GiaBan + GiaThem —
            // để JS hiện đúng giá gạch của SP đang sale.
            Assert.Equal(18_990_000m, giaTheoVariant[var512Xiaomi].GiaGoc);
            // iPhone 15 Pro: 28.990.000đ + 512GB (+4.000.000đ) = 32.990.000đ.
            // (Details iPhone gọi riêng vì mỗi lần gọi là 1 SP.)
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoProductsController(services, scope);
                await ctrl.Details(iphoneId);
                var dictIphone = Assert.IsType<Dictionary<int, GiaApDungResult>>(
                    ctrl.ViewBag.GiaTheoVariant);
                Assert.Equal(32_990_000m, dictIphone[var512Iphone].DonGia);
            }

            // Đối chiếu từng variant với TinhGiaAsync (cùng hàm khi thêm giỏ).
            using var scope2 = services.CreateScope();
            var gia = scope2.ServiceProvider.GetRequiredService<PriceCalculationService>();
            Assert.Equal(
                (await gia.TinhGiaAsync(xiaomiId, 3_000_000, 1, now)).DonGia,
                giaTheoVariant[var512Xiaomi].DonGia);
            _out.WriteLine($"Xiaomi 512GB data-gia={giaTheoVariant[var512Xiaomi].DonGia:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 2. Parity giá variant khi đang Flash Sale ============

    [Fact]
    public async Task Variant_FlashSale_DonGia_BangGiaSaleCongGiaThem()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, var256Xiaomi, var512Xiaomi, _) =
                await SeedXiaomiIphoneVoiVariantAsync(services);
            var now = DateTime.Now;
            await SeedFlashSaleAsync(services, xiaomiId, 11_990_000,
                now.AddHours(-1), now.AddHours(1), gioiHan: 20, daBan: 0);

            Dictionary<int, GiaApDungResult> giaTheoVariant;
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoProductsController(services, scope);
                await ctrl.Details(xiaomiId);
                giaTheoVariant = Assert.IsType<Dictionary<int, GiaApDungResult>>(
                    ctrl.ViewBag.GiaTheoVariant);
            }

            // DonGia = GiaSale + GiaThem (xem PriceCalculationService).
            Assert.Equal(11_990_000m, giaTheoVariant[var256Xiaomi].DonGia);
            Assert.Equal(14_990_000m, giaTheoVariant[var512Xiaomi].DonGia);
            // GiaGoc (gạch) = giá NIÊM YẾT + GiaThem (QA Phase 4 fix):
            // 256GB → 15.990.000đ, 512GB → 18.990.000đ (không phải
            // GiaBan + GiaThem = 12.990.000/15.990.000 như TinhGiaAsync).
            Assert.Equal(15_990_000m, giaTheoVariant[var256Xiaomi].GiaGoc);
            Assert.Equal(18_990_000m, giaTheoVariant[var512Xiaomi].GiaGoc);

            using var scope2 = services.CreateScope();
            var gia = scope2.ServiceProvider.GetRequiredService<PriceCalculationService>();
            Assert.Equal(
                (await gia.TinhGiaAsync(xiaomiId, 3_000_000, 1, now)).DonGia,
                giaTheoVariant[var512Xiaomi].DonGia);
            _out.WriteLine($"Flash sale: 256GB={giaTheoVariant[var256Xiaomi].DonGia:N0} 512GB={giaTheoVariant[var512Xiaomi].DonGia:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 3. Combo 3 món ============

    [Fact]
    public async Task Combo_BaMon_Details_HienDuBaMon_TongGiamKhopService()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b, c) = await SeedBaSanPhamAsync(services);
            var comboId = await SeedComboAsync(services,
                new[] { (a, 1), (b, 1), (c, 2) }, phanTram: 10);
            var now = DateTime.Now;

            List<CartLineViewModel> comboMon;
            decimal tongBo, giamGia;
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoProductsController(services, scope);
                await ctrl.Details(a);
                comboMon = Assert.IsType<List<CartLineViewModel>>(ctrl.ViewBag.ComboMon);
                tongBo = Assert.IsType<decimal>(ctrl.ViewBag.ComboTongBo);
                giamGia = Assert.IsType<decimal>(ctrl.ViewBag.ComboGiamGia);
            }

            Assert.Equal(3, comboMon.Count);
            // Món chính lên đầu, các món kèm giữ thứ tự combo.
            Assert.Equal(a, comboMon[0].Product.Id);
            Assert.Equal(b, comboMon[1].Product.Id);
            Assert.Equal(c, comboMon[2].Product.Id);
            Assert.Equal(2, comboMon[2].SoLuong); // "× 2" ở view
            Assert.Equal(100_000m, comboMon[0].DonGia);
            Assert.Equal(50_000m, comboMon[1].DonGia);
            Assert.Equal(200_000m, comboMon[2].DonGia);

            // Tổng/giảm khớp TinhGiaComboAsync (cùng hàm checkout dùng).
            var lines = comboMon.Select(m => new CartLineViewModel
            {
                Product = m.Product, Variant = null, SoLuong = m.SoLuong,
            }).ToList();
            using var scope2 = services.CreateScope();
            var gia = scope2.ServiceProvider.GetRequiredService<PriceCalculationService>();
            var kq = await gia.TinhGiaComboAsync(comboId, lines, now);
            Assert.True(kq.HopLe, kq.Loi);
            Assert.Equal(kq.TongTienBo, tongBo);
            Assert.Equal(kq.GiamGia, giamGia);
            // 100k + 50k + 200k×2 = 550k; giảm 10% = 55k.
            Assert.Equal(550_000m, tongBo);
            Assert.Equal(55_000m, giamGia);
            _out.WriteLine($"Combo 3 món: tongBo={tongBo:N0} giam={giamGia:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 4. Combo 2 món vẫn như cũ ============

    [Fact]
    public async Task Combo_HaiMon_VanHienDuHaiMon_NhuCu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, b) = await SeedHaiSanPhamAsync(services);
            var comboId = await SeedComboAsync(services,
                new[] { (a, 1), (b, 1) }, phanTram: 10);

            List<CartLineViewModel> comboMon;
            using (var scope = services.CreateScope())
            {
                var ctrl = TaoProductsController(services, scope);
                await ctrl.Details(a);
                comboMon = Assert.IsType<List<CartLineViewModel>>(ctrl.ViewBag.ComboMon);
            }

            Assert.Equal(2, comboMon.Count);
            Assert.Equal(a, comboMon[0].Product.Id);
            Assert.Equal(b, comboMon[1].Product.Id);
            Assert.Equal(150_000m, comboMon.Sum(m => m.DonGia * m.SoLuong));
            _out.WriteLine($"Combo 2 món: {comboMon[0].Product.Ten} + {comboMon[1].Product.Ten}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 5. Remove có TempData đúng câu ============

    [Fact]
    public async Task Cart_Remove_CoTempDataDungCau_VaXoaDong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (a, _) = await SeedHaiSanPhamAsync(services);
            var h = new CartHarness(services);
            try
            {
                h.DatGioVaoSession(new List<CartLine>
                    { new() { ProductId = a, VariantId = null, SoLuong = 2 } });
                var (ketQua, tempData) = await h.RemoveAsync(a, null);
                Assert.IsType<RedirectToActionResult>(ketQua);
                Assert.Equal("Đã xóa SP Combo A khỏi giỏ hàng.", tempData["Success"]);
                Assert.Empty(h.DocGio());
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Cart_Remove_SPKhongConTrongDB_CauChung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedHaiSanPhamAsync(services);
            var h = new CartHarness(services);
            try
            {
                h.DatGioVaoSession(new List<CartLine>
                    { new() { ProductId = 999, VariantId = null, SoLuong = 1 } });
                var (_, tempData) = await h.RemoveAsync(999, null);
                Assert.Equal("Đã xóa sản phẩm khỏi giỏ hàng.", tempData["Success"]);
                Assert.Empty(h.DocGio());
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 6. Điểm sẽ tích = TongCong / 100000 làm tròn xuống ============

    [Fact]
    public async Task Cart_DiemSeTich_BangTongCongChia100000_LamTronXuong()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, _, _, _) = await SeedXiaomiIphoneVoiVariantAsync(services);
            var h = new CartHarness(services);
            try
            {
                // Guest: Xiaomi 12.990.000đ → 129 điểm (12.990.000/100.000 = 129,9 → 129).
                h.DatGioVaoSession(new List<CartLine>
                    { new() { ProductId = xiaomiId, VariantId = null, SoLuong = 1 } });
                var (viewBag, _) = await h.IndexAsync();
                Assert.Equal(129, viewBag["DiemSeTich"]);
                Assert.False(viewBag.ContainsKey("DiemHienTai")); // guest: không có số dư
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Cart_DiemSeTich_DaDangNhap_HienCaDiemHienTai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, _, _, _) = await SeedXiaomiIphoneVoiVariantAsync(services);
            await SeedUserVoiDiemAsync(services, "user-p4", "khach-p4", diem: 50);
            var h = new CartHarness(services);
            try
            {
                h.DangNhap("khach-p4");
                h.DatGioVaoSession(new List<CartLine>
                    { new() { ProductId = xiaomiId, VariantId = null, SoLuong = 1 } });
                var (viewBag, _) = await h.IndexAsync();
                Assert.Equal(129, viewBag["DiemSeTich"]);
                Assert.Equal(50, viewBag["DiemHienTai"]);
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 7. Guest KHÔNG tích điểm thật (căn cứ sửa text guest) ============

    [Fact]
    public async Task Guest_KhongTichDiem_GhiDiemMuaHang_BoQua()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedHaiSanPhamAsync(services);

            // Đơn guest (UserId null), tổng 12.990.000đ → lẽ ra 129 điểm nếu có user.
            var donGuest = await TaoDonAsync(services, userId: null, tongTien: 12_990_000m);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                await loyalty.GhiDiemMuaHangAsync(donGuest);
            }
            Assert.Equal(0, await DemGiaoDichDiemAsync(services));

            // Cùng tổng đó, đơn có user → ghi đủ 129 điểm.
            await SeedUserVoiDiemAsync(services, "user-p4", "khach-p4", diem: 0);
            var donUser = await TaoDonAsync(services, userId: "user-p4", tongTien: 12_990_000m);
            using (var scope = services.CreateScope())
            {
                var loyalty = scope.ServiceProvider.GetRequiredService<LoyaltyService>();
                await loyalty.GhiDiemMuaHangAsync(donUser);
            }
            Assert.Equal(1, await DemGiaoDichDiemAsync(services));
            using var scope2 = services.CreateScope();
            var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var gd = await ctx.PointTransactions.FirstAsync();
            Assert.Equal(129, gd.Diem);
            Assert.Equal("Earn", gd.Loai);
            _out.WriteLine("Guest: 0 điểm (UserId null → return sớm); user: 129 điểm");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 8. TongCongTuAnh tái tạo đúng tổng GET ============

    [Fact]
    public async Task Checkout_TongCongTuAnh_BangTongCongKetQua()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, _, _, _) = await SeedXiaomiIphoneVoiVariantAsync(services);
            var sp100k = await SeedProductAsync(services, "SP 100k", 100_000, tonKho: 10);

            // Đơn lớn (≥2tr sau giảm): ship 0 → TongCongTuAnh == TongCong.
            var tongLon = await TinhTongDonAsync(services, (xiaomiId, 1), userId: null);
            Assert.Equal(tongLon.TongCong,
                CheckoutGiaHelper.TongCongTuAnh(CheckoutGiaHelper.ChupTu(tongLon)));
            Assert.Equal(12_990_000m, tongLon.TongCong);

            // Đơn nhỏ 100k: ship 30k → TongCongTuAnh == 130.000đ.
            var tongNho = await TinhTongDonAsync(services, (sp100k, 1), userId: null);
            Assert.Equal(130_000m, tongNho.TongCong);
            Assert.Equal(130_000m,
                CheckoutGiaHelper.TongCongTuAnh(CheckoutGiaHelper.ChupTu(tongNho)));

            // Voucher 500k trên Xiaomi: tổng sau giảm vẫn khớp ảnh.
            await SeedVoucherAsync(services, "TECHZONE500", "Tien", 500_000, 0);
            var tongVoucher = await TinhTongDonAsync(services, (xiaomiId, 1),
                userId: null, maVoucher: "TECHZONE500");
            Assert.Equal(12_490_000m, tongVoucher.TongCong);
            Assert.Equal(tongVoucher.TongCong,
                CheckoutGiaHelper.TongCongTuAnh(CheckoutGiaHelper.ChupTu(tongVoucher)));
            _out.WriteLine($"TongCongTuAnh khớp: lon={tongLon.TongCong:N0} nho={tongNho.TongCong:N0} voucher={tongVoucher.TongCong:N0}");
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ 9. Checkout cũ→mới qua controller thật ============

    [Fact]
    public async Task Checkout_GiaDoi_GiuaGetVaPost_TraVeXacNhanLaiGia_VoiGiaCuMoiChenhLech()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, _, _, _) = await SeedXiaomiIphoneVoiVariantAsync(services);
            var h = new CheckoutHarness(services);
            try
            {
                h.DatGioVaoSession(xiaomiId, 1);
                var vmGet = await h.GetCheckoutAsync();
                Assert.NotNull(vmGet);

                // Giá đổi giữa GET và POST: GiaKhuyenMai 12.990.000 → 13.990.000.
                await MutateAsync(services, async ctx =>
                {
                    var sp = await ctx.Products.FirstAsync(p => p.Id == xiaomiId);
                    sp.GiaKhuyenMai = 13_990_000m;
                });

                var model = new CheckoutViewModel
                {
                    HoTenNhan = "Test",
                    SoDienThoai = "0900000000",
                    DiaChi = "Test",
                    PhuongThucTT = "COD",
                    IdempotencyKey = vmGet!.IdempotencyKey,
                };
                var (ketQua, tempData) = await h.PostCheckoutAsync(model);
                var view = Assert.IsType<ViewResult>(ketQua);
                Assert.True(h.ControllerViewBag(view, "GiaDaThayDoi"));
                Assert.Equal(12_990_000m, h.ControllerViewBagDecimal(view, "GiaCu"));
                Assert.Equal(13_990_000m, h.ControllerViewBagDecimal(view, "GiaMoi"));
                Assert.Equal(1_000_000m, h.ControllerViewBagDecimal(view, "ChenhLech"));
                Assert.Equal("Giá đã thay đổi, vui lòng xác nhận lại.", tempData["Error"]);
                Assert.Equal(0, await CountOrdersAsync(services)); // chưa tạo đơn
                _out.WriteLine("GiaCu=12.990.000 GiaMoi=13.990.000 ChenhLech=+1.000.000, 0 đơn");
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Checkout_GiaKhongDoi_PostDatHangBinhThuong_KhongCoGiaCuMoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (xiaomiId, _, _, _, _) = await SeedXiaomiIphoneVoiVariantAsync(services);
            var h = new CheckoutHarness(services);
            try
            {
                h.DatGioVaoSession(xiaomiId, 1);
                var vmGet = await h.GetCheckoutAsync();
                var model = new CheckoutViewModel
                {
                    HoTenNhan = "Test",
                    SoDienThoai = "0900000000",
                    DiaChi = "Test",
                    PhuongThucTT = "COD",
                    IdempotencyKey = vmGet!.IdempotencyKey,
                };
                var (ketQua, _) = await h.PostCheckoutAsync(model);
                // Đặt thành công → redirect về Details đơn (không qua banner giá đổi).
                var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
                Assert.Equal("Details", redirect.ActionName);
                Assert.Equal(1, await CountOrdersAsync(services));
            }
            finally
            {
                h.Dispose();
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Harness / helpers =================

    private sealed class CartHarness : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly DefaultHttpContext _http = new();
        private readonly FakeSession _session = new();

        public CartHarness(ServiceProvider services)
        {
            _services = services;
            _http.Session = _session;
            // CartService resolve IHttpContextAccessor từ DI: phải set
            // HttpContext lên đúng singleton đã đăng ký, không tạo mới.
            var accessor = services.GetRequiredService<IHttpContextAccessor>()
                as TestHttpContextAccessor;
            accessor!.HttpContext = _http;
            _http.RequestServices = services;
        }

        public void DangNhap(string userName) =>
            _http.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) }, "Test"));

        public void DatGioVaoSession(List<CartLine> lines) =>
            _session.SetString("TechZone.Cart", JsonSerializer.Serialize(lines));

        public List<CartLine> DocGio() =>
            JsonSerializer.Deserialize<List<CartLine>>(
                _session.GetString("TechZone.Cart") ?? "[]") ?? new List<CartLine>();

        public async Task<(Dictionary<string, object?> ViewBag, CartViewModel Model)> IndexAsync()
        {
            using var scope = _services.CreateScope();
            var ctrl = TaoCartController(scope);
            var ketQua = await ctrl.Index();
            var view = Assert.IsType<ViewResult>(ketQua);
            var vb = new Dictionary<string, object?>
            {
                ["DiemSeTich"] = ctrl.ViewBag.DiemSeTich,
            };
            if (ctrl.ViewBag.DiemHienTai is not null)
                vb["DiemHienTai"] = ctrl.ViewBag.DiemHienTai;
            return (vb, Assert.IsType<CartViewModel>(view.Model));
        }

        public async Task<(IActionResult Result, ITempDataDictionary TempData)> RemoveAsync(
            int productId, int? variantId)
        {
            using var scope = _services.CreateScope();
            var ctrl = TaoCartController(scope);
            var ketQua = await ctrl.Remove(productId, variantId);
            return (ketQua, ctrl.TempData);
        }

        private CartController TaoCartController(IServiceScope scope)
        {
            var sp = scope.ServiceProvider;
            var ctrl = new CartController(
                sp.GetRequiredService<CartService>(),
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<VoucherService>(),
                sp.GetRequiredService<LoyaltyService>())
            {
                ControllerContext = TaoControllerContext(_http),
                TempData = new TempDataDictionary(_http, new NoOpTempDataProvider()),
            };
            return ctrl;
        }

        public void Dispose() { }
    }

    /// <summary>Mô phỏng 1 "request" HTTP = 1 scope riêng trên cùng file DB +
    /// cùng HttpContext/session (mẫu CheckoutTamperingTests.Harness).</summary>
    private sealed class CheckoutHarness : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly DefaultHttpContext _http = new();
        private readonly FakeSession _session = new();

        public CheckoutHarness(ServiceProvider services)
        {
            _services = services;
            _http.Session = _session;
            var accessor = services.GetRequiredService<IHttpContextAccessor>()
                as TestHttpContextAccessor;
            accessor!.HttpContext = _http;
            _http.RequestServices = services;
        }

        public void DatGioVaoSession(int productId, int soLuong = 1) =>
            _session.SetString("TechZone.Cart", JsonSerializer.Serialize(new List<CartLine>
                { new() { ProductId = productId, VariantId = null, SoLuong = soLuong } }));

        private OrdersController TaoController(IServiceScope scope)
        {
            var sp = scope.ServiceProvider;
            return new OrdersController(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<CartService>(),
                sp.GetRequiredService<LoyaltyService>(),
                sp.GetRequiredService<CheckoutService>(),
                sp.GetRequiredService<OrderService>(),
                sp.GetRequiredService<VatService>(),
                sp.GetRequiredService<PriceCalculationService>())
            {
                ControllerContext = TaoControllerContext(_http),
                TempData = new TempDataDictionary(_http, new NoOpTempDataProvider()),
            };
        }

        public async Task<CheckoutViewModel?> GetCheckoutAsync()
        {
            using var scope = _services.CreateScope();
            var result = await TaoController(scope).Checkout() as ViewResult;
            Assert.NotNull(result);
            return (CheckoutViewModel)result!.Model!;
        }

        public async Task<(IActionResult Result, ITempDataDictionary TempData)> PostCheckoutAsync(
            CheckoutViewModel model)
        {
            using var scope = _services.CreateScope();
            var ctrl = TaoController(scope);
            var result = await ctrl.Checkout(model);
            return (result, ctrl.TempData);
        }

        public bool ControllerViewBag(ViewResult view, string key)
        {
            // ViewBag của controller đã render xong không đọc lại được từ
            // ViewResult; giá trị được kiểm chứng qua ViewData của view.
            return view.ViewData[key] is true;
        }

        public decimal ControllerViewBagDecimal(ViewResult view, string key) =>
            Assert.IsType<decimal>(view.ViewData[key]);

        public void Dispose() { }
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = new();
        public string Id => "test-session";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _data.Keys;
        public void Clear() => _data.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_data.TryGetValue(key, out var v)) { value = v; return true; }
            value = Array.Empty<byte>();
            return false;
        }
    }

    private static ProductsController TaoProductsController(
        ServiceProvider services, IServiceScope scope)
    {
        var http = new DefaultHttpContext();
        http.Session = new FakeSession();
        http.RequestServices = services;
        return new ProductsController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new TestWebHostEnvironment(),
            scope.ServiceProvider.GetRequiredService<PriceCalculationService>())
        {
            ControllerContext = TaoControllerContext(http),
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
    }

    /// <summary>ControllerContext đủ RouteData + ActionDescriptor để
    /// RedirectToAction (dùng UrlHelper) không NRE trong test.</summary>
    private static ControllerContext TaoControllerContext(HttpContext http) =>
        new()
        {
            HttpContext = http,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor(),
        };

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
        services.AddSingleton<IHttpContextAccessor, TestHttpContextAccessor>();
        services.AddControllersWithViews(); // IUrlHelperFactory + ITempDataDictionaryFactory cho RedirectToAction/ViewBag
        services.AddScoped<LoyaltyService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        services.AddScoped<CartService>();
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
        await EnsureDbAsync(services);
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
        await EnsureDbAsync(services);
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
        decimal gia, int tonKho, decimal? giaKhuyenMai = null)
    {
        var categoryId = await SeedCategoryAsync(services);
        var brandId = await SeedBrandAsync(services, "Test Brand");
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

    /// <summary>Xiaomi 15 (GiaBan 12.990.000đ) + iPhone 15 Pro (28.990.000đ),
    /// mỗi máy 2 variant: 256GB (+0) và 512GB (+3tr / +4tr) như SeedData.</summary>
    private static async Task<(int XiaomiId, int IphoneId, int Var256Xiaomi, int Var512Xiaomi, int Var512Iphone)>
        SeedXiaomiIphoneVoiVariantAsync(ServiceProvider services)
    {
        var xiaomiId = await SeedProductAsync(services, "Xiaomi 15 5G", 12_990_000,
            tonKho: 25, giaKhuyenMai: 12_990_000);
        var iphoneId = await SeedProductAsync(services, "iPhone 15 Pro", 28_990_000,
            tonKho: 18, giaKhuyenMai: 28_990_000);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v256 = new ProductVariant
            { ProductId = xiaomiId, TenBienThe = "Đen / 256GB", GiaThem = 0, TonKho = 12 };
        var v512 = new ProductVariant
            { ProductId = xiaomiId, TenBienThe = "Đen / 512GB", GiaThem = 3_000_000, TonKho = 4 };
        var i512 = new ProductVariant
            { ProductId = iphoneId, TenBienThe = "Titan Tự nhiên / 512GB", GiaThem = 4_000_000, TonKho = 4 };
        ctx.ProductVariants.AddRange(v256, v512, i512);
        await ctx.SaveChangesAsync();
        return (xiaomiId, iphoneId, v256.Id, v512.Id, i512.Id);
    }

    private static async Task<(int, int)> SeedHaiSanPhamAsync(ServiceProvider services)
    {
        var a = await SeedProductAsync(services, "SP Combo A", 100_000, tonKho: 5);
        var b = await SeedProductAsync(services, "SP Combo B", 50_000, tonKho: 5);
        return (a, b);
    }

    private static async Task<(int, int, int)> SeedBaSanPhamAsync(ServiceProvider services)
    {
        var a = await SeedProductAsync(services, "SP Combo A", 100_000, tonKho: 5);
        var b = await SeedProductAsync(services, "SP Combo B", 50_000, tonKho: 5);
        var c = await SeedProductAsync(services, "SP Combo C", 200_000, tonKho: 5);
        return (a, b, c);
    }

    private static async Task<int> SeedComboAsync(ServiceProvider services,
        (int ProductId, int SoLuong)[] items, decimal phanTram = 10)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var combo = new Combo
        {
            Ten = "Combo Test P4",
            PhanTramGiam = phanTram,
            BatDau = DateTime.Now.AddDays(-1),
            KetThuc = DateTime.Now.AddDays(30),
            IsActive = true,
            Items = items.Select(i => new ComboItem
                { ProductId = i.ProductId, SoLuong = i.SoLuong }).ToList(),
        };
        ctx.Combos.Add(combo);
        await ctx.SaveChangesAsync();
        return combo.Id;
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

    private static async Task SeedVoucherAsync(ServiceProvider services, string ma,
        string loaiGiam, decimal giaTri, decimal dieuKien)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Vouchers.Add(new Voucher
        {
            Ma = ma,
            LoaiGiam = loaiGiam,
            GiaTri = giaTri,
            DieuKien = dieuKien,
            NgayHetHan = DateTime.Now.AddDays(30),
            SoLuong = 100,
            DaDung = 0,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedUserVoiDiemAsync(ServiceProvider services,
        string userId, string userName, int diem)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await ctx.Users.AnyAsync(u => u.Id == userId))
        {
            ctx.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                Email = userId + "@x.vn",
                NormalizedEmail = (userId + "@X.VN").ToUpperInvariant(),
            });
            await ctx.SaveChangesAsync();
        }
        if (diem > 0)
        {
            ctx.PointTransactions.Add(new PointTransaction
            {
                UserId = userId,
                Diem = diem,
                Loai = "Earn",
                NgayTao = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }
    }

    private static async Task<Order> TaoDonAsync(ServiceProvider services,
        string? userId, decimal tongTien)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var don = new Order
        {
            UserId = userId,
            TongTien = tongTien,
            DiaChiGiao = "Test",
            PhuongThucTT = "COD",
            TrangThai = OrderStatus.ChoXacNhan,
        };
        ctx.Orders.Add(don);
        await ctx.SaveChangesAsync();
        return don;
    }

    private static async Task<int> DemGiaoDichDiemAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.PointTransactions.CountAsync();
    }

    private static async Task<KetQuaTongDon> TinhTongDonAsync(ServiceProvider services,
        (int ProductId, int SoLuong) line, string? userId, string? maVoucher = null)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        var product = await ctx.Products.FirstAsync(p => p.Id == line.ProductId);
        // TinhTongDonAsync cộng từ line.DonGia (giá thực trả) — phải tính
        // qua TinhGiaAsync như LoadLinesAsync của CartCheckoutParityTests.
        var giaApDung = await gia.TinhGiaAsync(line.ProductId, 0, line.SoLuong);
        var lines = new List<CartLineViewModel>
        {
            new()
            {
                Product = product, Variant = null, SoLuong = line.SoLuong,
                DonGia = giaApDung.DonGia, GiaGoc = giaApDung.GiaGoc,
                TenKhuyenMai = giaApDung.TenKhuyenMai,
            },
        };
        return await gia.TinhTongDonAsync(lines, userId, maVoucher, 0, null);
    }

    private static async Task MutateAsync(ServiceProvider services, Func<AppDbContext, Task> fn)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await fn(ctx);
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> CountOrdersAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.CountAsync();
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
