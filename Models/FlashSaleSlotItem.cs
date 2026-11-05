using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>San pham trong 1 khung Flash Sale (nhom B).</summary>
public class FlashSaleSlotItem
{
    public int Id { get; set; }
    public int SlotId { get; set; }
    public FlashSaleSlot Slot { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaSale { get; set; }
}
