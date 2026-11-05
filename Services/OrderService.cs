using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

public class DoiTrangThaiResult
{
    public bool ThanhCong { get; set; }
    public string? Loi { get; set; }

    /// <summary>True = don da o trang thai dich truoc do (huy 2 lan, doi trung trang thai).</summary>
    public bool DaOTrangThaiDich { get; set; }

    public static DoiTrangThaiResult Ok(bool daOTrangThaiDich = false) =>
        new() { ThanhCong = true, DaOTrangThaiDich = daOTrangThaiDich };
    public static DoiTrangThaiResult ThatBai(string loi) => new() { ThanhCong = false, Loi = loi };
}

/// <summary>
/// Doi trang thai don hang theo bang chuyen hop le (Sprint 1 — P0, AUDIT P1-5):
/// ChoXacNhan -&gt; DaXacNhan/DaHuy; DaXacNhan -&gt; DangDongGoi/DaHuy;
/// DangDongGoi -&gt; DangGiao/DaHuy; DangGiao -&gt; DaGiao; DaGiao/DaHuy: terminal.
/// - Huy don hoan ton kho (Sprint 1) + hoan 1 luot voucher (Sprint 2, worker A)
///   + hoan luot ban LuotBan/DaBan (Sprint 2, B-5); hoan tac diem
///   (thu Earn / tra Spend) la Mang 4.
/// - Huy idempotent: don da DaHuy thi tra thanh cong, khong hoan ton lan 2,
///   khong ghi them lich su.
/// - Chong race huy 2 lan / doi trang thai dong thoi: UPDATE trang thai co
///   dieu kien WHERE TrangThai = trang thai cu da doc; 0 dong = co ai do
///   doi truoc -> doc lai trang thai moi roi quyet dinh (da huy thi coi
///   nhu thanh cong idempotent, khac thi bao loi thu lai).
/// - Gap "database is locked" cua SQLite thi rollback va thu lai toi da 10 lan.
/// </summary>
public class OrderService
{
    private readonly AppDbContext _db;
    private readonly VoucherService _voucher;
    private readonly LoyaltyService _loyalty;
    private readonly RefundService _refund;

    public OrderService(AppDbContext db, VoucherService voucher,
        LoyaltyService loyalty, RefundService refund)
    {
        _db = db;
        _voucher = voucher;
        _loyalty = loyalty;
        _refund = refund;
    }

    /// <summary>Khach huy don cua minh (controller da kiem tra quyen so huu).</summary>
    public Task<DoiTrangThaiResult> HuyDonAsync(int orderId) =>
        DoiTrangThaiAsync(orderId, OrderStatus.DaHuy);

    public async Task<DoiTrangThaiResult> DoiTrangThaiAsync(int orderId, string trangThaiMoi)
    {
        if (!OrderStatus.LaTrangThaiHopLe(trangThaiMoi))
            return DoiTrangThaiResult.ThatBai("Trạng thái không hợp lệ.");

        for (var lan = 1; lan <= 10; lan++)
        {
            try
            {
                return await DoiTrangThaiMotLanAsync(orderId, trangThaiMoi);
            }
            catch (Exception ex) when (LaLoiKhoaDb(ex))
            {
                _db.ChangeTracker.Clear();
                if (lan == 10)
                    return DoiTrangThaiResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
                await Task.Delay(25 * lan);
            }
        }
        return DoiTrangThaiResult.ThatBai("Hệ thống đang bận, vui lòng thử lại.");
    }

    private async Task<DoiTrangThaiResult> DoiTrangThaiMotLanAsync(int orderId, string trangThaiMoi)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = await _db.Orders.AsNoTracking()
                .Include(o => o.ChiTiet)
                .FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null)
            {
                await tx.RollbackAsync();
                return DoiTrangThaiResult.ThatBai("Không tìm thấy đơn hàng.");
            }

            // Da o trang thai dich: idempotent, khong lam gi them.
            if (order.TrangThai == trangThaiMoi)
            {
                await tx.RollbackAsync();
                return DoiTrangThaiResult.Ok(daOTrangThaiDich: true);
            }

            if (!OrderStatus.ChuyenTrangThaiHopLeQ(order.TrangThai, trangThaiMoi))
            {
                await tx.RollbackAsync();
                return DoiTrangThaiResult.ThatBai(
                    $"Không thể chuyển đơn từ “{order.TrangThai}” sang “{trangThaiMoi}”.");
            }

            // UPDATE co dieu kien theo trang thai cu: neu 0 dong thi co
            // request khac doi truoc trong tich tac -> doc lai roi quyet dinh.
            var soDong = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Orders\" SET \"TrangThai\" = {0} WHERE \"Id\" = {1} AND \"TrangThai\" = {2}",
                trangThaiMoi, orderId, order.TrangThai);
            if (soDong == 0)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                var hienTai = await _db.Orders.AsNoTracking()
                    .Where(o => o.Id == orderId)
                    .Select(o => o.TrangThai)
                    .FirstOrDefaultAsync();
                if (hienTai == trangThaiMoi)
                    return DoiTrangThaiResult.Ok(daOTrangThaiDich: true);
                return DoiTrangThaiResult.ThatBai(
                    "Đơn hàng vừa được thay đổi trạng thái, vui lòng tải lại trang.");
            }

            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                TrangThai = trangThaiMoi,
                NgayCapNhat = DateTime.Now
            });

            // Huy don: hoan ton kho (chi 1 lan vi UPDATE co dieu kien o tren
            // dam bao chi 1 request thang) + hoan 1 luot voucher neu don
            // co dung voucher (Sprint 2 — worker A: HoanLuotAsync co guard
            // DaDung > 0, nhanh nay chi chay 1 lan cho moi don nen khong
            // hoan luot 2 lan) + hoan luot ban (Sprint 2 — B-5: LuotBan/DaBan
            // da ghi NGAY KHI TAO DON nen huy o bat ky trang thai nao truoc
            // giao cung phai hoan; dieu kien "LuotBan/DaBan >= soLuong" chi
            // de khong bao gio am neu du lieu cu lech; don tao truoc Sprint 2
            // khong co FlashSaleId thi khong hoan DaBan).
            // Sprint 2 — Mang 4 (B-9), hoan tac diem + hoan tien khi huy don:
            // thu hoi toan bo diem Earn cua don (dong "EarnHoan"), tra lai
            // toan bo diem Spend cua don (dong "SpendHoan") — ca 2 deu co
            // guard tong khong vuot so that + chi chay 1 lan nho UPDATE
            // trang thai co dieu kien o tren; hoan tien CHI khi tien da thu
            // that (DaThanhToan) — don COD chua thu huy thi khong co dong
            // hoan nao (khong tao giao dich hoan tien gia).
            if (trangThaiMoi == OrderStatus.DaHuy)
            {
                foreach (var detail in order.ChiTiet)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE \"Products\" SET \"TonKho\" = \"TonKho\" + {0}, "
                        + "\"LuotBan\" = \"LuotBan\" - {1} "
                        + "WHERE \"Id\" = {2} AND \"LuotBan\" >= {3}",
                        detail.SoLuong, detail.SoLuong, detail.ProductId, detail.SoLuong);
                    if (detail.FlashSaleId is not null)
                    {
                        await _db.Database.ExecuteSqlRawAsync(
                            "UPDATE \"FlashSales\" SET \"DaBan\" = \"DaBan\" - {0} "
                            + "WHERE \"Id\" = {1} AND \"DaBan\" >= {2}",
                            detail.SoLuong, detail.FlashSaleId.Value, detail.SoLuong);
                    }
                }
                // Sprint 3 — B-7: hoan suat combo da tru luc tao don (chi hoan
                // dung so bo snapshot tren don; dieu kien DaBan >= soBo de khong
                // bao gio am neu du lieu cu lech). Don khong dung combo thi
                // ComboId null / ComboSoBo 0 nen khong lam gi.
                if (order.ComboId.HasValue && order.ComboSoBo > 0)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE \"Combos\" SET \"DaBan\" = \"DaBan\" - {0} "
                        + "WHERE \"Id\" = {1} AND \"DaBan\" >= {2}",
                        order.ComboSoBo, order.ComboId.Value, order.ComboSoBo);
                }
                await _voucher.HoanLuotAsync(order.MaVoucher);
                if (order.UserId is not null)
                {
                    var earn = await _loyalty.EarnCuaDonAsync(orderId);
                    if (earn > 0)
                        await _loyalty.ThuHoiEarnAsync(order.UserId, orderId, earn);
                    var spend = await _loyalty.SpendCuaDonAsync(orderId);
                    if (spend > 0)
                        await _loyalty.HoanSpendAsync(order.UserId, orderId, spend);
                }
                if (order.DaThanhToan)
                    await _refund.GhiHoanTienAsync(orderId, null, order.ThanhToan, order.PhuongThucTT);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return DoiTrangThaiResult.Ok();
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
