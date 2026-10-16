using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ToolShop.Domain.Entities.Payments;

namespace ToolShop.Infrastructure.Payments;

/// <summary>
/// Fingerprint of a "create payment" request. Two requests with the same key must have the same fingerprint,
/// otherwise the key is being reused for a different payment. null and "" are the same; surrounding spaces and the
/// amount's trailing zeros do not matter.
/// </summary>
public static class PaymentRequestHash
{
    public static string Compute(int orderId, decimal amount, PaymentMethod method, string? referenceNumber, string? notes)
    {
        var canonical = string.Join('|',
            orderId.ToString(CultureInfo.InvariantCulture),
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            ((int)method).ToString(CultureInfo.InvariantCulture),
            (referenceNumber ?? string.Empty).Trim(),
            (notes ?? string.Empty).Trim());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
