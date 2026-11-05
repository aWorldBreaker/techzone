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
    /// Uu dai thanh vien o checkout (nhom B): hang Vang giam them 2% tam tinh
    /// + mien phi ship; ViewBag cho view hien diem/hang hien tai.
    /// </summary>
    private async Task GanUuDaiThanhVienAsync(CartViewModel gioHang)
    {
        var userId = LayUserIdHienTai();
        if (userId is null)
        {
            ViewBag.DiemHienTai = 0;
            ViewBag.Hang = "Đồng";
            return;
        }
        gioHang.GiamGiaHang = await _loyalty.GiamGiaHangAsync(userId, gioHang.TamTinh);
        if (await _loyalty.LaHangVangAsync(userId)) gioHang.FreeShip = true;
        ViewBag.DiemHienTai = await _loyalty.TongDiemAsync(userId);
        ViewBag.Hang = await _loyalty.HangAsync(userId);
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
        await _cart.TinhGiamGiaAsync(vm.GioHang, HttpContext.Session.GetString("TechZone.Voucher"));
        await GanUuDaiThanhVienAsync(vm.GioHang);
        await GanGiamGiaComboAsync(vm.GioHang);
        return View(vm);
    }

    /// <summary>
    /// Xem truoc giam gia combo o checkout (Sprint 3 — B-7): chi de HIEN THI.
    /// So tien that do CheckoutService tinh lai tu DB khi POST; neu combo
    /// khong con hop le (het han/tat/thieu mon) thi de 0, POST se bao loi.
    /// </summary>
    private async Task GanGiamGiaComboAsync(CartViewModel gioHang)
    {
        var raw = HttpContext.Session.GetString("TechZone.ComboId");
        if (!int.TryParse(raw, out var comboId)) return;
        var ketQua = await _gia.TinhGiaComboAsync(comboId, gioHang.Lines);
        if (ketQua.HopLe) gioHang.GiamGiaCombo = ketQua.GiamGia;
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
            await _cart.TinhGiamGiaAsync(gioHang, HttpContext.Session.GetString("TechZone.Voucher"));
            await GanUuDaiThanhVienAsync(gioHang);
            model.GioHang = gioHang;
            return View(model);
        }
        if (!ModelState.IsValid)
        {
            await _cart.TinhGiamGiaAsync(gioHang, HttpContext.Session.GetString("TechZone.Voucher"));
            await GanUuDaiThanhVienAsync(gioHang);
            model.GioHang = gioHang;
            return View(model);
        }

        await _cart.TinhGiamGiaAsync(gioHang, HttpContext.Session.GetString("TechZone.Voucher"));
        await GanUuDaiThanhVienAsync(gioHang);

        // Doi diem tich luy (nhom B): chi khi dang nhap va nhap diem > 0.
        var userId = LayUserIdHienTai();
        int diemDaDung = 0;
        if (userId is not null && model.DiemDung > 0)
        {
            var (giamDiem, loiDiem) = await _loyalty.DungDiemAsync(userId, model.DiemDung, gioHang.SauGiam);
            if (loiDiem is not null)
            {
                TempData["Error"] = loiDiem;
                model.GioHang = gioHang;
                return View(model);
            }
            gioHang.GiamGiaDiem = giamDiem;
            diemDaDung = (int)(giamDiem / 1_000m);
        }

        var ketQua = await _checkout.DatHangAsync(new CheckoutInput
        {
            UserId = userId,
            HoTenNhan = model.HoTenNhan,
            SoDienThoai = model.SoDienThoai,
            DiaChi = model.DiaChi,
            PhuongThucTT = model.PhuongThucTT,
            InstallmentMonths = model.InstallmentMonths is 6 or 12 ? model.InstallmentMonths : null,
            DiemDung = diemDaDung,
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
        });

        if (!ketQua.ThanhCong || ketQua.DonHang is null)
        {
            TempData["Error"] = ketQua.Loi ?? "Đặt hàng thất bại, vui lòng thử lại.";
            return RedirectToAction("Index", "Cart");
        }

        if (ketQua.LaDonCu)
        {
            TempData["Success"] = $"Đơn #{ketQua.DonHang.Id} đã được tạo trước đó.";
            return RedirectToAction(nameof(Details), new { id = ketQua.DonHang.Id });
        }

        _cart.Clear();
        HttpContext.Session.Remove("TechZone.Voucher");
        HttpContext.Session.Remove("TechZone.ComboId");
        TempData["Success"] = $"Đặt hàng thành công. Mã đơn: #{ketQua.DonHang.Id}.";
        return RedirectToAction(nameof(Details), new { id = ketQua.DonHang.Id });
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

    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .Include(o => o.LichSuTrangThai)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Timeline ve theo NgayCapNhat tang dan (nhom C).
        order.LichSuTrangThai = order.LichSuTrangThai.OrderBy(h => h.NgayCapNhat).ToList();

        // Don cua khach vang lai: ai co ma don thi xem duoc (demo).
        // Don cua tai khoan: chi chu don hoac Admin moi xem.
        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        if (order.UserId is not null && order.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();
        ViewBag.LaChuDon = order.UserId is not null && order.UserId == userId;
        return View(order);
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

    /// <summary>Hoa don VAT gia lap (nhom C): chu don hoac Admin; don khong xuat VAT thi ve Details.</summary>
    public async Task<IActionResult> HoaDon(int id)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .Include(o => o.ChiTiet).ThenInclude(d => d.Variant)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        var userId = User.Identity?.IsAuthenticated == true
            ? _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id
            : null;
        if (order.UserId is not null && order.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();
        if (string.IsNullOrEmpty(order.VatTenCongTy)
            || string.IsNullOrEmpty(order.VatMaSoThue)
            || string.IsNullOrEmpty(order.VatDiaChiCongTy))
        {
            // Don cu / don dat truoc khi co validate (Sprint 2 — B-10): hoa
            // don DN thieu 1 trong 3 thong tin thi khong xuat, ve Details.
            TempData["Error"] = "Đơn này không có đủ thông tin xuất hóa đơn VAT (thiếu tên công ty / mã số thuế / địa chỉ công ty).";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(order);
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
