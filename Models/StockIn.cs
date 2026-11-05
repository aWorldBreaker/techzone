namespace TechZone.Models;

/// <summary>
/// Lich su nhap kho (nhom D): nhap them so luong thi tang Product.TonKho
/// va ghi 1 dong nay, khong sua tay TonKho.
/// </summary>
public class StockIn
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int SoLuong { get; set; }
    public string? GhiChu { get; set; }
    public DateTime NgayNhap { get; set; } = DateTime.UtcNow;
}
