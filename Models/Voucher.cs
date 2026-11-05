using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

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

    public bool ConHieuLuc(DateTime hienTai) =>
        DaDung < SoLuong && hienTai <= NgayHetHan;
}
