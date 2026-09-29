using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Purchases;

public static class PurchaseNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('purchase_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"PUR-{result:D6}";
    }
}