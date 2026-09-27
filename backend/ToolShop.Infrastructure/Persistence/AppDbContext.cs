using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities;

namespace ToolShop.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    builder.Entity<RefreshToken>(entity =>
    {
        entity.HasIndex(rt => rt.TokenHash).IsUnique();
        entity.HasIndex(rt => rt.UserId);
    });

    // Identity table-ҳо (AspNetUsers, AspNetRoles, ...) худкор
    // тавассути IdentityDbContext сохта мешаванд.

    // DbSet-ҳои entity-ҳои бизнес (Product, Customer, Order, ...)
    // дар Task-ҳои баъдӣ дар ин ҷо илова мешаванд.
    
}
}