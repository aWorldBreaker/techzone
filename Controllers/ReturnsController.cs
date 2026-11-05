using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

/// <summary>
/// Doi tra + bao hanh online (nhom C, gia lap).
/// Bao hanh 24 thang tu ngay dat (xem TraCuu).
/// </summary>
public class ReturnsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ReturnService _doiTra;

    /// <summary>Trang thai admin duoc duyet (khong cho quay lai "Chờ duyệt").</summary>
    private static readonly string[] TrangThaiDuyet = { "Đã duyệt", "Từ chối", "Hoàn thành" };

    public ReturnsController(AppDbContext db, IWebHostEnvironment env, ReturnService doiTra)
    {
        _db = db;
        _env = env;
        _doiTra = doiTra;
    }

    private string? LayUserIdHienTai()
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        return _db.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id;
    }

    /// <summary>Don cua user hien tai kem chi tiet (de kiem tra SP co trong don).</summary>
    private Task<Order?> LayDonCuaUserAsync(int orderId, string? userId) =>
        _db.Orders.Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

    /// <summary>
    /// Sprint 3 — Admin: chi doi tra don DangGiao / DaGiao (giong dieu kien
    /// duyet o ReturnService). Don chua giao thi khach huy don thay vi doi
    /// tra; don DaHuy khong hoi sinh. Chan ngay luc tao yeu cau (GET hien
    /// form + POST luu), khong doi toi luc Admin duyet moi tu choi.
    /// </summary>
    private static bool DonDuocDoiTraQ(Order order) =>
        order.TrangThai is OrderStatus.DangGiao or OrderStatus.DaGiao;

    // --- Yeu cau cua toi ---
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userId = LayUserIdHienTai();
        ViewBag.BaoHanh = await _db.WarrantyClaims
            .Include(w => w.Product).Include(w => w.Order)
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.NgayTao).ToListAsync();
        var list = await _db.ReturnRequests
            .Include(r => r.Product).Include(r => r.Order)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.NgayTao).ToListAsync();
        return View(list);
    }

    // --- Doi tra ---
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Create(int orderId, int productId = 0)
    {
        var order = await LayDonCuaUserAsync(orderId, LayUserIdHienTai());
        if (order is null) return NotFound();
        if (!DonDuocDoiTraQ(order))
        {
            TempData["Error"] = order.TrangThai == OrderStatus.DaHuy
                ? "Đơn này đã hủy, không đổi trả được."
                : $"Chỉ đổi trả được đơn đang giao hoặc đã giao (đơn hiện “{order.TrangThai}”). Đơn chưa giao thì hủy đơn thay vì đổi trả.";
            return RedirectToAction("Details", "Orders", new { id = orderId });
        }
        // Chua chon SP: view hien danh sach SP trong don de chon.
        if (productId == 0)
        {
            ViewBag.Order = order;
            return View(new ReturnRequest { OrderId = orderId });
        }
        var detail = order.ChiTiet.FirstOrDefault(d => d.ProductId == productId);
        if (detail is null) return NotFound();
        ViewBag.Order = order;
        ViewBag.Product = detail.Product;
        return View(new ReturnRequest { OrderId = orderId, ProductId = productId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int orderId, int productId, string lyDo, IFormFile? anhKem)
    {
        var userId = LayUserIdHienTai();
        var order = await LayDonCuaUserAsync(orderId, userId);
        if (order is null) return NotFound();
        if (!DonDuocDoiTraQ(order))
        {
            TempData["Error"] = order.TrangThai == OrderStatus.DaHuy
                ? "Đơn này đã hủy, không đổi trả được."
                : $"Chỉ đổi trả được đơn đang giao hoặc đã giao (đơn hiện “{order.TrangThai}”). Đơn chưa giao thì hủy đơn thay vì đổi trả.";
            return RedirectToAction("Details", "Orders", new { id = orderId });
        }
        if (!order.ChiTiet.Any(d => d.ProductId == productId)) return NotFound();
        if (string.IsNullOrWhiteSpace(lyDo))
        {
            TempData["Error"] = "Vui lòng nhập lý do đổi trả.";
            return RedirectToAction(nameof(Create), new { orderId, productId });
        }

        // Anh kem: chi nhan anh, toi da 5MB (mau AdminController.ProductForm).
        string? anhUrl = null;
        if (anhKem is not null && anhKem.Length > 0)
        {
            var duoi = Path.GetExtension(anhKem.FileName).ToLowerInvariant();
            var duoiHopLe = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!duoiHopLe.Contains(duoi))
            {
                TempData["Error"] = "Chỉ nhận ảnh .jpg, .jpeg, .png hoặc .webp.";
                return RedirectToAction(nameof(Create), new { orderId, productId });
            }
            if (anhKem.Length > 5 * 1024 * 1024)
            {
                TempData["Error"] = "Ảnh tối đa 5MB.";
                return RedirectToAction(nameof(Create), new { orderId, productId });
            }
            var tenFile = $"{Guid.NewGuid():N}{duoi}";
            var thuMuc = Path.Combine(_env.WebRootPath, "images");
            Directory.CreateDirectory(thuMuc);
            await using var stream = new FileStream(Path.Combine(thuMuc, tenFile), FileMode.Create);
            await anhKem.CopyToAsync(stream);
            anhUrl = $"/images/{tenFile}";
        }

        _db.ReturnRequests.Add(new ReturnRequest
        {
            OrderId = orderId,
            ProductId = productId,
            UserId = userId,
            LyDo = lyDo.Trim(),
            AnhUrl = anhUrl,
            TrangThai = "Chờ duyệt",
            NgayTao = DateTime.Now
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã gửi yêu cầu đổi trả. TechZone sẽ phản hồi sớm.";
        return RedirectToAction(nameof(Index));
    }

    // --- Bao hanh ---
    // Sprint 4 — QA P1 (audit P1-17): chi bao hanh don DA GIAO (khach da
    // nhan hang moi mo ta duoc loi). Don chua giao thi khach huy don thay
    // vi bao hanh; don DaHuy khong hoi sinh. Chan ngay luc tao yeu cau
    // (GET hien form + POST luu), giong guard doi tra o Create.
    private static bool DonDuocBaoHanhQ(Order order) =>
        order.TrangThai is OrderStatus.DaGiao;

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Warranty(int orderId, int productId = 0)
    {
        var order = await LayDonCuaUserAsync(orderId, LayUserIdHienTai());
        if (order is null) return NotFound();
        if (!DonDuocBaoHanhQ(order))
        {
            TempData["Error"] = order.TrangThai == OrderStatus.DaHuy
                ? "Đơn này đã hủy, không bảo hành được."
                : $"Chỉ bảo hành được đơn đã giao (đơn hiện “{order.TrangThai}”). Đơn chưa giao thì hủy đơn thay vì bảo hành.";
            return RedirectToAction("Details", "Orders", new { id = orderId });
        }
        if (productId == 0)
        {
            ViewBag.Order = order;
            return View(new WarrantyClaim { OrderId = orderId });
        }
        var detail = order.ChiTiet.FirstOrDefault(d => d.ProductId == productId);
        if (detail is null) return NotFound();
        ViewBag.Order = order;
        ViewBag.Product = detail.Product;
        return View(new WarrantyClaim { OrderId = orderId, ProductId = productId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Warranty(int orderId, int productId, string moTaLoi)
    {
        var userId = LayUserIdHienTai();
        var order = await LayDonCuaUserAsync(orderId, userId);
        if (order is null) return NotFound();
        if (!DonDuocBaoHanhQ(order))
        {
            TempData["Error"] = order.TrangThai == OrderStatus.DaHuy
                ? "Đơn này đã hủy, không bảo hành được."
                : $"Chỉ bảo hành được đơn đã giao (đơn hiện “{order.TrangThai}”). Đơn chưa giao thì hủy đơn thay vì bảo hành.";
            return RedirectToAction("Details", "Orders", new { id = orderId });
        }
        if (!order.ChiTiet.Any(d => d.ProductId == productId)) return NotFound();
        if (string.IsNullOrWhiteSpace(moTaLoi))
        {
            TempData["Error"] = "Vui lòng mô tả lỗi của sản phẩm.";
            return RedirectToAction(nameof(Warranty), new { orderId, productId });
        }
        _db.WarrantyClaims.Add(new WarrantyClaim
        {
            OrderId = orderId,
            ProductId = productId,
            UserId = userId,
            MoTaLoi = moTaLoi.Trim(),
            TrangThai = "Chờ duyệt",
            NgayTao = DateTime.Now
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã gửi yêu cầu bảo hành. TechZone sẽ phản hồi sớm.";
        return RedirectToAction(nameof(Index));
    }

    // --- Chi tiet 1 yeu cau: chu yeu cau hoac Admin moi xem ---
    public async Task<IActionResult> Details(int id, string loai = "doitra")
    {
        var userId = LayUserIdHienTai();
        var laAdmin = User.IsInRole("Admin");
        if (loai == "baohanh")
        {
            var claim = await _db.WarrantyClaims
                .Include(w => w.Product).Include(w => w.Order)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (claim is null) return NotFound();
            if (claim.UserId != userId && !laAdmin) return Forbid();
            ViewBag.Loai = "baohanh";
            return View((object)claim);
        }
        var yeuCau = await _db.ReturnRequests
            .Include(r => r.Product).Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (yeuCau is null) return NotFound();
        if (yeuCau.UserId != userId && !laAdmin) return Forbid();
        ViewBag.Loai = "doitra";
        return View((object)yeuCau);
    }

    // --- Admin: tat ca yeu cau, loc theo trang thai ---
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminIndex(string? trangThai)
    {
        ViewBag.TrangThai = trangThai;
        var queryDoiTra = _db.ReturnRequests
            .Include(r => r.Product).Include(r => r.Order).Include(r => r.User)
            .AsQueryable();
        var queryBaoHanh = _db.WarrantyClaims
            .Include(w => w.Product).Include(w => w.Order).Include(w => w.User)
            .AsQueryable();
        if (!string.IsNullOrEmpty(trangThai))
        {
            queryDoiTra = queryDoiTra.Where(r => r.TrangThai == trangThai);
            queryBaoHanh = queryBaoHanh.Where(w => w.TrangThai == trangThai);
        }
        ViewBag.BaoHanh = await queryBaoHanh.OrderByDescending(w => w.NgayTao).ToListAsync();
        return View(await queryDoiTra.OrderByDescending(r => r.NgayTao).ToListAsync());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duyet(int id, string loai, string trangThaiMoi)
    {
        if (!TrangThaiDuyet.Contains(trangThaiMoi))
            return BadRequest("Trạng thái không hợp lệ.");
        if (loai == "baohanh")
        {
            var claim = await _db.WarrantyClaims.FindAsync(id);
            if (claim is null) return NotFound();
            claim.TrangThai = trangThaiMoi;
        }
        else
        {
            // Sprint 2 — Mang 4 (B-9): duyet doi tra di kem hoan tac day du
            // (ton/diem/voucher/hoan tien theo ti le dong tra) trong
            // ReturnService; tu choi thi chi doi trang thai, khong hoan tac.
            var yeuCau = await _db.ReturnRequests.FindAsync(id);
            if (yeuCau is null) return NotFound();
            var ketQua = await _doiTra.DuyetDoiTraAsync(id, trangThaiMoi);
            if (!ketQua.ThanhCong)
            {
                TempData["Error"] = ketQua.Loi ?? "Không cập nhật được yêu cầu đổi trả.";
                return RedirectToAction(nameof(AdminIndex));
            }
            TempData["Success"] = ketQua.DaXuLyTruocDo
                ? $"Yêu cầu #{id} đã xử lý trước đó."
                : ketQua.SoTienHoan > 0
                    ? $"Đã cập nhật trạng thái yêu cầu #{id}: {trangThaiMoi}. Đã ghi hoàn tiền {ketQua.SoTienHoan:N0}đ."
                    : $"Đã cập nhật trạng thái yêu cầu #{id}: {trangThaiMoi}.";
            return RedirectToAction(nameof(AdminIndex));
        }
        // Nhanh bao hanh: chi doi trang thai, khong hoan tac (bao hanh la sua chua, khong phai tra hang).
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật trạng thái yêu cầu #{id}: {trangThaiMoi}.";
        return RedirectToAction(nameof(AdminIndex));
    }

    // --- Tra cuu bao hanh theo ma don ---
    [HttpGet]
    public IActionResult TraCuu() => View("TraCuuBaoHanh", (Order?)null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TraCuu(int maDon)
    {
        var order = await _db.Orders
            .Include(o => o.ChiTiet).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == maDon);
        if (order is null)
        {
            TempData["Error"] = $"Không tìm thấy đơn #{maDon}.";
            return View("TraCuuBaoHanh", (Order?)null);
        }
        // Quy tac nhu Orders.Details: don cua tai khoan thi chi chu don hoac Admin.
        if (order.UserId is not null)
        {
            var userId = LayUserIdHienTai();
            if (order.UserId != userId && !User.IsInRole("Admin"))
            {
                TempData["Error"] = "Đơn này thuộc tài khoản khác. Vui lòng đăng nhập đúng tài khoản để tra cứu.";
                return View("TraCuuBaoHanh", (Order?)null);
            }
        }
        return View("TraCuuBaoHanh", order);
    }
}
