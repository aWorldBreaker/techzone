using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Lich su diem tich luy (nhom B). Loai: "Earn" (tich khi mua) hoac
/// "Spend" (doi diem giam gia). 1 diem = 1.000d khi doi; mua hang tich 1%
/// gia tri don: Diem = TongTien / 100.000, lam tron xuong.
/// </summary>
public class PointTransaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int Diem { get; set; }

    /// <summary>"Earn" hoac "Spend".</summary>
    [Required, MaxLength(10)]
    public string Loai { get; set; } = "Earn";

    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
