using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Lich su trang thai don hang (nhom C) — ve timeline theo doi don:
/// Da dat -&gt; Da xac nhan -&gt; Dang dong goi -&gt; Dang giao -&gt; Da giao.
/// </summary>
public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Required, MaxLength(50)]
    public string TrangThai { get; set; } = string.Empty;

    public DateTime NgayCapNhat { get; set; } = DateTime.UtcNow;
}
