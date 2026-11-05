using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

public class DuyetDoiTraResult
{
    public bool ThanhCong { get; set; }
    public string? Loi { get; set; }

    /// <summary>True = yeu cau da o trang thai dich truoc do (duyet 2 lan).</summary>
    public bool DaXuLyTruocDo { get; set; }

    /// <summary>So tien hoan da ghi (0 = khong hoan: chua thu tien / bi tu choi).</summary>
    public decimal SoTienHoan { get; set; }

    public static DuyetDoiTraResult Ok(bool daXuLyTruocDo = false, decimal soTienHoan = 0) =>
        new() { ThanhCong = true, DaXuLyTruocDo = daXuLyTruocDo, SoTienHoan = soTienHoan };
    public static DuyetDoiTraResult ThatBai(string loi) => new() { ThanhCong = false, Loi = loi };
}

/// <summary>
/// Duyet doi tra + hoan tac day du khi duoc duyet (Sprint 2 — Mang 4, B-9).
/// Thay the viec ReturnsController.Duyet doi trang thai "chay" truoc day
/// (chi doi chu, khong hoan tac gi) bang 1 transaction duyet + hoan tac.
///
/// Pham vi doi tra hien co (gioi han — xem Models/Refund.cs):
/// - 1 yeu cau = 1 dong SP trong don, tra TOAN BO so luong cua dong do
///   (ReturnRequest chua co cot SoLuong nen chua tra 1 phan so luong trong
///   1 dong; doi tra 1 PHAN don = tra 1 dong trong don nhieu dong).
/// - Chi duyet duoc don DangGiao / DaGiao (bang chuyen Sprint 1: DaHuy va
///   DaGiao la terminal — don DaHuy khong hoi sinh, don chua giao thi
///   khach huy don thay vi doi tra).
/// - Hoan tac theo ti le gia tri dong tra / tong gia tri hang cua don:
///   ton kho + LuotBan/DaBan theo dung so luong dong tra; Earn thu hoi
///   = floor(EarnDon * tiLe); Spend tra lai = floor(SpendDon * tiLe);
///   hoan tien = round(TongTien * tiLe) + PhiShip neu tra het don.
/// - Voucher: CHI hoan luot khi doi tra nay lam don duoc tra HET (moi dong
///   deu co yeu cau da duyet) — tra 1 phan don thi voucher van tinh la
///   da dung cho phan hang giu lai.
/// - Hoan tien CHI khi Order.DaThanhToan (tien da thu that); don COD chua
///   thu thi khong co dong hoan nao.
/// - Idempotent: UPDATE trang thai yeu cau co dieu kien (chi tu "Chờ duyệt");
///   duyet lan 2 tra thanh cong, khong hoan tac them; moi INSERT hoan tac
///   deu co guard tong (diem) / NOT EXISTS (hoan tien) rieng.
/// </summary>
public class ReturnService
{
    private readonly AppDbContext _db;
    private readonly VoucherService _voucher;
    private readonly LoyaltyService _loyalty;
    private readonly RefundService _refund;

    public ReturnService(AppDbContext db, VoucherService voucher,
        LoyaltyService loyalty, RefundService refund)
    {
        _db = db;
        _voucher = voucher;
        _loyalty = loyalty;
        _refund = refund;
    }

    public async Task<DuyetDoiTraResult> DuyetDoiTraAsync(int returnRequestId, string trangThaiMoi)
    {
        if (trangThaiMoi is not ("Đã duyệt" or "Từ chối" or "Hoàn thành"))
            return DuyetDoiTraResult.ThatBai("Trạng thái không hợp lệ.");

        for (var lan = 1; lan <= 10; lan++)
        {
            try
            {
                return await DuyetMotLanAsync(returnRequestId, trangThaiMoi);
            }
            catch (Exception ex) when (LaLoiKhoaDb(ex))
            {
                _db.ChangeTracker.Clear();
                if (lan == 10)
                    return DuyetDoiTraResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
                await Task.Delay(25 * lan);
            }
        }
        return DuyetDoiTraResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
    }

    private async Task<DuyetDoiTraResult> DuyetMotLanAsync(int returnRequestId, string trangThaiMoi)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var yeuCau = await _db.ReturnRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == returnRequestId);
            if (yeuCau is null)
            {
                await tx.RollbackAsync();
                return DuyetDoiTraResult.ThatBai("Không tìm thấy yêu cầu đổi trả.");
            }

            // Da o trang thai dich: idempotent, khong hoan tac them.
            if (yeuCau.TrangThai == trangThaiMoi)
            {
                await tx.RollbackAsync();
                return DuyetDoiTraResult.Ok(daXuLyTruocDo: true);
            }

            // "Đã duyệt" -> "Hoàn thành": chi doi chu, hoan tac da xong o lan duyet.
            var chiDoiChu = yeuCau.TrangThai == "Đã duyệt" && trangThaiMoi == "Hoàn thành";
            // Tu choi sau khi da duyet / sua yeu cau da ket thuc: cam.
            if (!chiDoiChu && yeuCau.TrangThai != "Chờ duyệt")
            {
                await tx.RollbackAsync();
                return DuyetDoiTraResult.ThatBai(
                    $"Yêu cầu đã ở trạng thái “{yeuCau.TrangThai}”, không thể chuyển sang “{trangThaiMoi}”.");
            }

            var canHoanTac = !chiDoiChu && trangThaiMoi != "Từ chối";
            Order? order = null;
            OrderDetail? detail = null;
            if (canHoanTac)
            {
                order = await _db.Orders.AsNoTracking()
                    .Include(o => o.ChiTiet)
                    .FirstOrDefaultAsync(o => o.Id == yeuCau.OrderId);
                if (order is null)
                {
                    await tx.RollbackAsync();
                    return DuyetDoiTraResult.ThatBai("Không tìm thấy đơn hàng của yêu cầu đổi trả.");
                }
                // Chi doi tra don DangGiao / DaGiao (Sprint 1: DaHuy la
                // terminal, khong hoi sinh; don chua giao thi huy don).
                if (order.TrangThai is not (OrderStatus.DangGiao or OrderStatus.DaGiao))
                {
                    await tx.RollbackAsync();
                    return DuyetDoiTraResult.ThatBai(
                        $"Chỉ đổi trả được đơn đang giao hoặc đã giao (đơn hiện “{order.TrangThai}”).");
                }
                detail = order.ChiTiet.FirstOrDefault(d => d.ProductId == yeuCau.ProductId);
                if (detail is null)
                {
                    await tx.RollbackAsync();
                    return DuyetDoiTraResult.ThatBai("Sản phẩm đổi trả không còn trong đơn hàng.");
                }
                // Dong nay da co yeu cau khac duoc duyet truoc do -> khong
                // hoan tac 2 lan cho cung 1 dong.
                var daTraDongNay = await _db.ReturnRequests.AsNoTracking()
                    .AnyAsync(r => r.OrderId == yeuCau.OrderId && r.ProductId == yeuCau.ProductId
                        && r.Id != yeuCau.Id && r.TrangThai != "Chờ duyệt" && r.TrangThai != "Từ chối");
                if (daTraDongNay)
                {
                    await tx.RollbackAsync();
                    return DuyetDoiTraResult.ThatBai("Dòng sản phẩm này đã được hoàn tác trước đó.");
                }
            }

            // UPDATE co dieu kien theo trang thai cu: 0 dong = co ai doi
            // truoc trong tich tac -> doc lai roi quyet dinh nhu OrderService.
            var soDong = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE \"ReturnRequests\" SET \"TrangThai\" = {0} WHERE \"Id\" = {1} AND \"TrangThai\" = {2}",
                trangThaiMoi, returnRequestId, yeuCau.TrangThai);
            if (soDong == 0)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                var hienTai = await _db.ReturnRequests.AsNoTracking()
                    .Where(r => r.Id == returnRequestId)
                    .Select(r => r.TrangThai)
                    .FirstOrDefaultAsync();
                if (hienTai == trangThaiMoi)
                    return DuyetDoiTraResult.Ok(daXuLyTruocDo: true);
                return DuyetDoiTraResult.ThatBai(
                    "Yêu cầu vừa được thay đổi trạng thái, vui lòng tải lại trang.");
            }

            var soTienHoan = 0m;
            if (canHoanTac && order is not null && detail is not null)
            {
                // 1 yeu cau = toan bo so luong cua dong do (chua co SoLuong).
                var soLuongTra = detail.SoLuong;
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Products\" SET \"TonKho\" = \"TonKho\" + {0}, "
                    + "\"LuotBan\" = \"LuotBan\" - {1} "
                    + "WHERE \"Id\" = {2} AND \"LuotBan\" >= {3}",
                    soLuongTra, soLuongTra, detail.ProductId, soLuongTra);
                if (detail.FlashSaleId is not null)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE \"FlashSales\" SET \"DaBan\" = \"DaBan\" - {0} "
                        + "WHERE \"Id\" = {1} AND \"DaBan\" >= {2}",
                        soLuongTra, detail.FlashSaleId.Value, soLuongTra);
                }

                // Ti le theo gia tri hang cua dong tra / tong gia tri hang.
                var tamTinhGoc = order.ChiTiet.Sum(d => d.DonGia * d.SoLuong);
                var tiLe = tamTinhGoc > 0
                    ? (detail.DonGia * soLuongTra) / tamTinhGoc
                    : 0m;

                // Don duoc tra HET khi moi dong deu co yeu cau da duyet
                // (tinh ca yeu cau nay — vua chuyen sang trang thai duyet).
                var dongDaDuyet = await _db.ReturnRequests.AsNoTracking()
                    .Where(r => r.OrderId == order.Id
                        && r.TrangThai != "Chờ duyệt" && r.TrangThai != "Từ chối")
                    .Select(r => r.ProductId)
                    .ToListAsync();
                var traHetDon = order.ChiTiet.All(d => dongDaDuyet.Contains(d.ProductId));

                if (order.UserId is not null)
                {
                    var earn = await _loyalty.EarnCuaDonAsync(order.Id);
                    var earnHoan = (int)Math.Floor(earn * tiLe);
                    if (earnHoan > 0)
                        await _loyalty.ThuHoiEarnAsync(order.UserId, order.Id, earnHoan);
                    var spend = await _loyalty.SpendCuaDonAsync(order.Id);
                    var spendHoan = (int)Math.Floor(spend * tiLe);
                    if (spendHoan > 0)
                        await _loyalty.HoanSpendAsync(order.UserId, order.Id, spendHoan);
                }
                // Voucher chi hoan luot khi tra HET don (tra 1 phan thi
                // voucher van tinh la da dung cho phan hang giu lai).
                if (traHetDon)
                    await _voucher.HoanLuotAsync(order.MaVoucher);

                // Hoan tien CHI khi tien da thu that; tra 1 phan thi khong
                // hoan PhiShip, tra het don thi hoan ca PhiShip.
                if (order.DaThanhToan)
                {
                    soTienHoan = Math.Round(order.TongTien * tiLe)
                        + (traHetDon ? order.PhiShip : 0);
                    await _refund.GhiHoanTienAsync(order.Id, yeuCau.Id,
                        soTienHoan, order.PhuongThucTT);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return DuyetDoiTraResult.Ok(soTienHoan: soTienHoan);
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* da rollback */ }
            throw;
        }
    }

    private static bool LaLoiKhoaDb(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException se && se.SqliteErrorCode == 5) return true;
            if (e.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
