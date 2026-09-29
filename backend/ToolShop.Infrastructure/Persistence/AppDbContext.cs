using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Domain.Entities.Customers;
using ToolShop.Domain.Entities.Inventory;

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

        // Identity table-ҳо (AspNetUsers, AspNetRoles, ...) худкор
        // тавассути IdentityDbContext сохта мешаванд.
    }
}