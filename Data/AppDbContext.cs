using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TechZone.Models;

namespace TechZone.Data;

/// <summary>
/// DbContext: 14 bang theo SCOPE.md + bang ProductSpecs (bang 15).
/// ProductSpecs tach rieng vi thong so ky thuat khac nhau theo loai san pham
/// (xem Models/ProductSpecs.cs). Users dung Identity (AspNetUsers).
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSpecs> ProductSpecs => Set<ProductSpecs>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<FlashSale> FlashSales => Set<FlashSale>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ProductSpecs: khoa chinh dong thoi la khoa ngoai sang Product (1-1)
        builder.Entity<ProductSpecs>()
            .HasKey(s => s.ProductId);
        builder.Entity<ProductSpecs>()
            .HasOne(s => s.Product)
            .WithOne(p => p.Specs)
            .HasForeignKey<ProductSpecs>(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ma voucher duy nhat
        builder.Entity<Voucher>()
            .HasIndex(v => v.Ma)
            .IsUnique();

        // Slug danh muc duy nhat
        builder.Entity<Category>()
            .HasIndex(c => c.Slug)
            .IsUnique();

        // Gia tri mac dinh
        builder.Entity<Product>().Property(p => p.TonKho).HasDefaultValue(0);
        builder.Entity<Product>().Property(p => p.LuotBan).HasDefaultValue(0);
    }
}
