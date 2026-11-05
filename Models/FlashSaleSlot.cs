namespace TechZone.Models;

/// <summary>
/// Khung gio Flash Sale (nhom B): 3 khung/ngay 0h, 12h, 20h.
/// GioBatDau/GioKetThuc la gio trong ngay (0-24); khung chay khi gio hien
/// tai nam trong [GioBatDau, GioKetThuc).
/// </summary>
public class FlashSaleSlot
{
    public int Id { get; set; }
    public int GioBatDau { get; set; }
    public int GioKetThuc { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<FlashSaleSlotItem> Items { get; set; } = new List<FlashSaleSlotItem>();

    public string Ten => $"{GioBatDau}h – {GioKetThuc}h";

    public bool DangChay(int gioHienTai) =>
        IsActive && gioHienTai >= GioBatDau && gioHienTai < GioKetThuc;
}
