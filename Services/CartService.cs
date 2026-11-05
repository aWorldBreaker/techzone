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

    public CartService(IHttpContextAccessor http, AppDbContext db)
    {
        _http = http;
        _db = db;
    }

    private ISession Session => _http.HttpContext!.Session;

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
    /// Tinh giam gia theo ma voucher: tra ve so tien giam va gan vao ViewModel.
    /// Voucher het han, het luot hoac don chua du dieu kien thi giam 0.
    /// </summary>
    public async Task TinhGiamGiaAsync(CartViewModel vm, string? ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return;
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Ma == ma);
        if (voucher is null || !voucher.ConHieuLuc(DateTime.Now) || vm.TamTinh < voucher.DieuKien) return;
        vm.MaVoucher = voucher.Ma;
        vm.GiamGia = voucher.LoaiGiam == "PhanTram"
            ? Math.Round(vm.TamTinh * voucher.GiaTri / 100)
            : Math.Min(voucher.GiaTri, vm.TamTinh);
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
            vm.Lines.Add(new CartLineViewModel
            {
                Product = product,
                Variant = variant,
                SoLuong = line.SoLuong
            });
        }
        return vm;
    }
}
