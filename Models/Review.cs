using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

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

    public DateTime NgayDanhGia { get; set; } = DateTime.UtcNow;
}
