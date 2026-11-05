using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Services;

/// <summary>Thong bao in-app cho khach (nhom B). Chuong o header dem so DaDoc = false.</summary>
public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    public async Task<int> DemChuaDocAsync(string userId) =>
        await _db.Notifications.CountAsync(n => n.UserId == userId && !n.DaDoc);

    /// <summary>Danh dau tat ca thong bao chua doc cua user thanh da doc.</summary>
    public async Task DanhDauDaDocAsync(string userId)
    {
        var chuaDoc = await _db.Notifications
            .Where(n => n.UserId == userId && !n.DaDoc)
            .ToListAsync();
        foreach (var n in chuaDoc)
            n.DaDoc = true;
        await _db.SaveChangesAsync();
    }

    public async Task TaoThongBaoAsync(string userId, string tieuDe, string? noiDung)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            TieuDe = tieuDe,
            NoiDung = noiDung,
            DaDoc = false,
            NgayTao = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>Danh sach thong bao moi nhat truoc, mac dinh 20 cai.</summary>
    public async Task<List<Notification>> DanhSachAsync(string userId, int take = 20) =>
        await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.NgayTao)
            .Take(take)
            .ToListAsync();
}
