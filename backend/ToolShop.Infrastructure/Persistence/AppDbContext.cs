using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Domain.Entities.Customers;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
namespace ToolShop.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
       public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();

        public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
        public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasIndex(rt => rt.UserId);
        });

        builder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<Brand>(entity =>
        {
            entity.Property(b => b.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(b => b.Name).IsUnique();
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.SKU).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Barcode).HasMaxLength(50);

            entity.Property(p => p.PurchasePrice).HasPrecision(18, 2);
            entity.Property(p => p.SalePrice).HasPrecision(18, 2);

            entity.HasIndex(p => p.SKU).IsUnique();
            entity.HasIndex(p => p.Barcode).IsUnique();

            entity.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Brand)
                .WithMany()
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        builder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            entity.Property(c => c.NormalizedPhone).HasMaxLength(30).IsRequired();
            entity.Property(c => c.Address).HasMaxLength(300);
            entity.Property(c => c.Notes).HasMaxLength(1000);

            entity.HasIndex(c => c.NormalizedPhone).IsUnique();
            entity.HasIndex(c => c.Name);
        });
        
        builder.Entity<InventoryMovement>(entity =>
        {
            entity.Property(m => m.Reason).HasMaxLength(500).IsRequired();
            entity.Property(m => m.Notes).HasMaxLength(1000);
            entity.Property(m => m.ReferenceType).HasMaxLength(50);
            entity.Property(m => m.CreatedByUserId).HasMaxLength(450).IsRequired();

            entity.HasIndex(m => m.ProductId);
            entity.HasIndex(m => m.CreatedAt);
            entity.HasIndex(m => m.MovementType);

            entity.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

                builder.Entity<Supplier>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
            entity.Property(s => s.ContactPerson).HasMaxLength(150);
            entity.Property(s => s.Phone).HasMaxLength(30);
            entity.Property(s => s.Email).HasMaxLength(200);
            entity.Property(s => s.Address).HasMaxLength(300);
            entity.Property(s => s.Notes).HasMaxLength(1000);

            entity.HasIndex(s => s.Name);
        });

        builder.Entity<Purchase>(entity =>
        {
            entity.Property(p => p.PurchaseNumber).HasMaxLength(20).IsRequired();
            entity.Property(p => p.Notes).HasMaxLength(1000);
            entity.Property(p => p.Subtotal).HasPrecision(18, 2);
            entity.Property(p => p.DiscountAmount).HasPrecision(18, 2);
            entity.Property(p => p.TotalAmount).HasPrecision(18, 2);
            entity.Property(p => p.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(p => p.ConfirmedByUserId).HasMaxLength(450);

            entity.HasIndex(p => p.PurchaseNumber).IsUnique();
            entity.HasIndex(p => p.SupplierId);
            entity.HasIndex(p => p.PurchaseDate);
            entity.HasIndex(p => p.Status);

            entity.HasOne(p => p.Supplier)
                .WithMany()
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseItem>(entity =>
        {
            entity.Property(i => i.UnitCost).HasPrecision(18, 2);
            entity.Property(i => i.LineTotal).HasPrecision(18, 2);

            entity.HasIndex(i => i.PurchaseId);
            entity.HasIndex(i => i.ProductId);

            entity.HasOne(i => i.Purchase)
                .WithMany(p => p.Items)
                .HasForeignKey(i => i.PurchaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

                builder.Entity<Order>(entity =>
        {
            entity.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            entity.Property(o => o.Notes).HasMaxLength(1000);
            entity.Property(o => o.Subtotal).HasPrecision(18, 2);
            entity.Property(o => o.DiscountAmount).HasPrecision(18, 2);
            entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
            entity.Property(o => o.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(o => o.CompletedByUserId).HasMaxLength(450);

            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.HasIndex(o => o.CustomerId);
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.OrderDate);
            entity.HasIndex(o => o.CreatedByUserId);

            entity.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderItem>(entity =>
        {
            entity.Property(i => i.UnitPrice).HasPrecision(18, 2);
            entity.Property(i => i.DiscountAmount).HasPrecision(18, 2);
            entity.Property(i => i.LineTotal).HasPrecision(18, 2);

            entity.HasIndex(i => i.OrderId);
            entity.HasIndex(i => i.ProductId);

            entity.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Identity table-ҳо (AspNetUsers, AspNetRoles, ...) худкор
        // тавассути IdentityDbContext сохта мешаванд.
    }
}