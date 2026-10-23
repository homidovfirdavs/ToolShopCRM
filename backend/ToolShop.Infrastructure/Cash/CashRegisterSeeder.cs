using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Cash;

/// <summary>Creates the single default drawer when none exists. Runs only when CashShifts:Enabled is true.</summary>
public static class CashRegisterSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.CashRegisters.AnyAsync()) return;

        db.CashRegisters.Add(new CashRegister { Name = "Main", IsActive = true });
        await db.SaveChangesAsync();
    }
}
