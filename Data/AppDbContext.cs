using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TechZone.Models;

namespace TechZone.Data;

/// <summary>
/// DbContext: 27 DbSet + AspNetUsers qua Identity = 28 thực thể;
/// 34 bảng vật lý với 7 bảng Identity (xem AUDIT-PHASE0 P1-12).
/// ProductSpecs tách riêng vì thông số kỹ thuật khác nhau theo loại sản phẩm
/// (xem Models/ProductSpecs.cs). Users dùng Identity (AspNetUsers).
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

    // --- Mo rong 2026-11-05 ---
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<FlashSaleSlot> FlashSaleSlots => Set<FlashSaleSlot>();
    public DbSet<FlashSaleSlotItem> FlashSaleSlotItems => Set<FlashSaleSlotItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<WarrantyClaim> WarrantyClaims => Set<WarrantyClaim>();
    public DbSet<StockIn> StockIns => Set<StockIn>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefundRecord> Refunds => Set<RefundRecord>();

    // --- Sprint 3 (B-7): Combo mua kem thanh thuc the DB ---
    public DbSet<Combo> Combos => Set<Combo>();
    public DbSet<ComboItem> ComboItems => Set<ComboItem>();

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

        // Wishlist: 1 user + 1 SP chi 1 dong
        builder.Entity<Wishlist>()
            .HasIndex(w => new { w.UserId, w.ProductId })
            .IsUnique();

        // Order.IdempotencyKey: chong dat don trung (Sprint 1). SQLite cho phep
        // nhieu NULL trong unique index nen don cu/seed (NULL) khong vi pham.
        builder.Entity<Order>()
            .HasIndex(o => o.IdempotencyKey)
            .IsUnique();

        // OrderDetail.FlashSaleId: xoa dot FlashSale thi giu lai don cu
        // (chi set null), khong xoa lich su don hang theo.
        builder.Entity<OrderDetail>()
            .HasOne(d => d.FlashSale)
            .WithMany()
            .HasForeignKey(d => d.FlashSaleId)
            .OnDelete(DeleteBehavior.SetNull);

        // RefundRecord: bang hoan tien (Sprint 2 — Mang 4, B-9). Chi luu Id
        // (OrderId/ReturnRequestId), khong dat FK constraint de giu lich su
        // hoan du don/yeu cau co bi xoa sau nay.
        builder.Entity<RefundRecord>()
            .HasIndex(r => r.OrderId)
            .HasDatabaseName("IX_Refunds_OrderId");

        // Combo (Sprint 3 — B-7): 1 combo + 1 SP chi 1 dong; xoa combo thi
        // xoa cac mon theo (Cascade), xoa SP trong combo thi chan (Restrict)
        // de khong vo tinh pha combo dang ban; xoa combo thi don cu van giu
        // snapshot GiamGiaCombo/ComboSoBo (SetNull).
        builder.Entity<ComboItem>()
            .HasIndex(i => new { i.ComboId, i.ProductId })
            .IsUnique();
        builder.Entity<ComboItem>()
            .HasOne(i => i.Combo)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.ComboId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ComboItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Order>()
            .HasOne(o => o.Combo)
            .WithMany()
            .HasForeignKey(o => o.ComboId)
            .OnDelete(DeleteBehavior.SetNull);

        // Gia tri mac dinh
        builder.Entity<Product>().Property(p => p.TonKho).HasDefaultValue(0);
        builder.Entity<Product>().Property(p => p.LuotBan).HasDefaultValue(0);
    }
}
