using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Controllers;

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string MatKhau { get; set; } = string.Empty;

    public bool GhiNho { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required, MaxLength(100)]
    public string HoTen { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string SoDienThoai { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(6)]
    public string MatKhau { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(MatKhau))]
    public string NhapLaiMatKhau { get; set; } = string.Empty;
}

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly LoyaltyService _loyalty;
    private readonly IWebHostEnvironment _env;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, LoyaltyService loyalty, IWebHostEnvironment env)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _loyalty = loyalty;
        _env = env;
    }

    /// <summary>
    /// Quy tac EmailConfirmed khi dang ky (SEC-01 Sign-off): chi tu xac nhan
    /// o Development/demo vi chua co he thong gui email that; o Production
    /// de false — phai lam xac thuc email that truoc khi public.
    /// </summary>
    public static bool NenTuXacNhanEmail(IHostEnvironment env) => !env.IsProduction();

    /// <summary>
    /// Step 4 — thong bao khoa theo thoi gian khoa THUC TE (LockoutEnd),
    /// khong bao cung "5 phut" cho moi truong hop: tai khoan demo bi khoa
    /// toi 2126 nhung message cu van noi "thu lai sau 5 phut" la sai.
    /// Nguong ngan/dai: khoa con lai &lt;= 60 phut la khoa ngan (sai mat
    /// khau mac dinh 5 phut, xem Program.cs) -&gt; hien so phut con lai
    /// (lam tron len, toi thieu 1 phut); khoa dai hon 60 phut (khoa thu
    /// cong / khoa demo) -&gt; huong dan lien he quan tri vien, khong neu
    /// so phut. LockoutEnd null / da het han (khong nen xay ra khi
    /// IsLockedOut) cung roi ve nhanh lien he quan tri vien.
    /// </summary>
    public const int NguongKhoaNganHanPhut = 60;

    public static string ThongBaoKhoa(DateTimeOffset? lockoutEnd, DateTimeOffset hienTai)
    {
        if (lockoutEnd is { } hetHan)
        {
            var conLai = hetHan - hienTai;
            if (conLai > TimeSpan.Zero && conLai <= TimeSpan.FromMinutes(NguongKhoaNganHanPhut))
            {
                var phut = Math.Max(1, (int)Math.Ceiling(conLai.TotalMinutes));
                return $"Tài khoản tạm khóa do đăng nhập sai nhiều lần, thử lại sau {phut} phút.";
            }
        }
        return "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên để được hỗ trợ mở khóa.";
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _signInManager.PasswordSignInAsync(model.Email, model.MatKhau, model.GhiNho, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            var userBiKhoa = await _userManager.FindByEmailAsync(model.Email);
            ModelState.AddModelError(string.Empty, ThongBaoKhoa(userBiKhoa?.LockoutEnd, DateTimeOffset.UtcNow));
            return View(model);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
            return View(model);
        }
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null && await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Dashboard", "Admin");
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = new ApplicationUser
        {
            // EmailConfirmed chi tu dong true khi KHONG phai Production
            // (Development/demo — chua co he thong gui email xac thuc that).
            // O Production de false: phai lam xac thuc email that truoc khi
            // public (xem NenTuXacNhanEmail).
            UserName = model.Email, Email = model.Email,
            HoTen = model.HoTen, SoDienThoai = model.SoDienThoai, EmailConfirmed = NenTuXacNhanEmail(_env)
        };
        var result = await _userManager.CreateAsync(user, model.MatKhau);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await _userManager.AddToRoleAsync(user, "Khach");
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// Trang tai khoan (nhom B): hang thanh vien, diem, tien do len hang,
    /// 10 giao dich diem gan nhat.
    /// </summary>
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var hang = await _loyalty.HangAsync(user.Id);
        var tongDiem = await _loyalty.TongDiemAsync(user.Id);
        var tongChiTieu = await _loyalty.TongChiTieuAsync(user.Id);
        string? hangTiepTheo;
        decimal mucTiepTheo;
        switch (hang)
        {
            case "Bạc":
                hangTiepTheo = "Vàng";
                mucTiepTheo = _loyalty.NguongVang;
                break;
            case "Vàng":
                hangTiepTheo = "Bạch kim";
                mucTiepTheo = _loyalty.NguongBachKim;
                break;
            case "Bạch kim":
                hangTiepTheo = null;
                mucTiepTheo = 0;
                break;
            default:
                hangTiepTheo = "Bạc";
                mucTiepTheo = _loyalty.NguongBac;
                break;
        }
        ViewBag.Hang = hang;
        ViewBag.TongDiem = tongDiem;
        ViewBag.TongChiTieu = tongChiTieu;
        ViewBag.HangTiepTheo = hangTiepTheo;
        ViewBag.MucTiepTheo = mucTiepTheo;
        ViewBag.ConThieu = mucTiepTheo > 0
            ? Math.Max(0, mucTiepTheo - tongChiTieu)
            : 0;
        ViewBag.LichSuDiem = await _loyalty.LichSuAsync(user.Id, 10);
        return View(user);
    }
}
