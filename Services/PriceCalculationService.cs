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
/// <summary>Kết quả tính TỔNG ĐƠN thống nhất (P1 — giỏ = checkout, 2026-11-06).</summary>
public record KetQuaTongDon
{
    /// <summary>Tạm tính = Σ giá thực trả từng dòng (đã áp Flash Sale / slot).</summary>
    public required decimal TamTinh { get; init; }

    public decimal GiamGiaVoucher { get; init; }
    public string? MaVoucher { get; init; }
    public bool FreeShipVoucher { get; init; }

    /// <summary>Lỗi voucher (null = hợp lệ hoặc không nhập mã). Voucher lỗi không làm hỏng tổng: giảm 0.</summary>
    public string? LoiVoucher { get; init; }

    public decimal GiamGiaCombo { get; init; }
    public int? ComboId { get; init; }

    /// <summary>Lỗi combo (null = hợp lệ hoặc không chọn combo).</summary>
    public string? LoiCombo { get; init; }

    /// <summary>Giảm hạng: 2% trên TOÀN BỘ tạm tính, chỉ hạng Vàng / Bạch kim (chính sách LoyaltyService).</summary>
    public decimal GiamGiaHang { get; init; }

    /// <summary>Hạng thành viên lúc tính ("Đồng" nếu chưa đăng nhập).</summary>
    public required string Hang { get; init; }

    public bool FreeShipHang { get; init; }
    public decimal GiamGiaDiem { get; init; }

    /// <summary>Số điểm thật sự trừ (GiamGiaDiem / 1.000); 0 nếu không dùng điểm hoặc bị lỗi.</summary>
    public int DiemDungThucTe { get; init; }

    /// <summary>Lỗi đổi điểm (null = đổi được hoặc không dùng điểm).</summary>
    public string? LoiDiem { get; init; }

    /// <summary>Tạm tính sau mọi khoản giảm, không âm.</summary>
    public decimal SauGiam => Math.Max(0, TamTinh - GiamGiaVoucher - GiamGiaCombo - GiamGiaHang - GiamGiaDiem);

    public bool FreeShip => FreeShipVoucher || FreeShipHang;

    /// <summary>0 nếu miễn ship / SauGiam ≥ 2.000.000 / giỏ trống, còn lại 30.000 — giống CartViewModel.PhiShip.</summary>
    public decimal PhiShip => FreeShip || SauGiam >= 2_000_000 || TamTinh == 0 ? 0 : 30_000;

    public decimal TongCong => SauGiam + PhiShip;
}

/// <summary>
/// THỨ TỰ ÁP DỤNG GIÁ THỰC TẾ (đã đối chiếu với CheckoutService.DatHangAsync, 2026-11-06):
///  1. Giá từng dòng: FlashSale (trong giờ + còn quota) → slot theo giờ → giá gốc.
///     TamTinh = Σ DonGia × SoLuong. Đây là cơ số duy nhất cho mọi bước sau.
///  2. Voucher: % / tiền cố định chỉ trên PHẦN TIỀN ĐỦ ĐIỀU KIỆN (Brand/Category),
///     không phải toàn bộ tạm tính (VoucherService, Sprint 2 — P1-1). FreeShip riêng.
///  3. Combo: % trên tổng giá thực trả của 1 bộ đầy đủ (đã áp sale ở bước 1).
///  4. Giảm hạng: 2% trên TOÀN BỘ TamTinh (Vàng / Bạch kim), làm tròn 1 lần
///     (LoyaltyService.GiamGiaHangAsync). KHÔNG phải 2% trên số còn lại sau
///     voucher/combo — đây là chính sách hiện tại, giữ nguyên.
///  5. Điểm: 1 điểm = 1.000đ, chặn ở SỐ CÒN LẠI sau voucher + combo + hạng và
///     ở số dư điểm. (Trước đây POST chặn trước combo vì preview combo chưa
///     trừ ở POST — nay thống nhất chặn sau combo: khách giữ lại điểm thừa
///     thay vì bị trừ điểm cho phần combo đã giảm.)
///  6. SauGiam = max(0, TamTinh − voucher − combo − hạng − điểm); phí ship
///     0 nếu miễn ship (hạng Vàng / voucher freeship) hoặc SauGiam ≥ 2tr.
///  Lưu ý: thứ tự trên KHÁC thứ tự gợi ý "Flash Sale → combo → voucher →
///  điểm → giảm hạng → phí ship" ở chỗ voucher/combo/hạng đều tính ĐỘC LẬP
///  trên cơ số riêng của mình rồi CỘNG vào tổng giảm — vì vậy hoán đổi thứ tự
///  2/3/4 không đổi tổng tiền. Không tự đổi thứ tự nghiệp vụ khi chưa có
///  bằng chứng: giữ nguyên công thức cộng dồn này ở cả giỏ và checkout.
/// </summary>
public class PriceCalculationService
{
    private readonly AppDbContext _db;
    private readonly VoucherService _voucher;
    private readonly LoyaltyService _loyalty;

    public PriceCalculationService(AppDbContext db, VoucherService voucher, LoyaltyService loyalty)
    {
        _db = db;
        _voucher = voucher;
        _loyalty = loyalty;
    }

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

    /// <summary>
    /// Tính TỔNG ĐƠN duy nhất cho Giỏ + Checkout GET + Checkout POST
    /// (P1 — giỏ = checkout, 2026-11-06). Chỉ ĐỌC + TÍNH, không trừ tồn /
    /// quota / lượt voucher / điểm — việc trừ vẫn là của CheckoutService
    /// trong transaction khi tạo đơn. `lines` phải đã có DonGia thực trả
    /// (qua TinhGiaAsync / CartService) — hàm không tính lại giá dòng.
    /// Voucher / combo không hợp lệ thì giảm 0 + ghi Loi, không ném lỗi,
    /// để giỏ vẫn hiển thị được tổng; CheckoutService POST vẫn tự từ chối
    /// đơn khi voucher/combo thật sự không hợp lệ (bảo vệ ở tầng ghi đơn).
    /// </summary>
    public async Task<KetQuaTongDon> TinhTongDonAsync(List<CartLineViewModel> lines,
        string? userId, string? maVoucher = null, int diemDung = 0,
        int? comboId = null, DateTime? thoiDiem = null)
    {
        var dongAnToan = lines ?? new List<CartLineViewModel>();
        var tamTinh = dongAnToan.Sum(l => l.ThanhTien);

        // 2. Voucher (chỉ trên phần đủ điều kiện — xem VoucherService).
        decimal giamVoucher = 0;
        var freeShipVoucher = false;
        string? loiVoucher = null;
        string? maChuan = null;
        if (!string.IsNullOrWhiteSpace(maVoucher))
        {
            var kqVoucher = await _voucher.KiemTraAsync(maVoucher, userId, tamTinh, dongAnToan);
            if (kqVoucher.HopLe)
            {
                giamVoucher = kqVoucher.GiamGia;
                freeShipVoucher = kqVoucher.FreeShip;
                maChuan = maVoucher.Trim().ToUpper();
            }
            else
            {
                loiVoucher = kqVoucher.Loi;
            }
        }

        // 3. Combo (% trên giá thực trả của bộ đầy đủ).
        decimal giamCombo = 0;
        string? loiCombo = null;
        if (comboId.HasValue)
        {
            var kqCombo = await TinhGiaComboAsync(comboId.Value, dongAnToan, thoiDiem);
            if (kqCombo.HopLe) giamCombo = kqCombo.GiamGia;
            else loiCombo = kqCombo.Loi;
        }

        // 4. Giảm hạng: 2% trên TOÀN BỘ tạm tính (Vàng / Bạch kim).
        var hang = "Đồng";
        decimal giamHang = 0;
        var freeShipHang = false;
        if (userId is not null)
        {
            hang = await _loyalty.HangAsync(userId);
            giamHang = await _loyalty.GiamGiaHangAsync(userId, tamTinh);
            freeShipHang = await _loyalty.LaHangVangAsync(userId);
        }

        // 5. Điểm: chặn ở số còn lại sau voucher + combo + hạng.
        decimal giamDiem = 0;
        var diemThucTe = 0;
        string? loiDiem = null;
        if (diemDung > 0)
        {
            if (userId is null)
            {
                loiDiem = "Cần đăng nhập để dùng điểm tích lũy.";
            }
            else
            {
                var conLai = Math.Max(0, tamTinh - giamVoucher - giamCombo - giamHang);
                var (giam, loi) = await _loyalty.DungDiemAsync(userId, diemDung, conLai);
                if (loi is not null) loiDiem = loi;
                else
                {
                    giamDiem = giam;
                    diemThucTe = (int)(giam / 1_000m);
                }
            }
        }

        return new KetQuaTongDon
        {
            TamTinh = tamTinh,
            GiamGiaVoucher = giamVoucher,
            MaVoucher = maChuan,
            FreeShipVoucher = freeShipVoucher,
            LoiVoucher = loiVoucher,
            GiamGiaCombo = giamCombo,
            ComboId = comboId,
            LoiCombo = loiCombo,
            GiamGiaHang = giamHang,
            Hang = hang,
            FreeShipHang = freeShipHang,
            GiamGiaDiem = giamDiem,
            DiemDungThucTe = diemThucTe,
            LoiDiem = loiDiem,
        };
    }

    /// <summary>
    /// Tính tổng đơn (TinhTongDonAsync) rồi GÁN vào CartViewModel để giỏ và
    /// checkout hiển thị cùng một bộ số: GiamGia / MaVoucher / GiamGiaCombo /
    /// GiamGiaHang / GiamGiaDiem / FreeShip. CartViewModel.SauGiam / PhiShip /
    /// TongCong tự suy ra từ các field này nên khớp với KetQuaTongDon.
    /// Trả về KetQuaTongDon để controller lấy thêm Hang / lỗi voucher-combo-điểm.
    /// </summary>
    public async Task<KetQuaTongDon> ApDungVaoGioHangAsync(CartViewModel vm,
        string? userId, string? maVoucher = null, int diemDung = 0,
        int? comboId = null, DateTime? thoiDiem = null)
    {
        var ketQua = await TinhTongDonAsync(vm.Lines, userId, maVoucher, diemDung, comboId, thoiDiem);
        vm.GiamGia = ketQua.GiamGiaVoucher;
        vm.MaVoucher = ketQua.MaVoucher;
        vm.GiamGiaCombo = ketQua.GiamGiaCombo;
        vm.GiamGiaHang = ketQua.GiamGiaHang;
        vm.GiamGiaDiem = ketQua.GiamGiaDiem;
        vm.FreeShip = ketQua.FreeShip;
        return ketQua;
    }
}
