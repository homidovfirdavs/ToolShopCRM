using Microsoft.EntityFrameworkCore;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Payments;

public static class PaymentNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db)
    {
        var result = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('payment_number_seq') AS \"Value\"")
            .SingleAsync();

        return $"PAY-{result:D6}";
    }
}