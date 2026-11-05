using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

public static class OrderStatus
{
    public const string ChoXacNhan = "Chờ xác nhận";
    public const string DaXacNhan = "Đã xác nhận";
    public const string DangDongGoi = "Đang đóng gói";
    public const string DangGiao = "Đang giao";
    public const string DaGiao = "Đã giao";
    public const string DaHuy = "Đã hủy";

    /// <summary>Thu tu timeline theo doi don (nhom C).</summary>
    public static readonly string[] Timeline =
    {
        ChoXacNhan, DaXacNhan, DangDongGoi, DangGiao, DaGiao
    };

    /// <summary>
    /// Bang chuyen trang thai hop le (Sprint 1 — P0). Chi cac chuyen trong bang
    /// moi duoc phep; DaGiao va DaHuy la trang thai cuoi (terminal).
    /// Cam tuyet doi: DaHuy -&gt; bat ky trang thai nao (hoi sinh don da huy),
    /// DaGiao -&gt; DaHuy (hang da giao roi khong duoc hoan ton kho).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ChuyenTrangThaiHopLe =
        new Dictionary<string, string[]>
        {
            [ChoXacNhan] = new[] { DaXacNhan, DaHuy },
            [DaXacNhan] = new[] { DangDongGoi, DaHuy },
            [DangDongGoi] = new[] { DangGiao, DaHuy },
            [DangGiao] = new[] { DaGiao },
            [DaGiao] = Array.Empty<string>(),
            [DaHuy] = Array.Empty<string>(),
        };

    /// <summary>Chuyen trang thai co hop le khong (ke ca giu nguyen trang thai).</summary>
    public static bool ChuyenTrangThaiHopLeQ(string tu, string den) =>
        tu == den || (ChuyenTrangThaiHopLe.TryGetValue(tu, out var dich) && dich.Contains(den));

    /// <summary>Don o trang thai nay co duoc huy khong.</summary>
    public static bool CoTheHuy(string trangThai) =>
        trangThai is ChoXacNhan or DaXacNhan or DangDongGoi;

    public static bool LaTrangThaiHopLe(string trangThai) =>
        ChuyenTrangThaiHopLe.ContainsKey(trangThai);
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

    /// <summary>
    /// Phuong thuc thanh toan (gia lap): COD / ChuyenKhoan / MoMo / ZaloPay / VNPay.
    /// </summary>
    [Required, MaxLength(100)]
    public string PhuongThucTT { get; set; } = "COD";

    public DateTime NgayDat { get; set; } = DateTime.UtcNow;

    /// <summary>Chống đặt đơn trùng (Sprint 1 — P0): mỗi lần checkout sinh 1 key duy nhất; NULL = đơn cũ/seed, không ràng buộc.</summary>
    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }

    [MaxLength(20)]
    public string? MaVoucher { get; set; }

    public ICollection<OrderDetail> ChiTiet { get; set; } = new List<OrderDetail>();

    /// <summary>Tong khach tra = TongTien (da tru giam) + PhiShip.</summary>
    [NotMapped]
    public decimal ThanhToan => TongTien + PhiShip;

    /// <summary>
    /// SNAPSHOT tong tien VAT cua don (chi tien hang, khong tinh phi ship),
    /// = tong OrderDetail.TienVat (Sprint 2 — B-10, can migration).
    /// Gia da gom VAT nen TongVat la phan tach ra khoi TongTien, khong cong them.
    /// </summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal TongVat { get; set; }

    /// <summary>Tong tien hang truoc moi khoan giam (tinh lai tu chi tiet).</summary>
    [NotMapped]
    public decimal TamTinhGoc => ChiTiet.Sum(d => d.ThanhTien);

    /// <summary>Tong moi khoan giam: voucher + doi diem + hang Vang + combo.</summary>
    [NotMapped]
    public decimal TongGiam => GiamGia + GiamGiaDiem + GiamGiaHang + GiamGiaCombo;

    /// <summary>
    /// Combo da ap cho don nay (Sprint 3 — B-7). Null = don khong dung combo.
    /// FK SetNull: xoa combo thi don cu van giu GiamGiaCombo/ComboSoBo da snapshot.
    /// </summary>
    public int? ComboId { get; set; }
    public Combo? Combo { get; set; }

    /// <summary>So tien giam do combo (snapshot luc dat, % tinh lai tu DB).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal GiamGiaCombo { get; set; }

    /// <summary>
    /// So bo combo da ban trong don nay (snapshot). OrderService dung de hoan
    /// Combo.DaBan khi huy don — giong cach OrderDetail.FlashSaleId hoan DaBan
    /// Flash Sale (Sprint 2 — B-5).
    /// </summary>
    public int ComboSoBo { get; set; }

    // --- Mo rong 2026-11-05 (nhom A/B/C) ---

    /// <summary>Tra gop 0% gia lap: so thang (6 hoac 12). Null = tra 1 lan.</summary>
    public int? InstallmentMonths { get; set; }

    /// <summary>So diem tich luy da dung de giam gia don nay (nhom B).</summary>
    public int DiemDaDung { get; set; }

    /// <summary>So tien giam do doi diem (DiemDaDung x 1.000d).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal GiamGiaDiem { get; set; }

    /// <summary>So tien giam them cho hang Vang (2% tam tinh, nhom B).</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal GiamGiaHang { get; set; }

    /// <summary>Vi MoMo/ZaloPay/VNPay: khach bam "Toi da thanh toan" thi true (gia lap).</summary>
    public bool DaThanhToan { get; set; }

    /// <summary>Thong tin xuat hoa don VAT (nhom C). Null = khong xuat.</summary>
    [MaxLength(200)]
    public string? VatTenCongTy { get; set; }

    [MaxLength(20)]
    public string? VatMaSoThue { get; set; }

    [MaxLength(500)]
    public string? VatDiaChiCongTy { get; set; }

    /// <summary>So tien gop moi thang (chi khi InstallmentMonths co gia tri).</summary>
    [NotMapped]
    public decimal TienGopMoiThang =>
        InstallmentMonths.HasValue && InstallmentMonths.Value > 0
            ? Math.Round(ThanhToan / InstallmentMonths.Value)
            : 0;

    public ICollection<OrderStatusHistory> LichSuTrangThai { get; set; } = new List<OrderStatusHistory>();
}
