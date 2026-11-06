namespace TechZone.Models;

/// <summary>
/// SEC-04: form nhap ma tra cuu (POST) cho don khach vang lai — dung chung
/// cho Orders.Details va Orders.HoaDon. HanhDong = ten action POST se nhan
/// form ("Details" hoac "HoaDon").
/// </summary>
public class NhapMaTraCuuViewModel
{
    public int OrderId { get; set; }
    public string HanhDong { get; set; } = "Details";
}
