using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>
/// Ghi nhan hoan tien khi huy don / doi tra duoc duyet (Sprint 2 — Mang 4, B-9).
/// Bang "Refunds" la entity EF that (DbSet trong AppDbContext, migration
/// AddSprint2Rollback) — doc qua EF; ghi van dung INSERT ... SELECT ...
/// WHERE NOT EXISTS nguyen tu de giu idempotent du 2 request chay song song
/// (check-then-insert bang EF khong nguyen tu).
/// - CHI ghi hoan khi Order.DaThanhToan == true (tien da thu that).
///   Don COD chua thu huy thi khong co dong hoan nao — service goi
///   (OrderService / ReturnService) phai kiem tra DaThanhToan truoc.
/// - Idempotent: GhiHoanTienAsync chi chen khi chua co dong hoan cho cung
///   (OrderId, ReturnRequestId) — huy/duyet 2 lan khong tao 2 dong hoan.
/// </summary>
public class RefundService
{
    private readonly AppDbContext _db;

    public RefundService(AppDbContext db) => _db = db;

    /// <summary>
    /// Ghi 1 dong hoan tien "Đã hoàn". Tra true neu vua chen, false neu da co
    /// dong hoan cho cung (OrderId, ReturnRequestId) — idempotent.
    /// returnRequestId null = hoan do huy don; co gia tri = hoan do doi tra.
    /// </summary>
    public async Task<bool> GhiHoanTienAsync(int orderId, int? returnRequestId,
        decimal soTien, string phuongThucTt)
    {
        if (soTien <= 0) return false;
        // EF khong map duoc DBNull lam tham so (InvalidOperationException)
        // nen tach 2 nhanh: ReturnRequestId NULL (huy don) dung literal
        // NULL trong SQL; co gia tri (doi tra) dung tham so.
        int soDong;
        if (returnRequestId is null)
        {
            soDong = await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Refunds\" (\"OrderId\", \"ReturnRequestId\", \"SoTien\", \"PhuongThucTT\", \"TrangThai\", \"NgayTao\") "
                + "SELECT {0}, NULL, {1}, {2}, 'Đã hoàn', {3} "
                + "WHERE NOT EXISTS (SELECT 1 FROM \"Refunds\" "
                + "WHERE \"OrderId\" = {0} AND \"ReturnRequestId\" IS NULL)",
                orderId, soTien, phuongThucTt, DateTime.UtcNow);
        }
        else
        {
            soDong = await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Refunds\" (\"OrderId\", \"ReturnRequestId\", \"SoTien\", \"PhuongThucTT\", \"TrangThai\", \"NgayTao\") "
                + "SELECT {0}, {1}, {2}, {3}, 'Đã hoàn', {4} "
                + "WHERE NOT EXISTS (SELECT 1 FROM \"Refunds\" "
                + "WHERE \"OrderId\" = {0} AND \"ReturnRequestId\" = {1})",
                orderId, returnRequestId.Value, soTien, phuongThucTt, DateTime.UtcNow);
        }
        return soDong > 0;
    }

    /// <summary>Tat ca dong hoan cua 1 don (moi nhat truoc). Rong = chua hoan dong nao.</summary>
    public Task<List<RefundRecord>> LayTheoDonAsync(int orderId) =>
        _db.Refunds.Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

    public async Task<int> DemTheoDonAsync(int orderId) =>
        (await LayTheoDonAsync(orderId)).Count;

    public async Task<decimal> TongTienHoanAsync(int orderId) =>
        (await LayTheoDonAsync(orderId)).Sum(r => r.SoTien);
}
