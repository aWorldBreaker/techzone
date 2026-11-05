using System.ComponentModel.DataAnnotations;

namespace TechZone.Models;

public class Brand
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Ten { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
