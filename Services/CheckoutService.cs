using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>Mot dong hang trong gio de checkout (gia da tinh tu CartService).</summary>
public record CheckoutLineInput(int ProductId, int? VariantId, int SoLuong, decimal DonGia);

/// <summary>
/// Du lieu dau vao cho checkout. Giam diem / giam hang da duoc tinh truoc
/// o controller (VoucherService / LoyaltyService); GiamGia voucher CHI LA
/// GOI Y — service tinh lai tu DB bang VoucherService truoc khi tao don
/// (Sprint 2 — P1-1) nen gia tri gui len sai/lech se bi sua ve dung.
/// Service nay kiem tra lai gia tu DB, tru ton/voucher bang cap nhat co
/// dieu kien va ghi don trong 1 transaction duy nhat.
/// </summary>
public class CheckoutInput
{
    public string? UserId { get; set; }
    public string HoTenNhan { get; set; } = string.Empty;
    public string SoDienThoai { get; set; } = string.Empty;
    public string DiaChi { get; set; } = string.Empty;
    public string PhuongThucTT { get; set; } = "COD";
    public int? InstallmentMonths { get; set; }
    public int DiemDung { get; set; }
    public decimal GiamGiaDiem { get; set; }
    public bool XuatHoaDon { get; set; }
    public string? TenCongTy { get; set; }
    public string? MaSoThue { get; set; }
    public string? DiaChiCongTy { get; set; }
    public string? MaVoucher { get; set; }
    public decimal GiamGia { get; set; }
    public decimal GiamGiaHang { get; set; }

    /// <summary>
    /// Combo ap cho don nay (Sprint 3 — B-7). Null = khong dung combo.
    /// GiamGiaCombo CHI LA GOI Y — service tinh lai tu DB bang
    /// PriceCalculationService.TinhGiaComboAsync truoc khi tao don nen gia tri
    /// gui len sai/lech se bi sua ve dung (giong GiamGia voucher).
    /// </summary>
    public int? ComboId { get; set; }
    public decimal GiamGiaCombo { get; set; }

    public bool FreeShip { get; set; }
    public List<CheckoutLineInput> Lines { get; set; } = new();

    /// <summary>
    /// Key chong dat don trung (GUID sinh o form checkout, hidden field).
    /// Rong/null = client cu khong gui key: van dat duoc don nhung khong
    /// co bao ve replay (controller tu sinh 1 key neu thieu).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Ngay dat hang dung de chon thue suat VAT theo ngay hieu luc (Sprint 2
    /// — B-10). Null = bay gio. Controller khong set (don that lay gio dat);
    /// test set de kiem don truoc/sau ngay doi suat.
    /// </summary>
    public DateTime? NgayDat { get; set; }
}

public class CheckoutResult
{
    public bool ThanhCong { get; set; }
    public string? Loi { get; set; }
    public Order? DonHang { get; set; }

    /// <summary>True = don nay da ton tai tu lan POST truoc voi cung key, tra ve don cu.</summary>
    public bool LaDonCu { get; set; }

    public static CheckoutResult ThatBai(string loi) => new() { ThanhCong = false, Loi = loi };
    public static CheckoutResult Ok(Order don, bool laDonCu = false) =>
        new() { ThanhCong = true, DonHang = don, LaDonCu = laDonCu };
}

/// <summary>
/// Checkout an toan concurrency (Sprint 1 — P0, AUDIT P0-3/P1-6):
/// 1 transaction duy nhat cho: kiem tra lai gia tu DB, tru ton kho bang
/// UPDATE co dieu kien (TonKho &gt;= qty, 0 dong = het hang), tru luot
/// voucher bang UPDATE co dieu kien (DaDung &lt; SoLuong), tao don + chi tiet
/// + lich su trang thai + Spend diem + Earn diem + thong bao.
/// - GhiDiemMuaHangAsync / TaoThongBaoAsync tu goi SaveChangesAsync nhung
///   van nam trong cung transaction (cung DbContext + explicit transaction)
///   nen khong commit rieng.
/// - SQLite chi cho 1 writer tai 1 thoi diem: neu gap "database is locked"
///   thi rollback va thu lai toi da 10 lan (delay tang dan); lan thu lai
///   se thay ton kho/voucher da bi tru va tra loi sach se thay vi 500.
/// - Unique index Orders.IdempotencyKey la rao chan cuoi: neu 2 POST cung
///   key vuot qua buoc lookup, lenh INSERT thu 2 vi pham unique (code 19)
///   thi tra ve don cu thay vi loi.
/// </summary>
public class CheckoutService
{
    private static readonly string[] PhuongThucHopLe = { "COD", "ChuyenKhoan", "MoMo", "ZaloPay", "VNPay" };

    private readonly AppDbContext _db;
    private readonly LoyaltyService _loyalty;
    private readonly VoucherService _voucher;
    private readonly NotificationService _thongBao;
    private readonly PriceCalculationService _gia;
    private readonly VatService _vat;

    public CheckoutService(AppDbContext db, LoyaltyService loyalty, VoucherService voucher,
        NotificationService thongBao, PriceCalculationService gia, VatService vat)
    {
        _db = db;
        _loyalty = loyalty;
        _voucher = voucher;
        _thongBao = thongBao;
        _gia = gia;
        _vat = vat;
    }

    public async Task<CheckoutResult> DatHangAsync(CheckoutInput input)
    {
        var loiDauVao = KiemTraDauVao(input);
        if (loiDauVao is not null) return CheckoutResult.ThatBai(loiDauVao);

        // Thu lai toan bo transaction khi gap khoa ghi cua SQLite.
        for (var lan = 1; lan <= 10; lan++)
        {
            try
            {
                return await DatHangMotLanAsync(input);
            }
            catch (Exception ex) when (LaLoiKhoaDb(ex))
            {
                _db.ChangeTracker.Clear();
                if (lan == 10)
                    return CheckoutResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
                await Task.Delay(25 * lan);
            }
        }
        return CheckoutResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
    }

    private string? KiemTraDauVao(CheckoutInput input)
    {
        if (input.Lines.Count == 0) return "Giỏ hàng đang trống.";
        if (input.Lines.Any(l => l.SoLuong <= 0)) return "Số lượng sản phẩm không hợp lệ.";
        if (!PhuongThucHopLe.Contains(input.PhuongThucTT))
            return "Phương thức thanh toán không hợp lệ.";
        if (input.InstallmentMonths is not (null or 6 or 12))
            return "Trả góp chỉ nhận 6 hoặc 12 tháng.";
        if (string.IsNullOrWhiteSpace(input.HoTenNhan)
            || string.IsNullOrWhiteSpace(input.SoDienThoai)
            || string.IsNullOrWhiteSpace(input.DiaChi))
            return "Vui lòng nhập đủ họ tên, số điện thoại và địa chỉ.";
        if (input.DiemDung < 0) return "Số điểm dùng không hợp lệ.";
        if (input.ComboId is <= 0) return "Combo không hợp lệ.";
        if (input.DiemDung > 0 && input.UserId is null)
            return "Cần đăng nhập để dùng điểm tích lũy.";
        // Sprint 2 — B-10: tick xuat hoa don DN ma thieu thong tin thi tu choi
        // ngay (khong luu hoa don trong), ca o controller lan service.
        return _vat.KiemTraHoaDonDoanhNghiep(input.XuatHoaDon, input.TenCongTy,
            input.MaSoThue, input.DiaChiCongTy);
    }

    private async Task<CheckoutResult> DatHangMotLanAsync(CheckoutInput input)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. Idempotency: don da co key nay (cung user; khach vang lai thi
            // UserId deu null) -> tra ve don cu, khong tao moi, khong tru gi them.
            if (!string.IsNullOrEmpty(input.IdempotencyKey))
            {
                var donCu = await _db.Orders
                    .Include(o => o.ChiTiet)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.IdempotencyKey == input.IdempotencyKey
                        && o.UserId == input.UserId);
                if (donCu is not null)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.Ok(donCu, laDonCu: true);
                }
            }

            // 2. Kiem tra lai gia tu DB (khong tin DonGia client gui len).
            // Sprint 2 — B-5: gia chuan la ket qua cua PriceCalculationService
            // (Flash Sale / slot / gia goc), giong het gia gio hang hien thi.
            // Gia sale chi dung khi con quota cho TOAN BO so luong dong nay;
            // het quota thi bao loi rieng thay vi loi gia chung chung.
            // Ngay dat dung chung cho ca don: chon thue suat theo ngay hieu
            // luc + snapshot vao chi tiet (Sprint 2 — B-10).
            var ngayDat = input.NgayDat ?? DateTime.Now;
            var tamTinh = 0m;
            // Gia theo tung dong (cung thu tu input.Lines — khong key theo
            // ProductId vi 1 SP co the co 2 dong bien the khac nhau).
            var giaTheoDong = new List<GiaApDungResult>();
            // Giu lai Product/Variant de dung cho buoc voucher (tinh lai
            // giam gia theo pham vi) — khong load lai lan 2.
            var gioLines = new List<CartLineViewModel>();
            // Thue suat theo tung dong (cung thu tu input.Lines).
            var thueSuats = new List<decimal>();
            foreach (var line in input.Lines)
            {
                var product = await _db.Products.AsNoTracking()
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId);
                if (product is null)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai($"Sản phẩm #{line.ProductId} không còn tồn tại.");
                }
                decimal giaThem = 0;
                ProductVariant? variant = null;
                if (line.VariantId is not null)
                {
                    variant = await _db.ProductVariants.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == line.VariantId);
                    if (variant is null || variant.ProductId != line.ProductId)
                    {
                        await tx.RollbackAsync();
                        return CheckoutResult.ThatBai($"Phiên bản của {product.Ten} không hợp lệ.");
                    }
                    giaThem = variant.GiaThem;
                }
                var gia = await _gia.TinhGiaAsync(line.ProductId, giaThem, line.SoLuong);
                if (gia.HetQuota)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai(
                        $"Flash Sale cho {product.Ten} vừa hết suất, vui lòng kiểm tra lại giỏ hàng.");
                }
                var giaDung = gia.DonGia;
                if (line.DonGia != giaDung)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai(
                        $"Giá {product.Ten} đã thay đổi ({giaDung:N0}đ), vui lòng kiểm tra lại giỏ hàng.");
                }
                giaTheoDong.Add(gia);
                tamTinh += giaDung * line.SoLuong;
                gioLines.Add(new CartLineViewModel
                {
                    Product = product,
                    Variant = variant,
                    // DonGia = gia thuc thu cua dong nay (PriceCalculationService):
                    // voucher tinh pham vi tren gia nay; khong de mac dinh 0
                    // (tu Sprint 2 DonGia khong con tu tinh tu GiaBan nua).
                    DonGia = giaDung,
                    SoLuong = line.SoLuong
                });
                // Thue suat tai ngay dat (khong phai ngay cau hinh doi sau nay).
                thueSuats.Add(_vat.LayThueSuat(product.Category?.Slug, ngayDat));
            }

            // 2b. Combo (Sprint 3 — B-7): tinh lai TU DB bang PriceCalculationService
            // (khong tin GiamGiaCombo client gui len), roi giu suat bang UPDATE co
            // dieu kien — giong tru ton/voucher: 0 dong = suat cuoi vua bi don khac
            // lay giua buoc kiem tra va buoc nay -> huy toan bo don. Dat truoc
            // buoc tru ton kho de don loi combo khong tru ton vo ich (rollback
            // cung khoi phuc het, nhung fail som thi sach hon). Du hang tung mon
            // van do buoc 3 dam bao (UPDATE ton kho co dieu kien theo tung dong).
            var giamGiaCombo = 0m;
            var comboSoBo = 0;
            if (input.ComboId.HasValue)
            {
                var ketQuaCombo = await _gia.TinhGiaComboAsync(
                    input.ComboId.Value, gioLines, ngayDat);
                if (!ketQuaCombo.HopLe)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai(ketQuaCombo.Loi ?? "Combo không áp dụng được.");
                }
                var soDongCombo = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Combos\" SET \"DaBan\" = \"DaBan\" + {0} "
                    + "WHERE \"Id\" = {1} AND (\"GioiHanSL\" IS NULL OR \"DaBan\" + {2} <= \"GioiHanSL\")",
                    ketQuaCombo.SoBo, input.ComboId.Value, ketQuaCombo.SoBo);
                if (soDongCombo == 0)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai("Combo vừa hết suất, vui lòng kiểm tra lại giỏ hàng.");
                }
                giamGiaCombo = ketQuaCombo.GiamGia;
                comboSoBo = ketQuaCombo.SoBo;
            }

            // 3. Tru ton kho + tang luot ban bang UPDATE co dieu kien.
            // 0 dong bi anh huong = het hang (ke ca khi 2 checkout dong thoi
            // tranh nhau don vi cuoi cung): huy toan bo transaction.
            // Sprint 2 — B-5: QUYET DINH GHI LUOT BAN: DaBan (FlashSale) va
            // LuotBan (Product) ghi NGAY KHI TAO DON (ChoXacNhan), trong
            // cung transaction nay, va HOAN TAC khi huy don (OrderService).
            // Ly do: quota sale la tai nguyen khan — phai giu cho ngay luc
            // checkout, neu doi toi "DaXacNhan" thi N don ChoXacNhan co the
            // cung huong gia sale va vuot GioiHanSL truoc khi admin duyet;
            // LuotBan giu nhat quan voi TonKho (da tru ngay khi tao don tu
            // Sprint 1). Them vao gio KHONG ghi luot ban (chi doc gia).
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var soDong = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Products\" SET \"TonKho\" = \"TonKho\" - {0}, \"LuotBan\" = \"LuotBan\" + {1} "
                    + "WHERE \"Id\" = {2} AND \"TonKho\" >= {3}",
                    line.SoLuong, line.SoLuong, line.ProductId, line.SoLuong);
                if (soDong == 0)
                {
                    await tx.RollbackAsync();
                    var ten = await _db.Products.AsNoTracking()
                        .Where(p => p.Id == line.ProductId)
                        .Select(p => p.Ten).FirstOrDefaultAsync();
                    return CheckoutResult.ThatBai(
                        $"{ten ?? "Sản phẩm"} vừa hết hàng, vui lòng kiểm tra lại giỏ hàng.");
                }

                // Tru quota Flash Sale bang UPDATE co dieu kien (giong tru
                // ton/voucher Sprint 1): 0 dong = suat cuoi vua bi don khac
                // lay giua buoc kiem gia va buoc nay -> huy toan bo don.
                // Chi dong nao that su duoc gia sale (co FlashSaleId) moi tru.
                var flashSaleId = giaTheoDong[i].FlashSaleId;
                if (flashSaleId is not null)
                {
                    var soDongSale = await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE \"FlashSales\" SET \"DaBan\" = \"DaBan\" + {0} "
                        + "WHERE \"Id\" = {1} AND \"DaBan\" + {2} <= \"GioiHanSL\"",
                        line.SoLuong, flashSaleId.Value, line.SoLuong);
                    if (soDongSale == 0)
                    {
                        await tx.RollbackAsync();
                        var ten = await _db.Products.AsNoTracking()
                            .Where(p => p.Id == line.ProductId)
                            .Select(p => p.Ten).FirstOrDefaultAsync();
                        return CheckoutResult.ThatBai(
                            $"Flash Sale cho {ten ?? "sản phẩm"} vừa hết suất, vui lòng kiểm tra lại giỏ hàng.");
                    }
                }
            }

            // 4. Voucher: TINH LAI tu DB bang chung 1 ham VoucherService
            // (Sprint 2 — P1-1: % chi tren phan du dieu kien), khong tin
            // GiamGia controller gui len — gia tri cu (vd 10% toan bo gio)
            // se bi sua ve dung truoc khi tao don. Khong hop le thi tu choi
            // don thay vi am tham giam 0 (voucher co the het han / het luot
            // / doi hang giua luc xem gio va luc dat). Sau do tru luot bang
            // UPDATE co dieu kien (DaDung < SoLuong) nhu cu.
            // Khong co ma voucher: giam voucher = 0, bo qua GiamGia gui len.
            var giamGia = 0m;
            var freeShip = input.FreeShip;
            if (!string.IsNullOrEmpty(input.MaVoucher))
            {
                var ketQuaVoucher = await _voucher.KiemTraAsync(
                    input.MaVoucher, input.UserId, tamTinh, gioLines);
                if (!ketQuaVoucher.HopLe)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai(ketQuaVoucher.Loi
                        ?? $"Mã {input.MaVoucher} không áp dụng được cho đơn này.");
                }
                giamGia = ketQuaVoucher.GiamGia;
                freeShip = freeShip || ketQuaVoucher.FreeShip;
                var soDong = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Vouchers\" SET \"DaDung\" = \"DaDung\" + 1 "
                    + "WHERE \"Ma\" = {0} AND \"DaDung\" < \"SoLuong\"",
                    input.MaVoucher.Trim().ToUpper());
                if (soDong == 0)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai($"Mã {input.MaVoucher} vừa hết lượt dùng.");
                }
            }

            // 5. Diem Spend: kiem tra lai so du trong transaction truoc khi ghi.
            if (input.DiemDung > 0 && input.UserId is not null)
            {
                var soDu = await _loyalty.TongDiemAsync(input.UserId);
                if (input.DiemDung > soDu)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai($"Bạn chỉ có {soDu:N0} điểm.");
                }
            }

            var sauGiam = Math.Max(0, tamTinh - giamGia - input.GiamGiaDiem - input.GiamGiaHang - giamGiaCombo);
            var phiShip = freeShip || sauGiam >= 2_000_000 || tamTinh == 0 ? 0 : 30_000;

            var order = new Order
            {
                UserId = input.UserId,
                TrangThai = OrderStatus.ChoXacNhan,
                DiaChiGiao = $"{input.HoTenNhan.Trim()}, {input.SoDienThoai.Trim()}, {input.DiaChi.Trim()}",
                PhuongThucTT = input.PhuongThucTT,
                NgayDat = ngayDat,
                MaVoucher = input.MaVoucher,
                GiamGia = giamGia,
                PhiShip = phiShip,
                TongTien = sauGiam,
                InstallmentMonths = input.InstallmentMonths,
                DiemDaDung = input.DiemDung,
                GiamGiaDiem = input.GiamGiaDiem,
                GiamGiaHang = input.GiamGiaHang,
                // Snapshot combo (Sprint 3 — B-7): Id + so tien giam + so bo da
                // tru suat; don cu khong doi khi combo doi gia/tat sau nay.
                ComboId = input.ComboId,
                GiamGiaCombo = giamGiaCombo,
                ComboSoBo = comboSoBo,
                VatTenCongTy = input.XuatHoaDon ? input.TenCongTy : null,
                VatMaSoThue = input.XuatHoaDon ? input.MaSoThue : null,
                VatDiaChiCongTy = input.XuatHoaDon ? input.DiaChiCongTy : null,
                IdempotencyKey = input.IdempotencyKey,
            };
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var gia = giaTheoDong[i];
                order.ChiTiet.Add(new OrderDetail
                {
                    ProductId = line.ProductId,
                    VariantId = line.VariantId,
                    // Snapshot gia (Sprint 2 — B-5): DonGia la gia thuc thu,
                    // GiaGoc/TenKhuyenMai/FlashSaleId khoa lai khuyen mai da
                    // ap — don cu / hoa don khong doi khi gia SP doi sau nay,
                    // va huy don biet chinh xac dong nao can hoan DaBan.
                    DonGia = line.DonGia,
                    GiaGoc = gia.GiaGoc,
                    TenKhuyenMai = gia.TenKhuyenMai,
                    FlashSaleId = gia.FlashSaleId,
                    SoLuong = line.SoLuong,
                    // Snapshot thue suat luc dat (Sprint 2 — B-10).
                    ThueSuatVat = thueSuats[i],
                });
            }
            // Tach VAT tren phan sau giam (gia da gom VAT): TienVat tung dong
            // + TongVat; tong tien khach tra KHONG doi (khong cong them VAT).
            var bangVat = _vat.TinhVatDon(order);
            for (var i = 0; i < order.ChiTiet.Count; i++)
                order.ChiTiet.ElementAt(i).TienVat = bangVat.Lines[i].TienVat;
            order.TongVat = bangVat.TongVat;
            _db.Orders.Add(order);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (LaLoiTrungIdempotencyKey(ex))
            {
                // Rao chan cuoi: 2 POST cung key vuot qua buoc lookup (chay
                // song song) -> INSERT thu 2 vi pham unique index. Rollback
                // roi tra ve don cu thay vi bao loi.
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                var donCu = await _db.Orders
                    .Include(o => o.ChiTiet)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.IdempotencyKey == input.IdempotencyKey
                        && o.UserId == input.UserId);
                return donCu is not null
                    ? CheckoutResult.Ok(donCu, laDonCu: true)
                    : CheckoutResult.ThatBai("Đơn hàng đã được tạo trước đó, vui lòng kiểm tra lại.");
            }

            // 6. Lich su trang thai + Spend diem + Earn diem + thong bao:
            // tat ca trong CUNG transaction nay (SaveChanges cua
            // LoyaltyService/NotificationService khong commit rieng).
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                TrangThai = OrderStatus.ChoXacNhan,
                NgayCapNhat = DateTime.Now
            });
            if (input.DiemDung > 0 && input.UserId is not null)
            {
                // Tru diem race-safe (Sprint 2 — Mang 5): INSERT Spend co
                // dieu kien so du trong 1 cau lenh; 0 dong = het diem that
                // (vd 2 checkout cung luc tranh cung so diem) -> rollback
                // toan bo don thay vi de so du am.
                var (truOk, loiTru) = await _loyalty.TruDiemAsync(
                    input.UserId, input.DiemDung, order.Id);
                if (!truOk)
                {
                    await tx.RollbackAsync();
                    return CheckoutResult.ThatBai(loiTru ?? "Không trừ được điểm tích lũy.");
                }
            }
            await _db.SaveChangesAsync();
            await _loyalty.GhiDiemMuaHangAsync(order);
            if (input.UserId is not null)
            {
                await _thongBao.TaoThongBaoAsync(input.UserId,
                    $"Đặt hàng thành công — Đơn #{order.Id}",
                    $"Đơn hàng #{order.Id} ({order.ThanhToan:N0}đ) đang chờ TechZone xác nhận.");
            }

            await tx.CommitAsync();
            return CheckoutResult.Ok(order);
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* da rollback */ }
            throw;
        }
    }

    /// <summary>
    /// SQLite Error 5 (SQLITE_BUSY / "database is locked"): chi co 1 writer
    /// tai 1 thoi diem. Di doc ca chuoi InnerException vi ExecuteSqlRawAsync
    /// nem SqliteException truc tiep, con SaveChangesAsync boc trong
    /// DbUpdateException.
    /// </summary>
    private static bool LaLoiKhoaDb(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException se && se.SqliteErrorCode == 5) return true;
            if (e.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool LaLoiTrungIdempotencyKey(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException se && se.SqliteErrorCode == 19 // SQLITE_CONSTRAINT
                && se.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
