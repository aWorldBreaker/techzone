using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

public class OrderDetail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    /// <summary>Don gia tai thoi diem dat hang (khoa gia, sau nay doi gia SP khong anh huong don cu).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal DonGia { get; set; }

    /// <summary>
    /// Snapshot Sprint 2 (B-5): gia goc 1 don vi tai thoi diem dat hang
    /// (GiaBan + GiaThem bien the, CHUA ap Flash Sale). Cung DonGia cho
    /// thay khach da duoc giam bao nhieu; doi gia SP sau nay khong anh
    /// huong don cu. CAN MIGRATION: cot moi OrderDetails.GiaGoc.
    /// </summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaGoc { get; set; }

    /// <summary>
    /// Snapshot Sprint 2 (B-5): ten khuyen mai da ap cho dong nay
    /// (vd "Flash Sale"), null = khong co khuyen mai. Hien tren don /
    /// hoa don tu snapshot nay, khong doc lai bang FlashSales.
    /// CAN MIGRATION: cot moi OrderDetails.TenKhuyenMai.
    /// </summary>
    [MaxLength(100)]
    public string? TenKhuyenMai { get; set; }

    /// <summary>
    /// Snapshot Sprint 2 (B-5): FlashSale da tru quota cho dong nay.
    /// Null = dong khong dung gia Flash Sale. OrderService dung field
    /// nay de hoan DaBan khi huy don (chi hoan dung dong da tru, 1 lan).
    /// CAN MIGRATION: cot moi OrderDetails.FlashSaleId (FK -> FlashSales).
    /// </summary>
    public int? FlashSaleId { get; set; }
    public FlashSale? FlashSale { get; set; }

    public int SoLuong { get; set; }

    [NotMapped]
    public decimal ThanhTien => DonGia * SoLuong;

    /// <summary>
    /// SNAPSHOT thue suat VAT luc dat hang (Sprint 2 — B-10, can migration).
    /// Vd 0.08m = 8%. 0 = don cu chua co snapshot — khi tinh lai thi suy ra
    /// tu danh muc SP + NgayDat qua VatService (xem VatService.TinhVatDon).
    /// Doi cau hinh thue sau nay khong lam doi so lieu don cu nho snapshot nay.
    /// </summary>
    [Column(TypeName = "decimal(5,4)")]
    public decimal ThueSuatVat { get; set; }

    /// <summary>
    /// SNAPSHOT tien VAT cua dong nay sau khi phan bo giam gia (Sprint 2 —
    /// B-10, can migration). Gia da gom VAT nen day la phan tach ra, khong
    /// phai cong them; tong TienVat cac dong = Order.TongVat.
    /// </summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal TienVat { get; set; }
}
