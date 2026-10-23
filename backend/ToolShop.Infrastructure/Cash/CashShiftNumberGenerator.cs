using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Cash;

public static class CashShiftNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('cash_shift_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"SHF-{result:D6}";
    }
}
