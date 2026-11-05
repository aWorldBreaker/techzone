using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>
/// Bien the: mau sac / dung luong. GiaThem cong them vao GiaBan.
/// QUYET DINH Sprint 3 — Admin (audit P1-7): ProductVariant.TonKho CHI LA
/// THONG TIN HIEN THI, khong tham gia bat ky tinh toan ton kho nao.
/// Ly do chon (b) thay vi (a) dong bo: khong code nao doc/ghi TonKho bien
/// the (checkout/cart chi kiem tra va tru Product.TonKho, bien the chi anh
/// huong GiaThem); dong bo (a) doi hoi tru ton ca 2 cap trong cung
/// transaction + xu ly tranh chap bien the, phuc tap hon nhieu trong khi
/// do an khong ban theo bien the rieng (khach chon bien the chi de tinh
/// gia). Ton kho that duy nhat la Product.TonKho. Seed giu tong TonKho cac
/// bien the = TonKho SP de so lieu hien thi khong lech.
/// </summary>
public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(100)]
    public string TenBienThe { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,0)")]
    public decimal GiaThem { get; set; }

    public int TonKho { get; set; }
}
