using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Yeu cau doi tra (nhom C). TrangThai: "Chờ duyệt" / "Đã duyệt" /
/// "Từ chối" / "Hoàn thành". Admin duyet o ReturnsController (role Admin).
/// </summary>
public class ReturnRequest
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(1000)]
    public string LyDo { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AnhUrl { get; set; }

    [Required, MaxLength(20)]
    public string TrangThai { get; set; } = "Chờ duyệt";

    /// <summary>So luong tra lai trong 1 dong SP (Sprint 2 — Mang 4).
    /// 0 = toan bo so luong cua dong do (mac dinh hien tai); > 0 = tra 1 phan
    /// so luong trong dong (ReturnService chua dung truong nay de tinh
    /// hoan 1 phan so luong — PLANNED).</summary>
    public int SoLuong { get; set; }

    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
