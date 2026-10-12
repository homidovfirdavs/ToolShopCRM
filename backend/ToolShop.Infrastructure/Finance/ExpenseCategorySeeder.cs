using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Finance;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Finance;

public static class ExpenseCategorySeeder
{
    private static readonly string[] DefaultCategories =
    [
        "Rent", "Salary", "Utilities", "Internet", "Transport",
        "Delivery", "Fuel", "Marketing", "Office Supplies", "Repairs", "Other"
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        foreach (var name in DefaultCategories)
        {
            var exists = await db.ExpenseCategories.AnyAsync(c => c.Name == name);
            if (!exists)
            {
                db.ExpenseCategories.Add(new ExpenseCategory
                {
                    Name = name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
    }
}