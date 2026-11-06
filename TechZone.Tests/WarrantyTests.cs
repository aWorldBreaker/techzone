using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Tests;

/// <summary>
/// Nhanh bao hanh (Returns.Warranty) — kiem thu live vong Phase 4 (2026-11-06,
/// app copy :5009, DB seed moi) xac nhan bang tay; file nay khoa cac hanh vi
/// do lai thanh regression test (khong sua doi hanh vi):
///  - Chi don DA GIAO moi duoc bao hanh (guard Sprint 4 P1-17); don ChoXacNhan
///    / DangGiao / DaHuy deu bi chan ngay luc tao (GET hien form + POST luu).
///  - POST hop le luu dung du lieu: OrderId / ProductId / UserId / MoTaLoi
///    (trim) / TrangThai "Chờ duyệt".
///  - Don cua user khac -> 404; SP khong co trong don -> 404; mo ta rong ->
///    khong luu.
///  - Trung yeu cau (cung don + SP): Step 4 CHAN tao moi khi da co yeu
///    cau "Chờ duyệt" hoac "Đã duyệt"; sau "Từ chối" (va "Hoàn thành")
///    duoc nop lai; SP khac trong cung don khong bi chan. GET form bao
///    hanh cung bao trung ngay (P2 Final Check, 2026-11-06). Nhanh doi tra
///    chan theo DON (khong theo SP): cung don, SP nao cung chan khi dang
///    mo; don khac thi khong chan.
///  - Admin duyet bao hanh chi doi trang thai, khong hoan tac (khac doi tra).
/// Ma tran quyen Details loai=baohanh (chu 200 / Admin 200 / khac 404) da co
/// o GuestTokenHardeningTests.ReturnsDetails_KhachKhac404_ChuDon200_Admin200;
/// TraCuu oracle chung da co o GuestTokenTests — khong lap lai o day.
/// </summary>
[Collection("DbTests")]
public class WarrantyTests
{
    [Fact]
    public async Task WarrantyPost_DonDaGiao_LuuClaimDungDuLieu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);

            var ctrl = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl.Warranty(orderId, productId, "  Màn hình sọc ngang  ");

            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claim = await ctx.WarrantyClaims.SingleAsync();
            Assert.Equal(orderId, claim.OrderId);
            Assert.Equal(productId, claim.ProductId);
            Assert.Equal("u1", claim.UserId);
            Assert.Equal("Màn hình sọc ngang", claim.MoTaLoi);
            Assert.Equal("Chờ duyệt", claim.TrangThai);
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task WarrantyGet_DonDaGiao_TraForm()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl.Warranty(orderId, productId);
            var view = Assert.IsType<ViewResult>(ketQua);
            Assert.Equal(orderId, ((WarrantyClaim)view.Model!).OrderId);
            Assert.Equal(productId, ((WarrantyClaim)view.Model!).ProductId);
        }
        finally { DeleteDb(db); }
    }

    // Runner (TechZone.Tests.Runner) goi method [Fact] khong tham so nen
    // khong dung [Theory]/InlineData duoc — lap cac trang thai trong 1 Fact.
    [Fact]
    public async Task Warranty_DonChuaGiao_BiChan_GetVaPost()
    {
        var trangThais = new[]
        {
            OrderStatus.ChoXacNhan, OrderStatus.DaXacNhan, OrderStatus.DangDongGoi,
            OrderStatus.DangGiao, OrderStatus.DaHuy,
        };
        foreach (var trangThai in trangThais)
            await Warranty_DonChuaGiao_BiChan_GetVaPost_1(trangThai);
    }

    private static async Task Warranty_DonChuaGiao_BiChan_GetVaPost_1(string trangThai)
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", trangThai);

            var ctrlGet = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var get = await ctrlGet.Warranty(orderId, productId);
            var redGet = Assert.IsType<RedirectToActionResult>(get);
            Assert.Equal("Details", redGet.ActionName);
            Assert.Equal("Orders", redGet.ControllerName);

            var ctrlPost = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var post = await ctrlPost.Warranty(orderId, productId, "Lỗi test");
            var redPost = Assert.IsType<RedirectToActionResult>(post);
            Assert.Equal("Details", redPost.ActionName);

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await ctx.WarrantyClaims.ToListAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_DonCuaUserKhac_Tra404_GetVaPost()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            await SeedUserAsync(services, "u2", "khac@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);

            var ctrlGet = TaoReturnsController(services, db, NguoiDung("khac@test.vn"));
            Assert.IsType<NotFoundResult>(await ctrlGet.Warranty(orderId, productId));
            var ctrlPost = TaoReturnsController(services, db, NguoiDung("khac@test.vn"));
            Assert.IsType<NotFoundResult>(await ctrlPost.Warranty(orderId, productId, "Lỗi test"));

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await ctx.WarrantyClaims.ToListAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_SpKhongTrongDon_Tra404()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, _) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var spKhac = await SeedSanPhamRiengAsync(services);

            var ctrlGet = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<NotFoundResult>(await ctrlGet.Warranty(orderId, spKhac));
            var ctrlPost = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<NotFoundResult>(await ctrlPost.Warranty(orderId, spKhac, "Lỗi test"));

            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await ctx.WarrantyClaims.ToListAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_MoTaRong_KhongLuu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl.Warranty(orderId, productId, "   ");
            Assert.IsType<RedirectToActionResult>(ketQua);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await ctx.WarrantyClaims.ToListAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_TrungKhiChoDuyet_ChanKhongTaoMoi()
    {
        // Step 4: POST lan 2 (claim dau dang "Chờ duyệt") bi chan — khong
        // tao claim moi, redirect ve Index kem TempData Error ro rang.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<RedirectToActionResult>(await ctrl1.Warranty(orderId, productId, "Lỗi lần 1"));
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId, productId, "Lỗi lần 2");
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            var loi = ctrl2.TempData["Error"]?.ToString();
            Assert.Contains("Đã có yêu cầu bảo hành đang", loi);
            Assert.Contains("Chờ duyệt", loi);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task WarrantyGet_TrungKhiChoDuyet_BaoNgayKhongHienForm()
    {
        // P2 (Final Check): GET form bao hanh cung chan trung nhu POST —
        // da co claim "Chờ duyệt" cho cung don + SP thi bao ngay ve Index,
        // khong de khach dien mo ta xong moi bi chan o POST.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<RedirectToActionResult>(await ctrl1.Warranty(orderId, productId, "Lỗi lần 1"));

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId, productId);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("Đã có yêu cầu bảo hành đang", ctrl2.TempData["Error"]?.ToString());
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_TrungKhiDaDuyet_ChanKhongTaoMoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Warranty(orderId, productId, "Lỗi lần 1");
            await DatTrangThaiBaoHanhAsync(services, orderId, productId, "Đã duyệt");

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId, productId, "Lỗi lần 2");
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Contains("Đã duyệt", ctrl2.TempData["Error"]?.ToString());
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_SauTuChoi_ChoNopLai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Warranty(orderId, productId, "Lỗi lần 1");
            await DatTrangThaiBaoHanhAsync(services, orderId, productId, "Từ chối");

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId, productId, "Lỗi lần 2 — nộp lại sau từ chối");
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Null(ctrl2.TempData["Error"]);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_DonKhac_KhongChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId1, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var orderId2 = await SeedDonVoiSanPhamAsync(services, "u1", OrderStatus.DaGiao, productId);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Warranty(orderId1, productId, "Lỗi đơn 1");
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId2, productId, "Lỗi đơn 2 — cùng SP, khác đơn");
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Null(ctrl2.TempData["Error"]);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Warranty_SanPhamKhacCungDon_KhongChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId1) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var productId2 = await ThemSanPhamVaoDonAsync(services, orderId);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Warranty(orderId, productId1, "Lỗi SP 1");
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Warranty(orderId, productId2, "Lỗi SP 2 — khác SP, cùng đơn");
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Null(ctrl2.TempData["Error"]);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.WarrantyClaims.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    // ================= Chong trung — nhanh DOI TRA (chan theo don) =================

    [Fact]
    public async Task DoiTra_TrungKhiChoDuyet_ChanKhongTaoMoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<RedirectToActionResult>(await ctrl1.Create(orderId, productId, "Đổi lần 1", null));
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Create(orderId, productId, "Đổi lần 2", null);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            var loi = ctrl2.TempData["Error"]?.ToString();
            Assert.Contains("Đã có yêu cầu đổi trả đang", loi);
            Assert.Contains("Chờ duyệt", loi);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTraGet_TrungKhiChoDuyet_BaoNgayKhongHienForm()
    {
        // P2 (Final Check): GET form đổi trả cũng chặn trùng như POST —
        // đã có yêu cầu "Chờ duyệt" cho đơn này thì báo ngay về Index,
        // không để khách chọn SP/điền lý do xong mới bị chặn ở POST.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            Assert.IsType<RedirectToActionResult>(await ctrl1.Create(orderId, productId, "Đổi lần 1", null));

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Create(orderId, productId);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("Đã có yêu cầu đổi trả đang", ctrl2.TempData["Error"]?.ToString());
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTra_TrungKhiDaDuyet_ChanKhongTaoMoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Create(orderId, productId, "Đổi lần 1", null);
            await DatTrangThaiDoiTraAsync(services, orderId, "Đã duyệt");

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl2.Create(orderId, productId, "Đổi lần 2", null);
            Assert.Contains("Đã duyệt", ctrl2.TempData["Error"]?.ToString());
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTra_SauTuChoi_ChoNopLai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Create(orderId, productId, "Đổi lần 1", null);
            await DatTrangThaiDoiTraAsync(services, orderId, "Từ chối");

            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Create(orderId, productId, "Đổi lần 2 — nộp lại sau từ chối", null);
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Null(ctrl2.TempData["Error"]);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTra_DonKhac_KhongChan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId1, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var orderId2 = await SeedDonVoiSanPhamAsync(services, "u1", OrderStatus.DaGiao, productId);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Create(orderId1, productId, "Đổi đơn 1", null);
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Create(orderId2, productId, "Đổi đơn 2 — khác đơn", null);
            Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Null(ctrl2.TempData["Error"]);
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task DoiTra_SanPhamKhacCungDon_VanChan()
    {
        // Khac bao hanh: doi tra chan theo DON — SP khac trong cung don van bi chan.
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId1) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var productId2 = await ThemSanPhamVaoDonAsync(services, orderId);
            var ctrl1 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrl1.Create(orderId, productId1, "Đổi SP 1", null);
            var ctrl2 = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            var ketQua = await ctrl2.Create(orderId, productId2, "Đổi SP 2 — khác SP, cùng đơn", null);
            var redirect = Assert.IsType<RedirectToActionResult>(ketQua);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("Đã có yêu cầu đổi trả đang", ctrl2.TempData["Error"]?.ToString());
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await ctx.ReturnRequests.CountAsync());
        }
        finally { DeleteDb(db); }
    }

    [Fact]
    public async Task Duyet_BaoHanh_ChiDoiTrangThai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedUserAsync(services, "u1", "chu@test.vn");
            var (orderId, productId) = await SeedDonAsync(services, "u1", OrderStatus.DaGiao);
            var ctrlTao = TaoReturnsController(services, db, NguoiDung("chu@test.vn"));
            await ctrlTao.Warranty(orderId, productId, "Lỗi test");
            int claimId;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                claimId = (await ctx.WarrantyClaims.SingleAsync()).Id;
            }
            var ctrlAdmin = TaoReturnsController(services, db, Admin());
            var ketQua = await ctrlAdmin.Duyet(claimId, "baohanh", "Đã duyệt");
            Assert.IsType<RedirectToActionResult>(ketQua);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var claim = await ctx.WarrantyClaims.FindAsync(claimId);
                Assert.Equal("Đã duyệt", claim!.TrangThai);
            }
        }
        finally { DeleteDb(db); }
    }

    // ================= Helpers (mau GuestTokenHardeningTests) =================

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider();
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId, string userName)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Seed 1 SP + 1 don o trang thai cho truoc, SP co trong don.</summary>
    private static async Task<(int OrderId, int ProductId)> SeedDonAsync(
        ServiceProvider services, string userId, string trangThai)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var category = new Category { Ten = "Dien thoai", Slug = "dien-thoai" };
        var brand = new Brand { Ten = "Hang Test" };
        ctx.Categories.Add(category);
        ctx.Brands.Add(brand);
        await ctx.SaveChangesAsync();
        var product = new Product { Ten = "SP Test", Gia = 100_000, CategoryId = category.Id, BrandId = brand.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        var order = new Order
        {
            UserId = userId, TrangThai = trangThai,
            DiaChiGiao = "Chu don, 0900000001, Ha Noi", TongTien = 100_000,
            ChiTiet = new List<OrderDetail>
            {
                new() { ProductId = product.Id, DonGia = 100_000, SoLuong = 1 },
            },
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return (order.Id, product.Id);
    }

    private static async Task<int> SeedSanPhamRiengAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx.Categories.FirstAsync();
        var brand = await ctx.Brands.FirstAsync();
        var product = new Product { Ten = "SP Ngoai Don", Gia = 50_000, CategoryId = category.Id, BrandId = brand.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>Seed them 1 don moi (cung user, cung SP) — dung cho test "don khac khong chan".</summary>
    private static async Task<int> SeedDonVoiSanPhamAsync(
        ServiceProvider services, string userId, string trangThai, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        var order = new Order
        {
            UserId = userId, TrangThai = trangThai,
            DiaChiGiao = "Chu don, 0900000001, Ha Noi", TongTien = 100_000,
            ChiTiet = new List<OrderDetail>
            {
                new() { ProductId = productId, DonGia = 100_000, SoLuong = 1 },
            },
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>Them 1 SP moi + dong chi tiet vao don co san — dung cho test "SP khac cung don".</summary>
    private static async Task<int> ThemSanPhamVaoDonAsync(ServiceProvider services, int orderId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx.Categories.FirstAsync();
        var brand = await ctx.Brands.FirstAsync();
        var product = new Product { Ten = "SP Thu 2 Trong Don", Gia = 50_000, CategoryId = category.Id, BrandId = brand.Id };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        ctx.OrderDetails.Add(new OrderDetail { OrderId = orderId, ProductId = product.Id, DonGia = 50_000, SoLuong = 1 });
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>Doi truc tiep TrangThai cua (cac) claim bao hanh — gia lap Admin duyet/tu choi.</summary>
    private static async Task DatTrangThaiBaoHanhAsync(
        ServiceProvider services, int orderId, int productId, string trangThai)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var claims = await ctx.WarrantyClaims
            .Where(w => w.OrderId == orderId && w.ProductId == productId).ToListAsync();
        Assert.NotEmpty(claims);
        foreach (var c in claims) c.TrangThai = trangThai;
        await ctx.SaveChangesAsync();
    }

    /// <summary>Doi truc tiep TrangThai cua (cac) yeu cau doi tra cua don — gia lap Admin duyet/tu choi.</summary>
    private static async Task DatTrangThaiDoiTraAsync(ServiceProvider services, int orderId, string trangThai)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var yeuCaus = await ctx.ReturnRequests.Where(r => r.OrderId == orderId).ToListAsync();
        Assert.NotEmpty(yeuCaus);
        foreach (var r in yeuCaus) r.TrangThai = trangThai;
        await ctx.SaveChangesAsync();
    }

    private static ClaimsPrincipal NguoiDung(string userName) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }, "Test"));

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin@techzone.vn"),
            new Claim(ClaimTypes.Role, "Admin"),
        }, "Test"));

    private static ReturnsController TaoReturnsController(ServiceProvider services, string dbPath, ClaimsPrincipal principal)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = new DefaultHttpContext { User = principal };
        return new ReturnsController(db, new TestWebHostEnvironment(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoOpTempDataProvider()),
        };
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* bo qua */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* bo qua */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* bo qua */ }
    }
}
