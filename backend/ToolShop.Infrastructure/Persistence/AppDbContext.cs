using Microsoft.EntityFrameworkCore;

namespace ToolShop.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // DbSet-ҳои entity-ҳо (Product, Customer, Order, ...) дар Task-ҳои
    // баъдӣ дар ин ҷо илова мешаванд.
}