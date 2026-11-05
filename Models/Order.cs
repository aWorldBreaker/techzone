using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

public static class OrderStatus
{
    public const string ChoXacNhan = "Chờ xác nhận";
    public const string DangGiao = "Đang giao";
    public const string DaGiao = "Đã giao";
    public const string DaHuy = "Đã hủy";
}

public class Order
{
    public int Id { get; set; }

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal TongTien { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal PhiShip { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiamGia { get; set; }

    [Required, MaxLength(50)]
    public string TrangThai { get; set; } = OrderStatus.ChoXacNhan;

    [Required, MaxLength(500)]
    public string DiaChiGiao { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PhuongThucTT { get; set; } = "COD";

    public DateTime NgayDat { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string? MaVoucher { get; set; }

    public ICollection<OrderDetail> ChiTiet { get; set; } = new List<OrderDetail>();

    /// <summary>Tong khach tra = TongTien (da tru giam) + PhiShip.</summary>
    [NotMapped]
    public decimal ThanhToan => TongTien + PhiShip;
}
