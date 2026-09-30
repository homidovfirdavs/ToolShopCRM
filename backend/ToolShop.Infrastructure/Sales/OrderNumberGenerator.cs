using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Sales;

public static class OrderNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('order_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"ORD-{result:D6}";
    }
}