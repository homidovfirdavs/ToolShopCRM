using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ToolShop.Infrastructure.Sales;

namespace ToolShop.Infrastructure.Idempotency;

/// <summary>
/// Fingerprints of Complete / Return requests. The same Idempotency-Key with the same fingerprint is a retry (replay);
/// with a different fingerprint it is a client bug (409). Text is trimmed and null == "" so harmless differences do not
/// count; money uses a fixed "0.00" format. Line order does not matter.
/// </summary>
public static class OrderRequestHash
{
    public static string ForComplete(int orderId, InitialPaymentInput? payment)
    {
        var body = payment is null
            ? "none"
            : string.Join("|",
                payment.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                (int)payment.Method,
                (payment.ReferenceNumber ?? "").Trim(),
                (payment.Notes ?? "").Trim());

        return Sha256($"complete|{orderId}|{body}");
    }

    public static string ForReturn(int orderId, ReturnInput input)
    {
        var lines = input.Lines is { Count: > 0 }
            ? string.Join(";", input.Lines
                .OrderBy(l => l.OrderItemId)
                .Select(l => $"{l.OrderItemId}:{l.Quantity}:{l.DamagedQuantity}"))
            : "ALL";

        var method = input.RefundMethod is null ? "auto" : ((int)input.RefundMethod.Value).ToString(CultureInfo.InvariantCulture);
        var amount = input.RefundAmount is null ? "auto" : input.RefundAmount.Value.ToString("0.00", CultureInfo.InvariantCulture);

        return Sha256($"return|{orderId}|{(input.Reason ?? "").Trim()}|{lines}|{method}|{amount}");
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
