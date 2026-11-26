namespace TechZone.Models;

public sealed record ProductNeed(string Key, string Title, string Departments, string[] CategorySlugs)
{
    public static IReadOnlyList<ProductNeed> All { get; } =
    [
        new("lam-viec", "Làm việc & học tập", "Laptop, tablet", ["laptop", "tablet"]),
        new("chup-anh", "Chụp ảnh & quay video", "Điện thoại", ["dien-thoai"]),
        new("nghe-nhac", "Nghe nhạc & di chuyển", "Tai nghe", ["tai-nghe"]),
    ];

    public static ProductNeed? Find(string? key) => All.FirstOrDefault(need => need.Key == key);

    public string[] SpecsFor(Product product)
    {
        if (product.Specs is not { } specs) return [];
        string?[] fields = Key switch
        {
            "lam-viec" => [specs.Chip, specs.Ram, specs.Rom],
            "chup-anh" => [specs.Camera, specs.ManHinh, specs.Pin],
            "nghe-nhac" => [specs.ThongSoKhac?.Split(',')[0].Trim(), specs.Pin],
            _ => [],
        };
        return fields.Where(field => !string.IsNullOrWhiteSpace(field)).Select(field => field!).ToArray();
    }
}
