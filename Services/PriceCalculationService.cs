using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>Kết quả tính giá thống nhất cho 1 sản phẩm (1 đơn vị, chưa nhân số lượng).</summary>
public record GiaApDungResult
{
    /// <summary>Giá gốc 1 đơn vị (GiaBan + tiền thêm biến thể), chưa áp sale.</summary>
    public required decimal GiaGoc { get; init; }

    /// <summary>Giá khách thực trả cho 1 đơn vị (đã áp sale nếu có).</summary>
    public required decimal DonGia { get; init; }

    /// <summary>FlashSale đã cấp giá sale (null = không dùng giá Flash Sale).</summary>
    public int? FlashSaleId { get; init; }

    /// <summary>Tên khuyến mãi để snapshot vào OrderDetail (null = không sale).</summary>
    public string? TenKhuyenMai { get; init; }

    /// <summary>True = DonGia &lt; GiaGoc (đang được giá sale).</summary>
    public bool LaGiaSale => DonGia < GiaGoc;

    /// <summary>
    /// True = có đợt Flash Sale đang trong khung giờ cho SP này nhưng đã
    /// hết suất (DaBan + số lượng &gt; GioiHanSL). Checkout dùng cờ này để
    /// báo lỗi "vừa hết suất" thay vì lỗi giá chung chung.
    /// </summary>
    public bool HetQuota { get; init; }
}

/// <summary>Kết quả tính giá combo (Sprint 3 — B-7).</summary>
public record KetQuaCombo
{
    public required bool HopLe { get; init; }
    public string? Loi { get; init; }

    /// <summary>Số bộ đầy đủ có trong giỏ (0 = chưa đủ món).</summary>
    public int SoBo { get; init; }

    /// <summary>Tổng tiền 1 bộ (giá thực trả từng món, đã áp Flash Sale nếu có).</summary>
    public decimal TongTienBo { get; init; }

    /// <summary>Tiền giảm cho toàn bộ số bộ (làm tròn 1 lần trên tổng).</summary>
    public decimal GiamGia { get; init; }

    public static KetQuaCombo ThatBai(string loi) => new() { HopLe = false, Loi = loi };
}
/// Trước đây giá sale chỉ để hiển thị (Home/Details) còn giỏ + checkout
/// vẫn tính GiaBan, và DaBan không bao giờ tăng nên GioiHanSL vô nghĩa.
/// Từ nay Cart, Checkout, Order, Invoice đều lấy giá qua service này:
///
/// Thứ tự ưu tiên (đã chốt, không lấy min lẻ tẻ giữa 2 nguồn):
///  1. FlashSale (bảng FlashSales): khung giờ BatDau..KetThuc (bao cả 2
///     đầu, khớp FlashSale.DangChay) + còn suất (DaBan + soLuong &lt;=
///     GioiHanSL) + GiaSale thật sự rẻ hơn GiaBan. Đây là nguồn duy nhất
///     có quota; khi hết quota thì KHÔNG rơi xuống slot (quota là ràng
///     buộc cứng — nếu không, slot cùng khung giờ sẽ phá giới hạn suất).
///  2. Slot theo giờ (FlashSaleSlotItem): chỉ khi KHÔNG có đợt FlashSale
///     nào đang trong khung giờ cho SP (kể cả đợt đã hết suất), slot đang
///     chạy (giờ hiện tại trong [GioBatDau, GioKetThuc)) và GiaSale rẻ hơn
///     GiaBan. Slot không có quota nên không trừ DaBan.
///  3. Không có sale hợp lệ: giá gốc GiaBan (+ tiền thêm biến thể).
///
/// Service chỉ ĐỌC và TÍNH giá — không trừ tồn/quota (không tăng DaBan
/// khi thêm vào giỏ). Việc trừ quota là của CheckoutService bằng UPDATE
/// có điều kiện trong cùng transaction với trừ tồn kho (xem đó).
/// Tất cả các lần gọi đều nhận `thoiDiem`/`soLuong` tường minh để test
/// biên giờ/quota không phải chờ đúng giờ thật.
/// </summary>
public class PriceCalculationService
{
    private readonly AppDbContext _db;

    public PriceCalculationService(AppDbContext db) => _db = db;

    /// <summary>
    /// Giá 1 đơn vị cho SP (đã cộng tiền thêm biến thể vào cả GiaGoc lẫn
    /// DonGia). Mặc định soLuong = 1: giá sale chỉ áp khi đủ suất cho toàn
    /// bộ số lượng (all-or-nothing, khớp cách checkout từ chối khi vượt
    /// quota thay vì tính nửa sale nửa gốc).
    /// </summary>
    public async Task<GiaApDungResult> TinhGiaAsync(int productId, decimal giaThemBienThe = 0,
        int soLuong = 1, DateTime? thoiDiem = null)
    {
        var now = thoiDiem ?? DateTime.Now;
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null)
            throw new InvalidOperationException($"Sản phẩm #{productId} không còn tồn tại.");

        var giaGoc = product.GiaBan + giaThemBienThe;

        // 1. FlashSale trong khung giờ (kể cả đã hết suất — để báo HetQuota).
        // SQLite khong ORDER BY duoc cot decimal nen lay het ve roi sort
        // o client (so dong it: moi SP chi vai dot sale).
        var flashTrongGio = (await _db.FlashSales.AsNoTracking()
            .Where(f => f.ProductId == productId && f.BatDau <= now && f.KetThuc >= now)
            .ToListAsync())
            .OrderBy(f => f.GiaSale)
            .ToList();
        var flashReNhat = flashTrongGio
            .Where(f => f.GiaSale > 0 && f.GiaSale < product.GiaBan)
            .ToList();
        foreach (var flash in flashReNhat)
        {
            if (flash.DaBan + soLuong <= flash.GioiHanSL)
            {
                return new GiaApDungResult
                {
                    GiaGoc = giaGoc,
                    DonGia = flash.GiaSale + giaThemBienThe,
                    FlashSaleId = flash.Id,
                    TenKhuyenMai = "Flash Sale",
                };
            }
        }
        if (flashReNhat.Count > 0)
            // Mọi đợt giá rẻ trong giờ đều đã hết suất.
            return new GiaApDungResult { GiaGoc = giaGoc, DonGia = giaGoc, HetQuota = true };
        // Có đợt trong giờ nhưng GiaSale không rẻ hơn giá gốc (cấu hình sai):
        // coi như không có sale, không báo hết quota.
        if (flashTrongGio.Count > 0)
            return new GiaApDungResult { GiaGoc = giaGoc, DonGia = giaGoc };

        // 2. Slot theo giờ (chỉ khi không có đợt FlashSale nào trong giờ).
        // Sort o client (ly do nhu tren: SQLite khong ORDER BY decimal).
        var slotItem = (await _db.FlashSaleSlotItems.AsNoTracking()
            .Include(i => i.Slot)
            .Where(i => i.ProductId == productId && i.Slot.IsActive
                && now.Hour >= i.Slot.GioBatDau && now.Hour < i.Slot.GioKetThuc)
            .ToListAsync())
            .OrderBy(i => i.GiaSale)
            .FirstOrDefault();
        if (slotItem is not null && slotItem.GiaSale > 0 && slotItem.GiaSale < product.GiaBan)
        {
            return new GiaApDungResult
            {
                GiaGoc = giaGoc,
                DonGia = slotItem.GiaSale + giaThemBienThe,
                TenKhuyenMai = "Flash Sale theo khung giờ",
            };
        }

        // 3. Không sale.
        return new GiaApDungResult { GiaGoc = giaGoc, DonGia = giaGoc };
    }

    /// <summary>
    /// Gia combo (Sprint 3 — B-7): nguon DUY NHAT tinh giam gia combo —
    /// Admin/Checkout/Cart preview deu goi ham nay, khong tinh le o controller.
    /// Quy tac: % giam tren tong gia thuc tra cua 1 bo day du (gia tung mon
    /// lay lai qua TinhGiaAsync nen Flash Sale van duoc ap truoc khi giam %);
    /// so bo = so bo day du it nhat co trong gio (vd gio co 2 Xiaomi + 1 Buds
    /// thi chi du 1 bo). Giam gia = lam tron 1 lan tren tong (tong 1 bo x so bo).
    /// Ham chi DOC + TINH, khong tru quota (DaBan) — CheckoutService tru bang
    /// UPDATE co dieu kien trong cung transaction voi tru ton kho (xem do).
    /// Lines truyen vao CHI CAN Product/Variant/SoLuong — DonGia tren line
    /// KHONG duoc tin (tinh lai het tu DB), nen goi tu CheckoutService (line
    /// da tinh lai) hay tu preview (line tu gio) deu cho cung ket qua.
    /// Gioi han: neu 1 SP co 2 dong bien the khac nhau trong gio thi gia mon
    /// lay theo dong dau tien gap (bien the re nhat khong chac duoc chon);
    /// combo thuong mua theo bo mac dinh nen chap nhan cho do an.
    /// </summary>
    public async Task<KetQuaCombo> TinhGiaComboAsync(int comboId,
        List<CartLineViewModel> lines, DateTime? thoiDiem = null)
    {
        var now = thoiDiem ?? DateTime.Now;
        var combo = await _db.Combos.AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.Id == comboId);
        if (combo is null)
            return KetQuaCombo.ThatBai("Combo không còn tồn tại.");
        if (!combo.IsActive)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” đã tắt.");
        if (now < combo.BatDau)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” chưa bắt đầu.");
        if (now > combo.KetThuc)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” đã hết hạn.");
        if (combo.Items.Count == 0)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” chưa có sản phẩm nào.");

        // So bo day du + tong tien 1 bo (gia tung mon tinh lai tu DB).
        var soBo = int.MaxValue;
        var tongTienBo = 0m;
        foreach (var item in combo.Items)
        {
            var dongCungSp = lines.Where(l => l.Product.Id == item.ProductId).ToList();
            var tongSoLuong = dongCungSp.Sum(l => l.SoLuong);
            if (tongSoLuong < item.SoLuong)
                return KetQuaCombo.ThatBai(
                    $"Giỏ hàng chưa đủ món của combo “{combo.Ten}” (thiếu {item.Product.Ten}).");
            soBo = Math.Min(soBo, tongSoLuong / item.SoLuong);
            var dongDau = dongCungSp.First();
            var gia = await TinhGiaAsync(item.ProductId,
                dongDau.Variant?.GiaThem ?? 0, dongDau.SoLuong, now);
            if (gia.HetQuota)
                return KetQuaCombo.ThatBai(
                    $"Flash Sale cho {item.Product.Ten} vừa hết suất, vui lòng kiểm tra lại giỏ hàng.");
            tongTienBo += gia.DonGia * item.SoLuong;
        }
        if (soBo == int.MaxValue || soBo <= 0)
            return KetQuaCombo.ThatBai($"Giỏ hàng chưa đủ món của combo “{combo.Ten}”.");
        if (combo.GioiHanSL.HasValue && combo.DaBan + soBo > combo.GioiHanSL.Value)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” vừa hết suất.");
        if (combo.PhanTramGiam <= 0 || combo.PhanTramGiam >= 100)
            return KetQuaCombo.ThatBai($"Combo “{combo.Ten}” có mức giảm không hợp lệ.");

        var giamGia = Math.Round(tongTienBo * soBo * combo.PhanTramGiam / 100);
        return new KetQuaCombo
        {
            HopLe = true,
            SoBo = soBo,
            TongTienBo = tongTienBo,
            GiamGia = giamGia,
        };
    }
}
