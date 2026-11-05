using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

public class FlashSale
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaSale { get; set; }

    public DateTime BatDau { get; set; }
    public DateTime KetThuc { get; set; }
    public int GioiHanSL { get; set; }
    public int DaBan { get; set; }

    public bool DangChay(DateTime hienTai) =>
        hienTai >= BatDau && hienTai <= KetThuc && DaBan < GioiHanSL;
}
