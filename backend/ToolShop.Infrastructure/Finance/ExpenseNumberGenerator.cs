using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Finance;

public static class ExpenseNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('expense_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"EXP-{result:D6}";
    }
}