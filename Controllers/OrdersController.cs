using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>
/// Dat hang COD / chuyen khoan GIA LAP (khong thu tien that).
/// Phi ship: mien phi don tu 2.000.000d sau giam, khong thi 30.000d.
/// </summary>
public class OrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly CartService _cart;
    private readonly LoyaltyService _loyalty;
    private readonly CheckoutService _checkout;
    private readonly OrderService _orders;
    private readonly VatService _vat;
    private readonly PriceCalculationService _gia;

    public OrdersController(AppDbContext db, CartService cart, LoyaltyService loyalty,
        CheckoutService checkout, OrderService orders, VatService vat, PriceCalculationService gia)
    {
        _db = db;
        _cart = cart;
        _loyalty = loyalty;
        _checkout = checkout;
        _orders = orders;
        _vat = vat;
        _gia = gia;
    }

    private string? LayUserIdHienTai()
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        return _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id;
    }

    /// <summary>
    /// Session key luu anh gia checkout luc GET (Buoc 4a, 2026-11-06):
    /// snapshot cac thanh phan gia de POST so sanh "gia co doi khong".
    /// Chi de so sanh, khong bao gio la nguon su that cho tong tien.
    /// </summary>
    private const string SessionGiaKey = "TechZone.CheckoutGia";

    private void LuuAnhGiaCheckout(KetQuaTongDon ketQua) =>
        HttpContext.Session.SetString(SessionGiaKey,
            JsonSerializer.Serialize(CheckoutGiaHelper.ChupTu(ketQua)));

    private AnhGiaCheckout? DocAnhGiaCheckout()
    {
        var json = HttpContext.Session.GetString(SessionGiaKey);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<AnhGiaCheckout>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Áp toàn bộ ưu đãi checkout (voucher + combo + giảm hạng 2% + điểm)
    /// bằng CÙNG 1 hàm tính tổng với giỏ (P1, 2026-11-06). Thay cho bộ ba
    /// rời rạc TinhGiamGiaAsync + GanUuDaiThanhVienAsync + GanGiamGiaComboAsync
    /// trước đây — chính sự rời rạc đó làm giỏ thiếu giảm hạng. ViewBag cho
    /// view: DiemHienTai / Hang.
    /// </summary>
    private async Task<KetQuaTongDon> ApDungUuDaiCheckoutAsync(CartViewModel gioHang, int diemDung = 0)
    {
        var userId = LayUserIdHienTai();
        var ketQua = await _gia.ApDungVaoGioHangAsync(gioHang, userId,
            HttpContext.Session.GetString("TechZone.Voucher"), diemDung,
            int.TryParse(HttpContext.Session.GetString("TechZone.ComboId"), out var comboId)
                ? comboId : null);
        ViewBag.DiemHienTai = userId is null ? 0 : await _loyalty.TongDiemAsync(userId);
        ViewBag.Hang = ketQua.Hang;
        return ketQua;
    }

    public async Task<IActionResult> Checkout()
    {
        var vm = new CheckoutViewModel
        {
            GioHang = await _cart.GetViewModelAsync(),
            // Sinh key chong dat don trung cho lan hien form nay (Sprint 1).
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        if (!vm.GioHang.Lines.Any())
        {
            TempData["Error"] = "Giỏ hàng đang trống.";
            return RedirectToAction("Index", "Cart");
        }
        var ketQuaGet = await ApDungUuDaiCheckoutAsync(vm.GioHang);
        // Buoc 4a: chup anh gia luc hien form de POST so sanh — neu gia
        // doi giua GET va POST thi tra ve trang nay voi tong moi + yeu
        // cau xac nhan lai thay vi dat luon.
        LuuAnhGiaCheckout(ketQuaGet);
        return View(vm);
    }

    /// <summary>
    /// Checkout an toan (Sprint 1): toan bo ghi don / tru kho / tru voucher /
    /// diem / thong bao nam trong 1 transaction cua CheckoutService.
    /// POST trung idempotency key thi tra ve don cu, khong tao don moi
    /// va khong xoa gio (gio co the da duoc nap lai sau lan dat truoc).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        var gioHang = await _cart.GetViewModelAsync();
        if (!gioHang.Lines.Any())
        {
            TempData["Error"] = "Giỏ hàng đang trống.";
            return RedirectToAction("Index", "Cart");
        }
        // Client cu / POST truc tiep khong co key: tu sinh 1 key de don van
        // co unique index bao ve (khong co bao ve replay cho client nay).
        model.IdempotencyKey ??= Guid.NewGuid().ToString("N");
        // Sprint 2 — B-10: tick xuat hoa don DN ma thieu thong tin thi o lai
        // form checkout (bao loi) thay vi tao don hoa don trong; service van
        // kiem lai 1 lan nua truoc khi ghi don.
        var loiVat = _vat.KiemTraHoaDonDoanhNghiep(model.XuatHoaDon, model.TenCongTy,
            model.MaSoThue, model.DiaChiCongTy);
        if (loiVat is not null)
        {
            TempData["Error"] = loiVat;
            await ApDungUuDaiCheckoutAsync(gioHang);
            model.GioHang = gioHang;
            return View(model);
        }
        if (!ModelState.IsValid)
        {
            await ApDungUuDaiCheckoutAsync(gioHang);
            model.GioHang = gioHang;
            return View(model);
        }

        // Doi diem tich luy (nhom B): chi khi dang nhap va nhap diem > 0.
        // Diem tinh CHUNG trong ham tong don (chan o so con lai sau voucher +
        // combo + hang) thay vi goi rieng DungDiemAsync nhu truoc.
        var userId = LayUserIdHienTai();
        var ketQuaGia = await ApDungUuDaiCheckoutAsync(gioHang, model.DiemDung);
        int diemDaDung = 0;
        // P1-1 (Final Check, 2026-11-06): kiem tra LoiDiem TRUOC CoThayDoi
        // va BAT KE dang nhap hay chua. Truoc day chi kiem tra khi
        // userId != null nen khach vang lai nhap diem > 0 lot xuong duoi:
        // TinhTongDonAsync bao LoiDiem ("Cần đăng nhập...") nhung
        // DiemDungThucTe van 0, CoThayDoi so voi muc du kien (vd 50 diem)
        // luon true -> TraVeXacNhanLaiGia luu lai dung anh cu -> submit
        // lai van vay, ket vong lap "Giá đã thay đổi" vo han (khach chi
        // thoat bang cach tu xoa o diem ve 0 ma khong co huong dan nao).
        // Gio: nhap diem ma co LoiDiem thi tra ve form ngay voi loi ro
        // rang, khong vao nhanh xac nhan lai gia. Khach dang nhap xin
        // vuot so du cung bi chan o day nhu cu (LoiDiem).
        if (model.DiemDung > 0 && ketQuaGia.LoiDiem is not null)
        {
            TempData["Error"] = ketQuaGia.LoiDiem;
            model.GioHang = gioHang;
            return View(model);
        }
        if (userId is not null && model.DiemDung > 0)
        {
            diemDaDung = ketQuaGia.DiemDungThucTe;
        }

        // Buoc 4a/4b — CHINH SACH XAC NHAN GIA (CHOT 2026-11-06):
        // - Co anh gia (POST qua GET): tong o form la tong khach da thay
        //   va dong y. POST tinh lai toan bo tu DB roi so tung thanh phan
        //   voi anh (CheckoutGiaHelper.CoThayDoi — so theo thanh phan nen
        //   khach dung diem lan dau khong bi bao "gia doi" oan). Neu khac:
        //   render lai trang voi tong moi + yeu cau xac nhan lai thay vi
        //   dat luon voi gia moi; chinh lan submit lai do la xac nhan.
        //   Voucher/combo chet giua 2 lan thi bo khoi session de lan xac
        //   nhan dat duoc voi tong moi (khong uu dai chet); service van
        //   tu choi neu con co gang dung.
        // - Khong co anh gia (POST truc tiep, khong qua GET): DUOC CHAP
        //   NHAN — tong do service tinh lai trong transaction chinh la
        //   tong xac nhan (nguoi dung xac nhan bang hanh dong submit),
        //   khong co gia cu nao de doi chieu. Snapshot chi bao ve truong
        //   hop gia doi giua GET va POST, khong phai dieu kien bat buoc
        //   de dat hang.
        // - Chong race (Buoc 4b): giua luc controller so sanh o tren va
        //   luc service chay transaction, du lieu van co the doi (hang
        //   doi lam mat/giam 2%, voucher/combo bi sua gia tri, freeship
        //   doi...). Nen controller truyen tong da xac nhan
        //   (ketQuaGia.TongCong) vao service qua TongXacNhan; service so
        //   lai tong cuoi trong transaction truoc khi tao don — khac thi
        //   huy, khong tao don, tra co GiaDaThayDoi de hien lai trang
        //   Checkout (nhanh duoi).
        // Het hang thi KHONG vao nhanh xac nhan lai: service tu choi
        // thang (khong co gia moi nao de xac nhan). Voucher/combo chet
        // truoc khi controller tinh lai thi NGUOC LAI — CO vao nhanh xac
        // nhan (CoThayDoi true do GiamGiaVoucher/GiamGiaCombo doi), roi
        // TraVeXacNhanLaiGia go uu dai chet khoi session de dat duoc voi
        // tong moi khong uu dai chet. Rieng het suat combo NGAY TRONG
        // transaction (buoc tru quota cua service that bai) thi service
        // tu choi thang ve gio nhu het hang.
        // Het quota Flash Sale thi NGUOC LAI: vao nhanh xac nhan lai nhu
        // het gio (P1-2, huong A, Final Check) — quota chi gioi han so
        // suat GIA SALE, khach van mua duoc gia goc, tong moi tren trang
        // xac nhan chinh la gia goc.
        var anhGiaCu = DocAnhGiaCheckout();
        if (anhGiaCu is not null && CheckoutGiaHelper.CoThayDoi(anhGiaCu, ketQuaGia, model.DiemDung))
            return await TraVeXacNhanLaiGia(model, gioHang);

        // Tong da xac nhan truyen vao service de chot chong race trong
        // transaction (Buoc 4b): co anh gia thi la TongCong vua so khop
        // anh; khong anh gia (POST truc tiep) thi null — tong service
        // tinh lai chinh la tong xac nhan (xem chinh sach o tren).
        var tongXacNhan = anhGiaCu is null ? null : (decimal?)ketQuaGia.TongCong;
        var ketQua = await _checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = model.HoTenNhan,
            SoDienThoai = model.SoDienThoai,
            DiaChi = model.DiaChi,
            PhuongThucTT = model.PhuongThucTT,
            InstallmentMonths = model.InstallmentMonths is 6 or 12 ? model.InstallmentMonths : null,
            DiemDung = diemDaDung,
            // GiamGia* / FreeShip duoi day CHI LA GOI Y hien thi — service
            // tinh lai tat ca tu DB (Buoc 4a), gui sai cung khong anh
            // huong tong don.
            GiamGiaDiem = gioHang.GiamGiaDiem,
            XuatHoaDon = model.XuatHoaDon,
            TenCongTy = model.TenCongTy,
            MaSoThue = model.MaSoThue,
            DiaChiCongTy = model.DiaChiCongTy,
            MaVoucher = gioHang.MaVoucher,
            GiamGia = gioHang.GiamGia,
            GiamGiaHang = gioHang.GiamGiaHang,
            // Combo (Sprint 3 — B-7): lay Id tu session (dat luc AddCombo);
            // service tinh lai giam gia tu DB, khong tin so hien thi o GET.
            ComboId = int.TryParse(HttpContext.Session.GetString("TechZone.ComboId"), out var comboId)
                ? comboId : null,
            GiamGiaCombo = gioHang.GiamGiaCombo,
            FreeShip = gioHang.FreeShip,
            Lines = gioHang.Lines
                .Select(l => new CheckoutLineInput(l.Product.Id, l.Variant?.Id, l.SoLuong, l.DonGia))
                .ToList(),
            IdempotencyKey = model.IdempotencyKey,
            TongXacNhan = tongXacNhan,
        });

        // Race (Buoc 4b): tong doi giua luc controller so sanh va luc
        // service chay transaction — service da huy, KHONG tao don.
        // Hien lai trang Checkout voi tong moi de khach xac nhan lai
        // (giong nhanh CoThayDoi o tren); gio hang van nguyen vi
        // transaction da rollback, khong tru ton/voucher/diem gi.
        if (ketQua.GiaDaThayDoi)
            return await TraVeXacNhanLaiGia(model, gioHang);

        if (!ketQua.ThanhCong || ketQua.DonHang is null)
        {
            TempData["Error"] = ketQua.Loi ?? "Đặt hàng thất bại, vui lòng thử lại.";
            return RedirectToAction("Index", "Cart");
        }

        if (ketQua.LaDonCu)
        {
            TempData["Success"] = $"Đơn #{ketQua.DonHang.Id} đã được tạo trước đó.";
            // SEC-04: khong dua ma tra cuu vao URL nua (khong lo vao
            // history/log/Referer). Don cu thi ma goc khong khoi phuc duoc
            // tu hash — khach dung ma da nhan o lan dat dau.
            return RedirectToAction(nameof(Details), new { id = ketQua.DonHang.Id });
        }

        _cart.Clear();
        HttpContext.Session.Remove("TechZone.Voucher");
        HttpContext.Session.Remove("TechZone.ComboId");
        TempData["Success"] = $"Đặt hàng thành công. Mã đơn: #{ketQua.DonHang.Id}.";
        // SEC-04: ma tra cuu goc (plaintext) chi truyen qua TempData
        // (session server) de hien 1 lan duy nhat tren trang Details —
        // khong nam trong URL. Don tai khoan thi null.
        if (ketQua.GuestTokenMoi is not null)
            TempData["GuestTokenMoi"] = ketQua.GuestTokenMoi;
        return RedirectToAction(nameof(Details), new { id = ketQua.DonHang.Id });
    }

    /// <summary>
    /// Hiện lại trang Checkout với tổng MỚI + yêu cầu xác nhận lại
    /// ("Giá đã thay đổi, vui lòng xác nhận lại"), thay vì đặt đơn với
    /// giá mới. Dùng chung cho cả 2 nhánh phát hiện giá đổi:
    /// (1) controller so với ảnh GET (CoThayDoi), (2) service phát hiện
    /// tổng đổi trong transaction (GiaDaThayDoi — race giữa controller
    /// và service, Bước 4b). Tính lại tổng mới từ DB, gỡ voucher/combo
    /// đã chết khỏi session rồi tính lại lần nữa để tổng mới không chứa
    /// ưu đãi chết, lưu ảnh giá mới để lần submit tiếp theo so với tổng
    /// mới này — chính lần submit lại đó là xác nhận.
    /// </summary>
    private async Task<IActionResult> TraVeXacNhanLaiGia(CheckoutViewModel model, CartViewModel gioHang)
    {
        // Sprint 6 Module 4 — lay anh gia CU truoc khi LuuAnhGiaCheckout
        // ghi de (ca 2 duong phat hien gia doi deu qua day: (1) controller
        // CoThayDoi, (2) service GiaDaThayDoi trong transaction — snapshot
        // trong session luc nay van la anh luc GET vi service khong cham
        // session). Tong cu tai tao tu anh (0 diem, giong tong form GET);
        // tong moi = tong tinh lai tu DB ben duoi.
        var anhGiaCu = DocAnhGiaCheckout();
        var ketQuaMoi = await ApDungUuDaiCheckoutAsync(gioHang, model.DiemDung);
        var boUuDaiChet = false;
        if (ketQuaMoi.LoiVoucher is not null)
        {
            HttpContext.Session.Remove("TechZone.Voucher");
            boUuDaiChet = true;
        }
        if (ketQuaMoi.LoiCombo is not null)
        {
            HttpContext.Session.Remove("TechZone.ComboId");
            boUuDaiChet = true;
        }
        if (boUuDaiChet)
            ketQuaMoi = await ApDungUuDaiCheckoutAsync(gioHang, model.DiemDung);
        LuuAnhGiaCheckout(ketQuaMoi);
        // Sprint 6 Module 4 — truyen tong cu/moi/chenh lech qua ViewBag de
        // banner + tom tat hien ro so (view chi hien thi, khong doi logic).
        // Khong co anh cu (vd POST truc tiep) thi khong co so cu — view
        // giu cau chung nhu truoc.
        if (anhGiaCu is not null)
        {
            var giaCu = CheckoutGiaHelper.TongCongTuAnh(anhGiaCu);
            ViewBag.GiaCu = giaCu;
            ViewBag.GiaMoi = ketQuaMoi.TongCong;
            ViewBag.ChenhLech = ketQuaMoi.TongCong - giaCu;
        }
        TempData["Error"] = "Giá đã thay đổi, vui lòng xác nhận lại.";
        ViewBag.GiaDaThayDoi = true;
        model.GioHang = gioHang;
        return View(model);
    }

    [Authorize]
    public async Task<IActionResult> MyOrders()
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var orders = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.NgayDat)
            .ToListAsync();
        return View(orders);
    }

    /// <summary>
    /// SEC-04: xem don KHONG con nhan ma tra cuu qua URL query (?token=)
    /// — token trong URL lo vao history/log/Referer. GET chi hien don cho
    /// chu don / Admin; moi truong hop khac (don vang lai, don cua tai
    /// khoan khac, don khong ton tai) deu tra CUNG mot form nhap ma
    /// (NhapMaTraCuu) de khong lo don co ton tai hay thuoc loai nao.
    /// Khach vang lai gui ma bang POST Details ben duoi. Ma goc vua tao o
    /// checkout (TempData["GuestTokenMoi"]) hien 1 lan duy nhat tren form.
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .Include(o => o.LichSuTrangThai)
            .FirstOrDefaultAsync(o => o.Id == id);
        var userId = LayUserIdHienTai();
        // Chu don / Admin xem thang khong can ma (token=null chi dung cho
        // don vang lai; CoTheXemDonQ voi token null van dung cho chu don).
        if (order is not null
            && OrderAccess.CoTheXemDonQ(order, userId, User.IsInRole("Admin"), guestToken: null))
            return HienThiDonHang(order, userId);
        ViewBag.GuestTokenMoi = TempData["GuestTokenMoi"] as string;
        return View("NhapMaTraCuu", new NhapMaTraCuuViewModel { OrderId = id, HanhDong = "Details" });
    }

    /// <summary>
    /// SEC-04: nhap ma tra cuu bang POST (token trong body, khong vao URL).
    /// Don vang lai: verify kem rate limit (XacMinhToken — sai 5 lan khoa
    /// 15 phut, het han tu choi); dung thi hien don. Don khong ton tai /
    /// don tai khoan: CUNG loi chung, khong dem sai (ma vo nghia voi don
    /// tai khoan) va khong lo don ton tai.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(int id, string token)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .Include(o => o.LichSuTrangThai)
            .FirstOrDefaultAsync(o => o.Id == id);
        var userId = LayUserIdHienTai();
        if (order is not null
            && OrderAccess.CoTheXemDonQ(order, userId, User.IsInRole("Admin"), guestToken: null))
            return HienThiDonHang(order, userId);
        const string loiChung = "Không tìm thấy đơn hoặc mã tra cứu không đúng. Vui lòng kiểm tra lại mã đơn và mã tra cứu.";
        var vm = new NhapMaTraCuuViewModel { OrderId = id, HanhDong = "Details" };
        if (order is null || order.UserId is not null)
        {
            ViewBag.Loi = loiChung;
            return View("NhapMaTraCuu", vm);
        }
        if (!OrderAccess.XacMinhToken(order, token, DateTime.Now))
        {
            // Luu dem sai / khoa (ke ca khi het han thi XacMinhToken khong
            // doi entity — SaveChanges van an toan).
            await _db.SaveChangesAsync();
            ViewBag.Loi = loiChung;
            return View("NhapMaTraCuu", vm);
        }
        await _db.SaveChangesAsync();
        return HienThiDonHang(order, userId);
    }

    /// <summary>
    /// Chuan bi ViewBag + tra view Details cho don da duoc phep xem.
    /// Timeline ve theo NgayCapNhat tang dan (nhom C).
    /// </summary>
    private IActionResult HienThiDonHang(Order order, string? userId)
    {
        order.LichSuTrangThai = order.LichSuTrangThai.OrderBy(h => h.NgayCapNhat).ToList();
        ViewBag.LaChuDon = order.UserId is not null && order.UserId == userId;
        ViewBag.LaKhachVangLai = order.UserId is null;
        return View("Details", order);
    }

    /// <summary>
    /// Khach huy don cua minh. Chuyen trang thai theo bang hop le + idempotent
    /// (Sprint 1): don da DaHuy thi tra thanh cong, khong hoan ton lan 2.
    /// Sprint 1 chi hoan ton kho; hoan tac diem/voucher/luot ban la Sprint 2.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Huy(int id)
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return NotFound();
        var ketQua = await _orders.HuyDonAsync(id);
        if (!ketQua.ThanhCong)
        {
            TempData["Error"] = ketQua.Loi ?? "Không hủy được đơn hàng.";
            return RedirectToAction(nameof(MyOrders));
        }
        TempData["Success"] = ketQua.DaOTrangThaiDich
            ? $"Đơn #{id} đã hủy trước đó."
            : $"Đã hủy đơn #{id}, tồn kho đã hoàn lại.";
        return RedirectToAction(nameof(MyOrders));
    }

    /// <summary>Doi dia chi giao (nhom C): chi chu don, chi khi don chua giao.</summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiDiaChi(int id, string hoTenNhan, string soDienThoai, string diaChi)
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return NotFound();
        if (order.TrangThai is not (OrderStatus.ChoXacNhan or OrderStatus.DaXacNhan))
        {
            TempData["Error"] = "Chỉ đổi được địa chỉ khi đơn đang chờ xác nhận hoặc đã xác nhận.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(hoTenNhan) || string.IsNullOrWhiteSpace(soDienThoai)
            || string.IsNullOrWhiteSpace(diaChi))
        {
            TempData["Error"] = "Vui lòng nhập đủ họ tên, số điện thoại và địa chỉ.";
            return RedirectToAction(nameof(Details), new { id });
        }
        order.DiaChiGiao = $"{hoTenNhan.Trim()}, {soDienThoai.Trim()}, {diaChi.Trim()}";
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật địa chỉ giao hàng.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Hoa don VAT gia lap (nhom C): chu don hoac Admin; don khong xuat VAT thi ve Details.
    /// SEC-04: giong Details — GET khong nhan token qua URL; khach vang lai
    /// nhap ma bang POST HoaDon ben duoi (form NhapMaTraCuu, HanhDong="HoaDon").
    /// </summary>
    public async Task<IActionResult> HoaDon(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .FirstOrDefaultAsync(o => o.Id == id);
        var userId = LayUserIdHienTai();
        if (order is not null
            && OrderAccess.CoTheXemDonQ(order, userId, User.IsInRole("Admin"), guestToken: null))
            return HienThiHoaDon(order, userId);
        return View("NhapMaTraCuu", new NhapMaTraCuuViewModel { OrderId = id, HanhDong = "HoaDon" });
    }

    /// <summary>SEC-04: nhap ma tra cuu bang POST de xem hoa don (xem POST Details).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HoaDon(int id, string token)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .FirstOrDefaultAsync(o => o.Id == id);
        var userId = LayUserIdHienTai();
        if (order is not null
            && OrderAccess.CoTheXemDonQ(order, userId, User.IsInRole("Admin"), guestToken: null))
            return HienThiHoaDon(order, userId);
        const string loiChung = "Không tìm thấy đơn hoặc mã tra cứu không đúng. Vui lòng kiểm tra lại mã đơn và mã tra cứu.";
        if (order is null || order.UserId is not null)
        {
            ViewBag.Loi = loiChung;
            return View("NhapMaTraCuu", new NhapMaTraCuuViewModel { OrderId = id, HanhDong = "HoaDon" });
        }
        if (!OrderAccess.XacMinhToken(order, token, DateTime.Now))
        {
            await _db.SaveChangesAsync();
            ViewBag.Loi = loiChung;
            return View("NhapMaTraCuu", new NhapMaTraCuuViewModel { OrderId = id, HanhDong = "HoaDon" });
        }
        await _db.SaveChangesAsync();
        return HienThiHoaDon(order, userId);
    }

    /// <summary>
    /// Tra view hoa don neu don co du thong tin VAT; thieu thi ve view Details
    /// (khong redirect kem token nhu truoc — khach vang lai vua verify xong
    /// khong can nhap lai ma). Chu don/Admin goi tu GET thi redirect ve Details.
    /// </summary>
    private IActionResult HienThiHoaDon(Order order, string? userId)
    {
        if (string.IsNullOrEmpty(order.VatTenCongTy)
            || string.IsNullOrEmpty(order.VatMaSoThue)
            || string.IsNullOrEmpty(order.VatDiaChiCongTy))
        {
            // Don cu / don dat truoc khi co validate (Sprint 2 — B-10): hoa
            // don DN thieu 1 trong 3 thong tin thi khong xuat, ve Details.
            TempData["Error"] = "Đơn này không có đủ thông tin xuất hóa đơn VAT (thiếu tên công ty / mã số thuế / địa chỉ công ty).";
            return HienThiDonHang(order, userId);
        }
        return View("HoaDon", order);
    }

    /// <summary>Khach bam "Toi da thanh toan" sau khi quet QR vi gia lap (nhom C).</summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XacNhanThanhToan(int id)
    {
        var userId = _db.Users.FirstOrDefault(u => u.UserName == User.Identity!.Name)?.Id;
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return NotFound();
        var laVi = new[] { "MoMo", "ZaloPay", "VNPay" }.Contains(order.PhuongThucTT);
        if (!laVi || order.DaThanhToan)
        {
            TempData["Error"] = "Chỉ xác nhận được thanh toán ví (MoMo/ZaloPay/VNPay) chưa thanh toán.";
            return RedirectToAction(nameof(Details), new { id });
        }
        order.DaThanhToan = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã ghi nhận thanh toán {order.PhuongThucTT} cho đơn #{id} (giả lập).";
        return RedirectToAction(nameof(Details), new { id });
    }
}
