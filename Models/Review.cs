using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

/// <summary>
/// Danh gia san pham. Mo rong 2026-11-05 (nhom A/D): kem anh + cho Admin
/// duyet truoc khi hien (IsApproved = false khi khach moi gui).
/// </summary>
public class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Range(1, 5)]
    public int Sao { get; set; }

    [MaxLength(1000)]
    public string? NoiDung { get; set; }

    /// <summary>Anh kem theo danh gia (upload jpg/png/webp, toi da 5MB).</summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Admin duyet moi hien tren trang chi tiet.</summary>
    public bool IsApproved { get; set; }

    public DateTime NgayDanhGia { get; set; } = DateTime.UtcNow;
}
