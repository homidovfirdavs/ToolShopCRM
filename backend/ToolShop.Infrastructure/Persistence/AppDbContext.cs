using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Domain.Entities.Customers;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Purchases;
using ToolShop.Domain.Entities.Sales;
using ToolShop.Domain.Entities.Idempotency;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Finance;
using ToolShop.Domain.Entities.Cash;
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
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderReturn> OrderReturns => Set<OrderReturn>();
    public DbSet<OrderReturnItem> OrderReturnItems => Set<OrderReturnItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashShiftPayment> CashShiftPayments => Set<CashShiftPayment>();
    public DbSet<CashShiftReturn> CashShiftReturns => Set<CashShiftReturn>();

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
            entity.Property(c => c.NameTj).HasMaxLength(150);
            entity.Property(c => c.NameRu).HasMaxLength(150);
            entity.Property(c => c.NameEn).HasMaxLength(150);
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
            entity.Property(i => i.CostAtSale).HasPrecision(18, 2);

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

        builder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.PaymentNumber).HasMaxLength(20).IsRequired();
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.ReferenceNumber).HasMaxLength(100);
            entity.Property(p => p.Notes).HasMaxLength(1000);
            entity.Property(p => p.ReceivedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(p => p.VoidedByUserId).HasMaxLength(450);
            entity.Property(p => p.VoidReason).HasMaxLength(500);

            entity.HasIndex(p => p.PaymentNumber).IsUnique();
            entity.HasIndex(p => p.OrderId);
            entity.HasIndex(p => p.CustomerId);
            entity.HasIndex(p => p.CreatedAt);
            entity.HasIndex(p => p.Status);

            entity.HasOne(p => p.Order)
                .WithMany()
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Customer)
                .WithMany()
                .HasForeignKey(p => p.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TASK 09 — Finance
        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(150).IsRequired();
            entity.Property(c => c.Description).HasMaxLength(500);

            entity.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<Expense>(entity =>
        {
            entity.Property(e => e.ExpenseNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ReferenceNumber).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.VoidedByUserId).HasMaxLength(450);
            entity.Property(e => e.VoidReason).HasMaxLength(500);

            entity.HasIndex(e => e.ExpenseNumber).IsUnique();
            entity.HasIndex(e => e.ExpenseDate);
            entity.HasIndex(e => e.ExpenseCategoryId);
            entity.HasIndex(e => e.IsVoided);

            entity.HasOne(e => e.ExpenseCategory)
                .WithMany()
                .HasForeignKey(e => e.ExpenseCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TASK C1 - returns ledger: immutable history of returns, accounted in the period of the return.
        // Everything is Restrict (history is never cascaded away); numbers come from return_number_seq.
        // Idempotency keys of money operations (payments now; sales and returns later). Written in the same transaction as
        // the operation. No foreign key on purpose: the table is generic (ResultEntityType + ResultEntityId).
        builder.Entity<IdempotencyRecord>(entity =>
        {
            entity.Property(r => r.Scope).HasMaxLength(50).IsRequired();
            entity.Property(r => r.UserId).HasMaxLength(450).IsRequired();
            entity.Property(r => r.Key).HasMaxLength(64).IsRequired();
            entity.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(r => r.ResultEntityType).HasMaxLength(50).IsRequired();

            entity.HasIndex(r => new { r.Scope, r.UserId, r.Key })
                .IsUnique()
                .HasDatabaseName("IX_IdempotencyRecords_Scope_UserId_Key");
            entity.HasIndex(r => r.ExpiresAt);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_IdempotencyRecords_RequestHash", "char_length(\"RequestHash\") = 64");
                t.HasCheckConstraint("CK_IdempotencyRecords_Key", "char_length(\"Key\") BETWEEN 16 AND 64");
            });
        });

        builder.HasSequence<long>("return_number_seq");

        builder.Entity<OrderReturn>(entity =>
        {
            entity.Property(r => r.ReturnNumber).HasMaxLength(20).IsRequired();
            entity.Property(r => r.Reason).HasMaxLength(500).IsRequired();
            entity.Property(r => r.ReturnedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(r => r.GoodsValue).HasPrecision(18, 2);
            entity.Property(r => r.CostReversed).HasPrecision(18, 2);
            entity.Property(r => r.WriteOffCost).HasPrecision(18, 2);
            entity.Property(r => r.RefundAmount).HasPrecision(18, 2);

            entity.HasIndex(r => r.ReturnNumber).IsUnique();
            entity.HasIndex(r => r.OrderId);
            entity.HasIndex(r => r.ReturnedAt);

            entity.HasOne(r => r.Order)
                .WithMany()
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_OrderReturns_GoodsValue", "\"GoodsValue\" >= 0");
                t.HasCheckConstraint("CK_OrderReturns_RefundAmount", "\"RefundAmount\" >= 0");
                t.HasCheckConstraint("CK_OrderReturns_RefundMethod", "\"RefundAmount\" = 0 OR \"RefundMethod\" IS NOT NULL");
            });
        });

        builder.Entity<OrderReturnItem>(entity =>
        {
            entity.Property(i => i.NetValue).HasPrecision(18, 2);
            entity.Property(i => i.CostAtSaleSnapshot).HasPrecision(18, 2);

            entity.HasIndex(i => i.OrderReturnId);
            entity.HasIndex(i => i.OrderItemId);
            entity.HasIndex(i => i.ProductId);

            entity.HasOne(i => i.OrderReturn)
                .WithMany(r => r.Items)
                .HasForeignKey(i => i.OrderReturnId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(i => i.OrderItem)
                .WithMany()
                .HasForeignKey(i => i.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_OrderReturnItems_Quantity", "\"Quantity\" > 0");
                t.HasCheckConstraint("CK_OrderReturnItems_Split", "\"RestockedQuantity\" >= 0 AND \"WrittenOffQuantity\" >= 0 AND \"RestockedQuantity\" + \"WrittenOffQuantity\" = \"Quantity\"");
                t.HasCheckConstraint("CK_OrderReturnItems_NetValue", "\"NetValue\" >= 0");
            });
        });

        // Cash shifts (Phase B). New tables only: Payments / Orders / Expenses are not changed.
        builder.HasSequence<long>("cash_shift_number_seq");

        builder.Entity<CashRegister>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(r => r.Name).IsUnique();
        });

        builder.Entity<CashShift>(entity =>
        {
            entity.Property(s => s.ShiftNumber).HasMaxLength(20).IsRequired();
            entity.Property(s => s.CashierUserId).HasMaxLength(450).IsRequired();
            entity.Property(s => s.OpenedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(s => s.ClosedByUserId).HasMaxLength(450);
            entity.Property(s => s.ReviewedByUserId).HasMaxLength(450);
            entity.Property(s => s.OpeningCash).HasPrecision(18, 2);
            entity.Property(s => s.ClosingCash).HasPrecision(18, 2);
            entity.Property(s => s.ExpectedCashAtClose).HasPrecision(18, 2);
            entity.Property(s => s.CashDifference).HasPrecision(18, 2);
            entity.Property(s => s.CloseNote).HasMaxLength(1000);
            entity.Property(s => s.ReviewNote).HasMaxLength(1000);

            entity.HasIndex(s => s.ShiftNumber).IsUnique();
            entity.HasIndex(s => s.CashierUserId);
            entity.HasIndex(s => s.OpenedAt);

            // The database itself refuses a second OPEN shift on the same drawer (Status 1 = Open).
            entity.HasIndex(s => s.CashRegisterId)
                .IsUnique()
                .HasFilter("\"Status\" = 1")
                .HasDatabaseName("UX_CashShifts_OneOpenPerRegister");

            entity.HasOne(s => s.CashRegister)
                .WithMany()
                .HasForeignKey(s => s.CashRegisterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CashShifts_OpeningCash", "\"OpeningCash\" >= 0");
                t.HasCheckConstraint("CK_CashShifts_Closed",
                    "\"Status\" = 1 OR (\"ClosedAt\" IS NOT NULL AND \"ClosingCash\" IS NOT NULL AND \"ExpectedCashAtClose\" IS NOT NULL AND \"CashDifference\" IS NOT NULL)");
            });
        });

        builder.Entity<CashMovement>(entity =>
        {
            entity.Property(m => m.Amount).HasPrecision(18, 2);
            entity.Property(m => m.Reason).HasMaxLength(500).IsRequired();
            entity.Property(m => m.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(m => m.VoidedByUserId).HasMaxLength(450);
            entity.Property(m => m.VoidReason).HasMaxLength(500);

            entity.HasIndex(m => m.CashShiftId);
            entity.HasIndex(m => m.ExpenseId);

            entity.HasOne(m => m.CashShift)
                .WithMany(s => s.Movements)
                .HasForeignKey(m => m.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.Expense)
                .WithMany()
                .HasForeignKey(m => m.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t => t.HasCheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0"));
        });

        builder.Entity<CashShiftPayment>(entity =>
        {
            entity.HasKey(l => l.PaymentId);
            entity.HasIndex(l => l.CashShiftId);

            entity.HasOne(l => l.Payment)
                .WithMany()
                .HasForeignKey(l => l.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.CashShift)
                .WithMany()
                .HasForeignKey(l => l.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CashShiftReturn>(entity =>
        {
            entity.HasKey(l => l.OrderReturnId);
            entity.HasIndex(l => l.CashShiftId);

            entity.HasOne(l => l.OrderReturn)
                .WithMany()
                .HasForeignKey(l => l.OrderReturnId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.CashShift)
                .WithMany()
                .HasForeignKey(l => l.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Identity table-ҳо (AspNetUsers, AspNetRoles, ...) худкор
        // тавассути IdentityDbContext сохта мешаванд.
    }
}