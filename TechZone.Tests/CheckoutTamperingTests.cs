using System.Security.Claims;
using System.Text.Json;
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
/// Test Bước 4a (2026-11-06): Checkout POST không được tin bất kỳ giá trị
/// tiền tệ nào do client/controller gửi kèm — service phải tính lại toàn
/// bộ từ DB (giá dòng, voucher, combo, giảm hạng theo hạng hiện tại, điểm
/// theo số dư thật, freeship hạng, phí ship), và khi dữ liệu đổi giữa
/// Checkout GET và POST thì POST phải trả về trang Checkout với tổng mới +
/// yêu cầu xác nhận lại (không đặt với giá cũ/sai, không từ chối khô khi
/// vẫn đặt được với giá mới). Hết hàng thì từ chối thẳng (không có giá
/// mới nào để xác nhận).
///
/// Gồm 4 tầng:
/// A. Service: gọi thẳng DatHangAsync với GiamGiaHang / GiamGiaDiem /
///    FreeShip / GiamGiaCombo giả mạo -> đơn phải mang số tính lại.
/// B. Controller: GET -> đổi dữ liệu (hạng / voucher / Flash Sale /
///    tồn kho / số dư điểm) -> POST -> xác nhận lại / từ chối / đặt lại.
/// C. Helper: CheckoutGiaHelper.CoThayDoi (so theo thành phần, không
///    báo oan khi khách dùng điểm lần đầu).
/// D. Race + chính sách xác nhận giá (Bước 4b): service so tổng cuối
///    với tổng đã xác nhận trong transaction (TongXacNhan); POST trực
///    tiếp không snapshot vẫn đặt được với tổng service tính lại.
/// </summary>
[Collection("DbTests")]
public class CheckoutTamperingTests
{
    private readonly ITestOutputHelper _out;

    public CheckoutTamperingTests(ITestOutputHelper output) => _out = output;

    // ================= A. Service: giá trị giả mạo bị tính lại =================

    [Fact]
    public async Task DatHang_GiamGiaHangGia_Vang_TinhLai2PhanTram()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Vang", 1_000_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-vang", "test-user-vang");
        await SeedOrderAsync(h.Services, "user-vang", 25_000_000m); // > 20tr -> Vang

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-vang",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            // GIA MAO: giam hang 999.999, giam diem 888.888, voucher 777.777.
            GiamGiaHang = 999_999,
            GiamGiaDiem = 888_888,
            GiamGia = 777_777,
            FreeShip = false,
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "TAMPER-HANG-1",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(20_000m, don.GiamGiaHang); // 2% tam tinh, khong phai 999.999
        Assert.Equal(0m, don.GiamGiaDiem);
        Assert.Equal(0m, don.GiamGia);
        Assert.Equal(980_000m, don.TongTien);
        Assert.Equal(0m, don.PhiShip); // Vang mien ship
        Assert.Equal(980_000m, don.ThanhToan);
        _out.WriteLine($"Vang gia mao: GiamGiaHang={don.GiamGiaHang:N0} TongTien={don.TongTien:N0}");
    }

    [Fact]
    public async Task DatHang_GiamGiaHangGia_Dong_BangKhong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Dong", 1_000_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-dong", "test-user-dong");
        // Khong co don truoc -> hang Dong, khong duoc giam hang.

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-dong",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            GiamGiaHang = 50_000, // gia mao
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "TAMPER-HANG-2",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        Assert.Equal(0m, ketQua.DonHang!.GiamGiaHang);
        Assert.Equal(1_000_000m, ketQua.DonHang.TongTien);
        Assert.Equal(30_000m, ketQua.DonHang.PhiShip);
    }

    [Fact]
    public async Task DatHang_GiamGiaDiemGia_TinhLaiTheoDiemDung()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Diem", 1_000_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-diem", "test-user-diem");
        await SeedEarnAsync(h.Services, "user-diem", 100);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-diem",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            DiemDung = 10,
            GiamGiaDiem = 9_999_999, // gia mao: xin 10 diem nhung doi giam gan 10tr
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "TAMPER-DIEM-1",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(10_000m, don.GiamGiaDiem);
        Assert.Equal(10, don.DiemDaDung);
        Assert.Equal(990_000m, don.TongTien);
        Assert.Equal(10, await SumPointsAsync(h.Services, "user-diem", "Spend"));
    }

    [Fact]
    public async Task DatHang_GiamGiaDiemBangKhong_VanTinhLai()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Diem 0", 1_000_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-diem0", "test-user-diem0");
        await SeedEarnAsync(h.Services, "user-diem0", 100);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-diem0",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            DiemDung = 10,
            GiamGiaDiem = 0, // gui 0 (neu tin thi khach mat giam gia)
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "TAMPER-DIEM-2",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        Assert.Equal(10_000m, ketQua.DonHang!.GiamGiaDiem);
        Assert.Equal(10, ketQua.DonHang.DiemDaDung);
    }

    [Fact]
    public async Task DatHang_FreeShipGia_KhachVangLai_VanTinhPhiShip()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Ship", 100_000, tonKho: 5);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = null, // khach vang lai
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            FreeShip = true, // gia mao
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "TAMPER-SHIP-1",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        Assert.Equal(100_000m, ketQua.DonHang!.TongTien);
        Assert.Equal(30_000m, ketQua.DonHang.PhiShip);
        Assert.Equal(130_000m, ketQua.DonHang.ThanhToan);
    }

    [Fact]
    public async Task DatHang_FreeShipSai_Vang_VanMienShip()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Ship Vang", 100_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-vang-ship", "test-user-vang-ship");
        await SeedOrderAsync(h.Services, "user-vang-ship", 25_000_000m); // Vang

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-vang-ship",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            FreeShip = false, // gui sai: Vang phai mien ship
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "TAMPER-SHIP-2",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        Assert.Equal(0m, ketQua.DonHang!.PhiShip);
    }

    [Fact]
    public async Task DatHang_DiemDungVuotSoDu_TuChoi_KhongTaoDon()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Vuot", 1_000_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-it-diem", "test-user-it-diem");
        await SeedEarnAsync(h.Services, "user-it-diem", 5);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-it-diem",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            DiemDung = 10, // chi co 5 diem
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "TAMPER-DIEM-3",
        });

        Assert.False(ketQua.ThanhCong);
        Assert.Contains("chỉ có 5 điểm", ketQua.Loi);
        Assert.Equal(0, await CountOrdersAsync(h.Services));
        Assert.Equal(5, await GetTonKhoAsync(h.Services, productId));
    }

    [Fact]
    public async Task DatHang_DiemDungBiChanOPhanConLai_ChiTruDiemThatDung()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Chan", 100_000, tonKho: 5);
        await SeedVoucherAsync(h.Services, "VC90", "Tien", 90_000, 0);
        await SeedUserAsync(h.Services, "user-chan", "test-user-chan");
        await SeedEarnAsync(h.Services, "user-chan", 100);

        // Tam tinh 100k - voucher 90k = con 10k -> xin 50 diem chi dung
        // duoc 10 diem (10.000d), khong tru tron 50 diem.
        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-chan",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = "VC90",
            DiemDung = 50,
            GiamGiaDiem = 50_000, // gia mao: doi giam tron 50 diem
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "TAMPER-DIEM-4",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(90_000m, don.GiamGia);
        Assert.Equal(10_000m, don.GiamGiaDiem);
        Assert.Equal(10, don.DiemDaDung);
        Assert.Equal(0m, don.TongTien);
        Assert.Equal(10, await SumPointsAsync(h.Services, "user-chan", "Spend"));
    }

    [Fact]
    public async Task DatHang_GiamGiaComboGia_TinhLaiTuDb()
    {
        using var h = new Harness();
        var productA = await SeedProductAsync(h.Services, "SP Combo A", 100_000, tonKho: 5);
        var productB = await SeedProductAsync(h.Services, "SP Combo B", 50_000, tonKho: 5);
        var comboId = await SeedComboAsync(h.Services, productA, productB, phanTram: 10);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = null,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            ComboId = comboId,
            GiamGiaCombo = 999_999, // gia mao
            Lines =
            {
                new CheckoutLineInput(productA, null, 1, 100_000),
                new CheckoutLineInput(productB, null, 1, 50_000),
            },
            IdempotencyKey = "TAMPER-COMBO-1",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(15_000m, don.GiamGiaCombo); // 10% cua 150.000
        Assert.Equal(135_000m, don.TongTien);
        Assert.Equal(1, don.ComboSoBo);
    }

    // ================= B. Controller: doi du lieu giua GET va POST =================

    [Fact]
    public async Task CheckoutPost_KhongDoiGia_DatThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Control", 100_000, tonKho: 5);
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h);
        Assert.NotNull(h.Session.GetString("TechZone.CheckoutGia"));
        var (result, _) = await PostCheckoutAsync(h, ModelDatHang(vmGet));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(1, await CountOrdersAsync(h.Services));
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(100_000m, don.TongTien);
        Assert.Equal(30_000m, don.PhiShip);
    }

    [Fact]
    public async Task CheckoutPost_DungDiemLanDau_KhongBaoGiaDoi()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Diem Moi", 100_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-pts", "test-user-pts");
        await SeedEarnAsync(h.Services, "user-pts", 100);
        h.DangNhap("test-user-pts");
        DatGioVaoSession(h, productId);

        // GET tinh voi 0 diem; POST xin 50 diem lan dau — tong doi do
        // diem (do nguoi dung chon), khong phai gia doi -> dat luon.
        var vmGet = await GetCheckoutAsync(h);
        var (result, _) = await PostCheckoutAsync(h, ModelDatHang(vmGet), diemDung: 50);

        Assert.IsType<RedirectToActionResult>(result);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(50_000m, don.GiamGiaDiem);
        Assert.Equal(50, don.DiemDaDung);
        Assert.Equal(50_000m, don.TongTien);
    }

    [Fact]
    public async Task CheckoutPost_HangDoi_TraVeXacNhanLai_RoiDatLaiThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Hang Doi", 100_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-vang", "test-user-vang");
        var donCuId = await SeedOrderAsync(h.Services, "user-vang", 25_000_000m); // Vang
        h.DangNhap("test-user-vang");
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h); // anh gia: GiamGiaHang 2.000
        // Giua GET va POST: don cu bi huy -> chi tieu ve 0 -> xuong Dong.
        await MutateAsync(h.Services, ctx =>
        {
            var donCu = ctx.Orders.First(o => o.Id == donCuId);
            donCu.TrangThai = OrderStatus.DaHuy;
            return Task.CompletedTask;
        });

        var model = ModelDatHang(vmGet);
        var (lan1, temp1) = await PostCheckoutAsync(h, model);
        Assert.IsType<ViewResult>(lan1);
        Assert.Contains("Giá đã thay đổi", temp1["Error"]?.ToString());
        Assert.Equal(1, await CountOrdersAsync(h.Services)); // chi don seed, chua co don moi

        // Submit lai chinh form vua render (tong moi) = xac nhan -> dat duoc.
        var (lan2, _) = await PostCheckoutAsync(h, model);
        Assert.IsType<RedirectToActionResult>(lan2);
        Assert.Equal(2, await CountOrdersAsync(h.Services));
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(0m, don.GiamGiaHang); // khong con giam hang Vang
        Assert.Equal(100_000m, don.TongTien);
    }

    [Fact]
    public async Task CheckoutPost_VoucherHetHan_TraVeXacNhanLai_RoiDatLaiThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP VC Het", 100_000, tonKho: 5);
        await SeedVoucherAsync(h.Services, "VC10", "Tien", 10_000, 0);
        DatGioVaoSession(h, productId);
        h.Session.SetString("TechZone.Voucher", "VC10");

        var vmGet = await GetCheckoutAsync(h); // anh gia: giam voucher 10.000
        // Giua GET va POST: voucher het han + het luot.
        await MutateAsync(h.Services, ctx =>
        {
            var v = ctx.Vouchers.First(x => x.Ma == "VC10");
            v.NgayHetHan = DateTime.Now.AddDays(-1);
            v.DaDung = v.SoLuong;
            return Task.CompletedTask;
        });

        var model = ModelDatHang(vmGet);
        var (lan1, temp1) = await PostCheckoutAsync(h, model);
        Assert.IsType<ViewResult>(lan1);
        Assert.Contains("Giá đã thay đổi", temp1["Error"]?.ToString());
        Assert.Equal(0, await CountOrdersAsync(h.Services));

        var (lan2, _) = await PostCheckoutAsync(h, model);
        Assert.IsType<RedirectToActionResult>(lan2);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(0m, don.GiamGia); // dat khong voucher, tong moi
        Assert.Equal(100_000m, don.TongTien);
    }

    [Fact]
    public async Task CheckoutPost_FlashSaleHet_TraVeXacNhanLai_RoiDatLaiThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Sale Het", 100_000, tonKho: 5);
        await SeedFlashSaleAsync(h.Services, productId, 60_000,
            DateTime.Now.AddHours(-1), DateTime.Now.AddHours(1), gioiHan: 10, daBan: 0);
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h); // anh gia: tam tinh 60.000
        // Giua GET va POST: Flash Sale het gio.
        await MutateAsync(h.Services, ctx =>
        {
            var f = ctx.FlashSales.First(x => x.ProductId == productId);
            f.KetThuc = DateTime.Now.AddHours(-1);
            return Task.CompletedTask;
        });

        var model = ModelDatHang(vmGet);
        var (lan1, temp1) = await PostCheckoutAsync(h, model);
        Assert.IsType<ViewResult>(lan1);
        Assert.Contains("Giá đã thay đổi", temp1["Error"]?.ToString());
        Assert.Equal(0, await CountOrdersAsync(h.Services));

        var (lan2, _) = await PostCheckoutAsync(h, model);
        Assert.IsType<RedirectToActionResult>(lan2);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(100_000m, don.TongTien); // gia goc, khong con gia sale
        Assert.Equal(100_000m, don.ChiTiet.First().DonGia);
    }

    [Fact]
    public async Task CheckoutPost_KhachVangLaiNhapDiem_TuChoiKhongKetVongLap()
    {
        // P1-1 (Final Check): khach vang lai nhap diem > 0 — truoc day
        // LoiDiem chi kiem tra khi dang nhap nen lot xuong CoThayDoi,
        // DiemDungThucTe luon 0 -> "Giá đã thay đổi" mai mai. Gio POST
        // phai tra ve form voi loi ro rang ("Cần đăng nhập..."), khong
        // tao don; POST lai y het cung vay (khong phai vong lap xac
        // nhan gia — khong co "Giá đã thay đổi").
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Diem Vang Lai", 100_000, tonKho: 5);
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h);
        var model = ModelDatHang(vmGet);
        var (lan1, temp1) = await PostCheckoutAsync(h, model, diemDung: 50);
        Assert.IsType<ViewResult>(lan1);
        Assert.Contains("Cần đăng nhập", temp1["Error"]?.ToString());
        Assert.DoesNotContain("Giá đã thay đổi", temp1["Error"]?.ToString() ?? "");
        Assert.Equal(0, await CountOrdersAsync(h.Services));

        // Submit lai y het: van la loi diem ro rang, khong phai trang
        // xac nhan lai gia — khong ket vong lap.
        var (lan2, temp2) = await PostCheckoutAsync(h, model, diemDung: 50);
        Assert.IsType<ViewResult>(lan2);
        Assert.Contains("Cần đăng nhập", temp2["Error"]?.ToString());
        Assert.DoesNotContain("Giá đã thay đổi", temp2["Error"]?.ToString() ?? "");
        Assert.Equal(0, await CountOrdersAsync(h.Services));
    }

    [Fact]
    public async Task CheckoutPost_FlashSaleHetQuota_TraVeXacNhanLai_RoiDatLaiGiaGocThanhCong()
    {
        // P1-2, huong A (Final Check): het quota giua GET–POST duoc xu
        // nhu het gio — quota chi gioi han so suat GIA SALE. POST dau
        // tra ve trang xac nhan lai voi tong gia goc; submit lai dat
        // duoc gia goc, khong tru quota (DaBan khong doi), khong bi
        // tu choi "vừa hết suất" ve gio nhu truoc.
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Sale Het Quota", 100_000, tonKho: 5);
        await SeedFlashSaleAsync(h.Services, productId, 60_000,
            DateTime.Now.AddHours(-1), DateTime.Now.AddHours(1), gioiHan: 1, daBan: 0);
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h); // anh gia: tam tinh 60.000
        // Giua GET va POST: suat sale duy nhat bi don khac lay mat.
        await MutateAsync(h.Services, ctx =>
        {
            var f = ctx.FlashSales.First(x => x.ProductId == productId);
            f.DaBan = 1;
            return Task.CompletedTask;
        });

        var model = ModelDatHang(vmGet);
        var (lan1, temp1) = await PostCheckoutAsync(h, model);
        Assert.IsType<ViewResult>(lan1);
        Assert.Contains("Giá đã thay đổi", temp1["Error"]?.ToString());
        Assert.Equal(0, await CountOrdersAsync(h.Services));

        var (lan2, _) = await PostCheckoutAsync(h, model);
        Assert.IsType<RedirectToActionResult>(lan2);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(100_000m, don.TongTien); // gia goc, khong con gia sale
        Assert.Equal(100_000m, don.ChiTiet.First().DonGia);
        Assert.Null(don.ChiTiet.First().FlashSaleId); // khong an suat sale
        Assert.Equal(1, await GetFlashSaleDaBanAsync(h.Services, productId)); // quota khong bi tru them
    }

    [Fact]
    public async Task DatHang_FlashSaleHetQuota_GiaGocDaXacNhan_DatThanhCong()
    {
        // P1-2, huong A, tang service: ben goi da gui gia dong = gia
        // goc (da thay gia sau khi het quota — luong controller gui
        // gio tinh lai + trang xac nhan) thi service chap nhan don gia
        // goc, khong tru quota. Gui gia sale cu thi van bi tu choi
        // "vừa hết suất" (xem GiaSale_SoLuongVuotSuatConLai o
        // FlashSaleTests — giu nguyen, khong doi test do).
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Quota Dich Vu", 100_000, tonKho: 5);
        await SeedFlashSaleAsync(h.Services, productId, 60_000,
            DateTime.Now.AddHours(-1), DateTime.Now.AddHours(1), gioiHan: 1, daBan: 1);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = null,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "QUOTA-GOC-1",
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(100_000m, don.TongTien);
        Assert.Equal(100_000m, don.ChiTiet.First().DonGia);
        Assert.Null(don.ChiTiet.First().FlashSaleId);
        Assert.Equal(1, await GetFlashSaleDaBanAsync(h.Services, productId));
    }

    [Fact]
    public async Task CheckoutPost_TonKhoVeKhong_TuChoiVeGio()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Het Hang", 100_000, tonKho: 1);
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h);
        // Giua GET va POST: ton kho ve 0 (don khac lay mat).
        await MutateAsync(h.Services, ctx =>
        {
            var p = ctx.Products.First(x => x.Id == productId);
            p.TonKho = 0;
            return Task.CompletedTask;
        });

        var (result, temp) = await PostCheckoutAsync(h, ModelDatHang(vmGet));
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Cart", redirect.ControllerName);
        Assert.Contains("hết hàng", temp["Error"]?.ToString());
        Assert.Equal(0, await CountOrdersAsync(h.Services));
        Assert.Equal(0, await GetTonKhoAsync(h.Services, productId));
    }

    [Fact]
    public async Task CheckoutPost_DiemGiamSauGet_TuChoi_KhongDatVoiDiemCu()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Diem Giam", 100_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-diem-giam", "test-user-diem-giam");
        await SeedEarnAsync(h.Services, "user-diem-giam", 100);
        h.DangNhap("test-user-diem-giam");
        DatGioVaoSession(h, productId);

        var vmGet = await GetCheckoutAsync(h);
        // Giua GET va POST: tieu 80 diem o noi khac -> chi con 20.
        await MutateAsync(h.Services, ctx =>
        {
            ctx.PointTransactions.Add(new PointTransaction
            {
                UserId = "user-diem-giam",
                Diem = 80,
                Loai = "Spend",
                NgayTao = DateTime.UtcNow,
            });
            return Task.CompletedTask;
        });

        var (result, temp) = await PostCheckoutAsync(h, ModelDatHang(vmGet), diemDung: 50);
        Assert.IsType<ViewResult>(result);
        Assert.Contains("chỉ có 20 điểm", temp["Error"]?.ToString());
        Assert.Equal(0, await CountOrdersAsync(h.Services));
    }

    // ================= C. Helper: so sanh gia GET vs POST =================

    [Fact]
    public void CoThayDoi_DungDiemLanDau_KhongDoi()
    {
        var cu = new AnhGiaCheckout(100_000, 0, 0, 0, false);
        var moi = new KetQuaTongDon
        {
            TamTinh = 100_000,
            Hang = "Đồng",
            GiamGiaDiem = 50_000,
            DiemDungThucTe = 50,
        };
        Assert.False(CheckoutGiaHelper.CoThayDoi(cu, moi, diemDungYeuCau: 50));
    }

    [Fact]
    public void CoThayDoi_VoucherChet_LaDoi()
    {
        var cu = new AnhGiaCheckout(100_000, 10_000, 0, 0, false);
        var moi = new KetQuaTongDon { TamTinh = 100_000, Hang = "Đồng" };
        Assert.True(CheckoutGiaHelper.CoThayDoi(cu, moi, diemDungYeuCau: 0));
    }

    [Fact]
    public void CoThayDoi_TamTinhDoi_LaDoi()
    {
        var cu = new AnhGiaCheckout(60_000, 0, 0, 0, false);
        var moi = new KetQuaTongDon { TamTinh = 100_000, Hang = "Đồng" };
        Assert.True(CheckoutGiaHelper.CoThayDoi(cu, moi, diemDungYeuCau: 0));
    }

    [Fact]
    public void CoThayDoi_DiemBiChanThem_LaDoi()
    {
        // Anh GET: tam tinh 100k - voucher 90k = con 10k -> xin 50 diem
        // du kien dung duoc 10. POST chi dung duoc 5 (so du / phan con
        // lai doi) -> doi.
        var cu = new AnhGiaCheckout(100_000, 90_000, 0, 0, false);
        var moi = new KetQuaTongDon
        {
            TamTinh = 100_000,
            GiamGiaVoucher = 90_000,
            Hang = "Đồng",
            GiamGiaDiem = 5_000,
            DiemDungThucTe = 5,
        };
        Assert.True(CheckoutGiaHelper.CoThayDoi(cu, moi, diemDungYeuCau: 50));
    }

    [Fact]
    public void CoThayDoi_DiemBiChanSan_KhongDoi()
    {
        // Xin 50 diem nhung voucher chi chua 10k -> ca GET lan POST deu
        // chi dung duoc 10 diem: khong phai gia doi (khong ket vong lap).
        var cu = new AnhGiaCheckout(100_000, 90_000, 0, 0, false);
        var moi = new KetQuaTongDon
        {
            TamTinh = 100_000,
            GiamGiaVoucher = 90_000,
            Hang = "Đồng",
            GiamGiaDiem = 10_000,
            DiemDungThucTe = 10,
        };
        Assert.False(CheckoutGiaHelper.CoThayDoi(cu, moi, diemDungYeuCau: 50));
    }

    // ================= D. Race + chinh sach xac nhan gia (Buoc 4b) =================
    //
    // Race con lai cua Buoc 4a: controller so voi anh GET xong, du lieu doi
    // truoc khi service chay transaction -> service tinh theo gia MOI va
    // tao don luon. Buoc 4b chot: controller truyen tong da xac nhan
    // (TongCong) vao service qua CheckoutInput.TongXacNhan; service so
    // lai tong cuoi trong transaction truoc khi tao don — khac thi huy,
    // khong tao don (GiaDaThayDoi). Chinh sach POST truc tiep khong
    // snapshot: duoc chap nhan, tong service tinh lai chinh la tong xac
    // nhan (submit = xac nhan).
    //
    // Luu y: khong the kich hoat dung cua so race o tang controller trong
    // test (POST chay dong bo 1 luong, khong co diem chen thay doi du lieu
    // giua 2 lan tinh) — tang service mo phong chinh xac tinh huong do:
    // truyen tong xac nhan CU (luc GET) sau khi du lieu da doi, service
    // phai tu choi. Nhanh controller xu ly co GiaDaThayDoi dung chung
    // helper TraVeXacNhanLaiGia voi nhanh CoThayDoi (da co test o muc B).

    [Fact]
    public async Task DatHang_TongXacNhanLech1Dong_TuChoi_KhongTaoDon()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Race 1d", 100_000, tonKho: 5);

        // Tong that: 100.000 + ship 30.000 = 130.000. Tong xac nhan lech
        // 1d — tuc la du lieu doi sau khi controller xac nhan — service
        // phai tu choi, khong tao don, khong tru ton.
        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = null,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "RACE-1D",
            TongXacNhan = 129_999,
        });

        Assert.False(ketQua.ThanhCong);
        Assert.True(ketQua.GiaDaThayDoi);
        Assert.Contains("Giá đã thay đổi", ketQua.Loi);
        Assert.Equal(0, await CountOrdersAsync(h.Services));
        Assert.Equal(5, await GetTonKhoAsync(h.Services, productId));
    }

    [Fact]
    public async Task DatHang_TongXacNhanKhop_DatThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Race Khop", 100_000, tonKho: 5);

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = null,
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "RACE-OK-1",
            TongXacNhan = 130_000, // 100.000 + ship 30.000
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        Assert.False(ketQua.GiaDaThayDoi);
        var don = ketQua.DonHang!;
        Assert.Equal(100_000m, don.TongTien);
        Assert.Equal(30_000m, don.PhiShip);
        Assert.Equal(130_000m, don.ThanhToan);
        Assert.Equal(4, await GetTonKhoAsync(h.Services, productId));
    }

    [Fact]
    public async Task DatHang_TongXacNhanKhop_VangVoucherDiem_DatThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Race Vang", 1_000_000, tonKho: 5);
        await SeedVoucherAsync(h.Services, "VC10", "Tien", 10_000, 0);
        await SeedUserAsync(h.Services, "user-vang-race", "test-user-vang-race");
        await SeedOrderAsync(h.Services, "user-vang-race", 25_000_000m); // Vang
        await SeedEarnAsync(h.Services, "user-vang-race", 100);

        // Tong xac nhan: 1.000.000 - voucher 10.000 - giam hang 20.000
        // (2%) - diem 10.000 = 960.000; Vang mien ship -> 960.000.
        // Kiem tra check tong khong bao oan khi du uu dai cung ap.
        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-vang-race",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            MaVoucher = "VC10",
            DiemDung = 10,
            Lines = { new CheckoutLineInput(productId, null, 1, 1_000_000) },
            IdempotencyKey = "RACE-OK-2",
            TongXacNhan = 960_000,
        });

        Assert.True(ketQua.ThanhCong, ketQua.Loi);
        var don = ketQua.DonHang!;
        Assert.Equal(10_000m, don.GiamGia);
        Assert.Equal(20_000m, don.GiamGiaHang);
        Assert.Equal(10_000m, don.GiamGiaDiem);
        Assert.Equal(960_000m, don.TongTien);
        Assert.Equal(0m, don.PhiShip);
        Assert.Equal(10, await SumPointsAsync(h.Services, "user-vang-race", "Spend"));
    }

    [Fact]
    public async Task DatHang_HangDoiSauXacNhan_TongXacNhanCu_TuChoi()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Race Hang", 100_000, tonKho: 5);
        await SeedUserAsync(h.Services, "user-race-hang", "test-user-race-hang");
        var donCuId = await SeedOrderAsync(h.Services, "user-race-hang", 25_000_000m); // Vang

        // Tong controller xac nhan luc GET (con Vang): 100.000 - giam
        // hang 2.000 = 98.000, Vang mien ship -> 98.000. Giua luc
        // controller so sanh va luc service chay transaction: don cu bi
        // huy -> xuong Dong -> tong that 100.000 + ship 30.000 = 130.000.
        await MutateAsync(h.Services, ctx =>
        {
            var donCu = ctx.Orders.First(o => o.Id == donCuId);
            donCu.TrangThai = OrderStatus.DaHuy;
            return Task.CompletedTask;
        });

        var ketQua = await DatHangDichVuAsync(h.Services, new CheckoutInput
        {
            UserId = "user-race-hang",
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            Lines = { new CheckoutLineInput(productId, null, 1, 100_000) },
            IdempotencyKey = "RACE-HANG-1",
            TongXacNhan = 98_000,
        });

        Assert.False(ketQua.ThanhCong);
        Assert.True(ketQua.GiaDaThayDoi);
        Assert.Contains("Giá đã thay đổi", ketQua.Loi);
        Assert.Equal(1, await CountOrdersAsync(h.Services)); // chi don seed
        Assert.Equal(5, await GetTonKhoAsync(h.Services, productId));
    }

    [Fact]
    public async Task CheckoutPost_KhongCoAnhGia_DatTrucTiepThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Direct", 100_000, tonKho: 5);
        DatGioVaoSession(h, productId);

        // Khong GET truoc: session khong co anh gia. POST truc tiep van
        // dat duoc — tong service tinh lai (130.000) chinh la tong xac
        // nhan (submit = xac nhan), snapshot khong phai dieu kien bat
        // buoc de dat hang.
        var model = new CheckoutViewModel
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            PhuongThucTT = "COD",
            IdempotencyKey = "DIRECT-1",
        };
        var (result, _) = await PostCheckoutAsync(h, model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.True(string.IsNullOrEmpty(h.Session.GetString("TechZone.CheckoutGia"))); // van khong co anh gia (FakeSession tra "" khi key chua co)
        Assert.Equal(1, await CountOrdersAsync(h.Services));
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(100_000m, don.TongTien);
        Assert.Equal(30_000m, don.PhiShip);
        Assert.Equal(130_000m, don.ThanhToan);
    }

    [Fact]
    public async Task CheckoutPost_KhongCoAnhGia_CoVoucher_DatThanhCong()
    {
        using var h = new Harness();
        var productId = await SeedProductAsync(h.Services, "SP Direct VC", 100_000, tonKho: 5);
        await SeedVoucherAsync(h.Services, "VC10", "Tien", 10_000, 0);
        DatGioVaoSession(h, productId);
        h.Session.SetString("TechZone.Voucher", "VC10");

        var model = new CheckoutViewModel
        {
            HoTenNhan = "Test",
            SoDienThoai = "0900000000",
            DiaChi = "Test",
            PhuongThucTT = "COD",
            IdempotencyKey = "DIRECT-VC-1",
        };
        var (result, _) = await PostCheckoutAsync(h, model);

        Assert.IsType<RedirectToActionResult>(result);
        var don = await GetLatestOrderAsync(h.Services);
        Assert.Equal(10_000m, don.GiamGia);
        Assert.Equal(90_000m, don.TongTien);
        Assert.Equal(30_000m, don.PhiShip);
    }

    // ================= Helpers =================

    /// <summary>
    /// Moi "request" HTTP mo phong = 1 scope rieng (DbContext moi) tren
    /// cung file DB + cung HttpContext/session — de thay doi du lieu
    /// giua GET va POST khong bi DbContext cu cache lai.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public readonly string DbPath;
        public readonly ServiceProvider Services;
        public readonly DefaultHttpContext Http;
        public readonly FakeSession Session;
        private readonly TestHttpContextAccessor _accessor;

        public Harness()
        {
            DbPath = TempDbPath();
            _accessor = new TestHttpContextAccessor();
            Http = new DefaultHttpContext();
            Session = new FakeSession();
            Http.Session = Session;
            _accessor.HttpContext = Http;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));
            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.User.RequireUniqueEmail = true;
            }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
            services.AddSingleton<IHttpContextAccessor>(_accessor);
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
            Services = services.BuildServiceProvider();
        }

        public void DangNhap(string userName) =>
            Http.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) }, "Test"));

        public RequestScope NewRequest()
        {
            var scope = Services.CreateScope();
            var controller = new OrdersController(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<CartService>(),
                scope.ServiceProvider.GetRequiredService<LoyaltyService>(),
                scope.ServiceProvider.GetRequiredService<CheckoutService>(),
                scope.ServiceProvider.GetRequiredService<OrderService>(),
                scope.ServiceProvider.GetRequiredService<VatService>(),
                scope.ServiceProvider.GetRequiredService<PriceCalculationService>());
            controller.ControllerContext = new ControllerContext { HttpContext = Http };
            controller.TempData = new TempDataDictionary(Http, new FakeTempDataProvider());
            return new RequestScope(scope, controller);
        }

        public void Dispose()
        {
            Services.Dispose();
            DeleteDb(DbPath);
        }
    }

    private sealed class RequestScope : IDisposable
    {
        public OrdersController Controller { get; }
        private readonly IServiceScope _scope;
        public RequestScope(IServiceScope scope, OrdersController controller)
        {
            _scope = scope;
            Controller = controller;
        }
        public void Dispose() => _scope.Dispose();
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

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) =>
            new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static async Task<CheckoutViewModel> GetCheckoutAsync(Harness h)
    {
        using var req = h.NewRequest();
        var result = await req.Controller.Checkout() as ViewResult;
        Assert.NotNull(result);
        return (CheckoutViewModel)result!.Model!;
    }

    private static async Task<(IActionResult Result, ITempDataDictionary TempData)> PostCheckoutAsync(
        Harness h, CheckoutViewModel model, int diemDung = 0)
    {
        using var req = h.NewRequest();
        model.DiemDung = diemDung;
        var result = await req.Controller.Checkout(model);
        return (result, req.Controller.TempData);
    }

    private static CheckoutViewModel ModelDatHang(CheckoutViewModel vmGet) => new()
    {
        HoTenNhan = "Test",
        SoDienThoai = "0900000000",
        DiaChi = "Test",
        PhuongThucTT = "COD",
        IdempotencyKey = vmGet.IdempotencyKey,
    };

    private static void DatGioVaoSession(Harness h, int productId, int soLuong = 1) =>
        h.Session.SetString("TechZone.Cart", JsonSerializer.Serialize(new List<CartLine>
            { new() { ProductId = productId, VariantId = null, SoLuong = soLuong } }));

    private static async Task MutateAsync(ServiceProvider services, Func<AppDbContext, Task> fn)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await fn(ctx);
        await ctx.SaveChangesAsync();
    }

    private static async Task<CheckoutResult> DatHangDichVuAsync(ServiceProvider services, CheckoutInput input)
    {
        using var scope = services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        return await checkout.DatHangAsync(input);
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
        decimal gia, int tonKho)
    {
        await EnsureDbAsync(services);
        var categoryId = await SeedCategoryAsync(services);
        var brandId = await SeedBrandAsync(services, "Test Brand");
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Ten = ten,
            Gia = gia,
            GiaKhuyenMai = gia,
            CategoryId = categoryId,
            BrandId = brandId,
            TonKho = tonKho,
            LuotBan = 0,
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    private static async Task SeedUserAsync(ServiceProvider services, string userId, string userName)
    {
        await EnsureDbAsync(services);
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await ctx.Users.AnyAsync(u => u.Id == userId)) return;
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

    private static async Task SeedEarnAsync(ServiceProvider services, string userId, int diem)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PointTransactions.Add(new PointTransaction
        {
            UserId = userId,
            Diem = diem,
            Loai = "Earn",
            NgayTao = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedVoucherAsync(ServiceProvider services, string ma, string loaiGiam,
        decimal giaTri, decimal dieuKien, int soLuong = 100)
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
            SoLuong = soLuong,
            DaDung = 0,
        });
        await ctx.SaveChangesAsync();
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

    private static async Task<int> SeedComboAsync(ServiceProvider services, int productA, int productB,
        decimal phanTram = 10)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var combo = new Combo
        {
            Ten = "Combo Test A+B",
            PhanTramGiam = phanTram,
            BatDau = DateTime.Now.AddDays(-1),
            KetThuc = DateTime.Now.AddDays(30),
            IsActive = true,
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

    private static async Task<int> CountOrdersAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.CountAsync();
    }

    /// <summary>Don moi nhat (don vua dat trong test).</summary>
    private static async Task<Order> GetLatestOrderAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Orders.Include(o => o.ChiTiet)
            .OrderByDescending(o => o.Id).FirstAsync();
    }

    private static async Task<int> GetTonKhoAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Products.Where(p => p.Id == productId).Select(p => p.TonKho).FirstAsync();
    }

    private static async Task<int> GetFlashSaleDaBanAsync(ServiceProvider services, int productId)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.FlashSales.Where(f => f.ProductId == productId).Select(f => f.DaBan).FirstAsync();
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
