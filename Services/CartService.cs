using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>
/// Gio hang session: dung duoc cho ca khach vang lai (chua dang nhap).
/// Luu List&lt;CartLine&gt; dang JSON trong ISession, doc san pham tu DB moi lan
/// hien thi nen gia/ton kho luon moi nhat, khong khoa gia cu trong session.
/// </summary>
public class CartService
{
    private const string SessionKey = "TechZone.Cart";
    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;
    private readonly VoucherService _voucher;
    private readonly PriceCalculationService _gia;

    public CartService(IHttpContextAccessor http, AppDbContext db, VoucherService voucher,
        PriceCalculationService gia)
    {
        _http = http;
        _db = db;
        _voucher = voucher;
        _gia = gia;
    }

    private ISession Session => _http.HttpContext!.Session;

    /// <summary>UserId hien tai (null neu khach vang lai) — de kiem tra voucher theo hang thanh vien.</summary>
    private string? LayUserId()
    {
        var ten = _http.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(ten)) return null;
        return _db.Users.FirstOrDefault(u => u.UserName == ten)?.Id;
    }

    public List<CartLine> GetLines()
    {
        var json = Session.GetString(SessionKey);
        return string.IsNullOrEmpty(json)
            ? new List<CartLine>()
            : JsonSerializer.Deserialize<List<CartLine>>(json) ?? new List<CartLine>();
    }

    private void Save(List<CartLine> lines) =>
        Session.SetString(SessionKey, JsonSerializer.Serialize(lines));

    public void Add(int productId, int? variantId, int soLuong)
    {
        var lines = GetLines();
        var line = lines.FirstOrDefault(l => l.ProductId == productId && l.VariantId == variantId);
        if (line is null)
            lines.Add(new CartLine { ProductId = productId, VariantId = variantId, SoLuong = soLuong });
        else
            line.SoLuong += soLuong;
        Save(lines);
    }

    public void UpdateSoLuong(int productId, int? variantId, int soLuong)
    {
        var lines = GetLines();
        var line = lines.FirstOrDefault(l => l.ProductId == productId && l.VariantId == variantId);
        if (line is null) return;
        if (soLuong <= 0) lines.Remove(line);
        else line.SoLuong = soLuong;
        Save(lines);
    }

    public void Remove(int productId, int? variantId)
    {
        var lines = GetLines();
        lines.RemoveAll(l => l.ProductId == productId && l.VariantId == variantId);
        Save(lines);
    }

    public void Clear() => Session.Remove(SessionKey);

    public int Count() => GetLines().Sum(l => l.SoLuong);

    /// <summary>
    /// Tinh giam gia theo ma voucher qua VoucherService (nhom B): kiem tra
    /// han, luot dung, don toi thieu, hang/danh muc/hang thanh vien, freeship.
    /// Khong hop le thi giam 0, khong bao loi (loi chi tiet hien o ApDungVoucher).
    /// </summary>
    public async Task TinhGiamGiaAsync(CartViewModel vm, string? ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return;
        var ketQua = await _voucher.KiemTraAsync(ma, LayUserId(), vm.TamTinh, vm.Lines);
        if (!ketQua.HopLe) return;
        vm.MaVoucher = ma.Trim().ToUpper();
        vm.GiamGia = ketQua.GiamGia;
        vm.FreeShip = ketQua.FreeShip;
    }

    public async Task<CartViewModel> GetViewModelAsync()
    {
        var lines = GetLines();
        var vm = new CartViewModel();
        foreach (var line in lines)
        {
            var product = await _db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == line.ProductId);
            if (product is null) continue;
            var variant = line.VariantId is null ? null
                : await _db.ProductVariants.FindAsync(line.VariantId);
            // Gia hien thi = gia thuc tra (Sprint 2 — B-5): qua
            // PriceCalculationService, giong het gia checkout se thu.
            // Chi DOC gia — them vao gio / xem gio KHONG tru quota
            // (DaBan/LuotBan chi ghi khi dat hang, xem CheckoutService).
            var gia = await _gia.TinhGiaAsync(product.Id,
                variant?.GiaThem ?? 0, line.SoLuong);
            vm.Lines.Add(new CartLineViewModel
            {
                Product = product,
                Variant = variant,
                SoLuong = line.SoLuong,
                DonGia = gia.DonGia,
                GiaGoc = gia.GiaGoc,
                TenKhuyenMai = gia.TenKhuyenMai
            });
        }
        return vm;
    }
}
