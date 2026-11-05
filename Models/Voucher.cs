using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>
/// Voucher. Mo rong 2026-11-05 (nhom B): gioi han theo hang / danh muc /
/// hang thanh vien, hoac mien phi ship. Cac dieu kien null = khong gioi han.
/// </summary>
public class Voucher
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Ma { get; set; } = string.Empty;

    /// <summary>LoaiGiam: "Tien" (giam so tien co dinh) hoac "PhanTram".</summary>
    [Required, MaxLength(20)]
    public string LoaiGiam { get; set; } = "Tien";

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaTri { get; set; }

    /// <summary>Don toi thieu moi duoc ap dung. 0 = khong dieu kien.</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal DieuKien { get; set; }

    public DateTime NgayHetHan { get; set; }
    public int SoLuong { get; set; }
    public int DaDung { get; set; }

    /// <summary>Chi ap cho san pham cua hang nay. Null = moi hang.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>Chi ap cho san pham thuoc danh muc nay. Null = moi danh muc.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Hang thanh vien toi thieu: "Đồng" / "Bạc" / "Vàng". Null = moi hang.</summary>
    [MaxLength(20)]
    public string? MinTier { get; set; }

    /// <summary>Voucher mien phi van chuyen (khong giam tien hang).</summary>
    public bool FreeShip { get; set; }

    public bool ConHieuLuc(DateTime hienTai) =>
        DaDung < SoLuong && hienTai <= NgayHetHan;
}
