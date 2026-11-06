using Microsoft.EntityFrameworkCore;

namespace TechZone.Services;

/// <summary>
/// SEC-04 — Duong nang cap du lieu cho ma tra cuu cu (plaintext).
/// Vi SQLite khong co ham SHA-256 trong SQL nen migration
/// GuestTokenHardening KHONG the hash truc tiep gia tri dang luu bang SQL;
/// viec hash thuc hien o day, bang C#:
///   - Chay 1 lan sau khi migrate (vd lenh quan tri / script): hash toan bo
///     GuestToken plaintext 32 ky tu con lai thanh SHA-256 hex 64 ky tu.
///   - Token nao chua co han thi gan han 30 ngay ke tu luc chay (an han
///     nhu migration da gan cho token cu).
///   - An toan khi chay lai (idempotent): chi hash hang co do dai 32 —
///     hang da hash (64) duoc bo qua.
/// Ngoai ra moi lan verify thanh cong, OrderAccess.XacMinhToken cung tu
/// nang cap hang legacy do len hash (nang cap co hoi), nen ke ca khong chay
/// backfill thi token cu van dung duoc.
/// KHONG tu chay khi app khoi dong — chi chay theo lenh (tranh ghi DB that
/// khong co nguoi giam sat); xem README muc bao mat.
/// </summary>
public static class GuestTokenBackfill
{
    /// <summary>Hash hang loat token legacy; tra ve so don da nang cap.</summary>
    public static async Task<int> ChayAsync(Data.AppDbContext db, DateTime? now = null)
    {
        now ??= DateTime.Now;
        var legacy = await db.Orders
            .Where(o => o.GuestToken != null && o.GuestToken.Length == 32)
            .ToListAsync();
        foreach (var order in legacy)
        {
            order.GuestToken = OrderAccess.BamGuestToken(order.GuestToken!);
            order.GuestTokenHetHan ??= now.Value.AddDays(OrderAccess.HanTokenNgay);
        }
        await db.SaveChangesAsync();
        return legacy.Count;
    }
}
