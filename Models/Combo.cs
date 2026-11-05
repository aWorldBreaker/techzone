using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>
/// Combo mua kem thanh thuc the DB (Sprint 3 — B-7). Truoc day combo chi la
/// Dictionary hard-code trong ProductsController (AUDIT P2-12): khong co gia
/// combo, khong sua duoc tu Admin, gia hien thi = cong gia 2 mon.
///
/// QUYET DINH GIA (da chot): giam theo % tren tong gia cac mon trong combo
/// (gia thuc tra cua tung mon — da ap Flash Sale neu co — chu khong phai gia
/// niem yet). Khong chon "gia goi co dinh" vi gia goi phai che nho ra tung
/// dong OrderDetail moi khoa duoc VAT snapshot theo dong (Sprint 2 — B-10);
/// % giam thi chi can 1 so snapshot tren Order (GiamGiaCombo), giong cach
/// voucher/hang Vang dang lam. % ap cho moi bo day du (SoBo): khach mua 2 bo
/// thi giam 2 lan, mien la du hang + con suat.
/// </summary>
public class Combo
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Ten { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? MoTa { get; set; }

    /// <summary>% giam tren tong gia 1 bo day du (0 &lt; x &lt; 100). Vd 10 = giam 10%.</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal PhanTramGiam { get; set; }

    /// <summary>Thoi gian hieu luc (bao ca 2 dau, giong FlashSale).</summary>
    public DateTime BatDau { get; set; }

    public DateTime KetThuc { get; set; }

    /// <summary>Admin tat = combo ngung ap dung ngay, ke ca khi con trong han.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gioi han so bo ban ra (null = khong gioi han). DaBan dem so bo da ban.</summary>
    public int? GioiHanSL { get; set; }

    public int DaBan { get; set; }

    public ICollection<ComboItem> Items { get; set; } = new List<ComboItem>();

    /// <summary>Con ap dung tai thoi diem hien tai khong (ca han + bat/tat + suat).</summary>
    public bool ConHieuLuc(DateTime hienTai) =>
        IsActive && hienTai >= BatDau && hienTai <= KetThuc
        && (GioiHanSL is null || DaBan < GioiHanSL);
}

/// <summary>1 mon trong combo: SP + so luong can co trong 1 bo.</summary>
public class ComboItem
{
    public int Id { get; set; }

    public int ComboId { get; set; }
    public Combo Combo { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>So luong mon nay trong 1 bo (>= 1).</summary>
    public int SoLuong { get; set; } = 1;
}
