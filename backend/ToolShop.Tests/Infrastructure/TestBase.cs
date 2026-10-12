using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Domain.Entities.Catalog;
using ToolShop.Domain.Entities.Customers;
using ToolShop.Domain.Entities.Inventory;
using ToolShop.Domain.Entities.Payments;
using ToolShop.Domain.Entities.Sales;

namespace ToolShop.Tests.Infrastructure;

/// <summary>Every test starts from an empty business database (users/roles are kept) and runs one at a time.</summary>
[Collection("db")]
public abstract class TestBase : IAsyncLifetime
{
    protected ApiFactory Factory { get; }
    protected HttpClient Admin { get; private set; } = null!;
    protected HttpClient Manager { get; private set; } = null!;
    protected HttpClient Cashier { get; private set; } = null!;
    protected HttpClient Warehouse { get; private set; } = null!;
    protected HttpClient Anonymous { get; private set; } = null!;

    private int _counter;

    protected TestBase(ApiFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        await Factory.ResetDataAsync();
        Admin = await Factory.ClientAsync("Admin");
        Manager = await Factory.ClientAsync("Manager");
        Cashier = await Factory.ClientAsync("Cashier");
        Warehouse = await Factory.ClientAsync("Warehouse");
        Anonymous = Factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------- direct DB seeding (arrange) ----------------

    private string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..10]}";

    protected async Task<int> SeedProductAsync(decimal salePrice = 100m, decimal purchasePrice = 60m, int stock = 0,
        bool active = true, int minimumStock = 0)
    {
        return await Factory.WithDbAsync(async db =>
        {
            var category = new Category { Name = Unique("cat") };
            var brand = new Brand { Name = Unique("brand") };
            db.Categories.Add(category);
            db.Brands.Add(brand);
            await db.SaveChangesAsync();

            var product = new Product
            {
                Name = Unique("Product"),
                SKU = Unique("SKU"),
                CategoryId = category.Id,
                BrandId = brand.Id,
                SalePrice = salePrice,
                PurchasePrice = purchasePrice,
                MinimumStockLevel = minimumStock,
                IsActive = active
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            if (stock > 0)
            {
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = product.Id,
                    MovementType = InventoryMovementType.StockIn,
                    Quantity = stock,
                    Reason = "test seed",
                    CreatedByUserId = Factory.AdminUserId
                });
                await db.SaveChangesAsync();
            }

            return product.Id;
        });
    }

    protected async Task<int> SeedCustomerAsync(bool active = true)
    {
        var n = Interlocked.Increment(ref _counter);
        return await Factory.WithDbAsync(async db =>
        {
            var phone = "9" + Random.Shared.Next(0, 100_000_000).ToString("D8") + n;
            var customer = new Customer
            {
                Name = Unique("Customer"),
                Phone = phone,
                NormalizedPhone = phone,
                IsActive = active
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            return customer.Id;
        });
    }

    /// <summary>
    /// A HISTORICAL completed walk-in order (no customer) written straight to the DB. The API refuses to create
    /// such an order unpaid (backend rule), so this models data that already existed before the rule.
    /// Returns (orderId, paymentId or 0 when nothing was paid).
    /// </summary>
    protected async Task<(int OrderId, int PaymentId)> SeedHistoricalWalkInOrderAsync(decimal total, decimal paid)
    {
        return await Factory.WithDbAsync(async db =>
        {
            var order = new Order
            {
                OrderNumber = Unique("HIST"),
                CustomerId = null,
                Status = OrderStatus.Completed,
                OrderDate = DateTime.UtcNow,
                Subtotal = total,
                TotalAmount = total,
                CreatedByUserId = Factory.AdminUserId,
                CompletedByUserId = Factory.AdminUserId,
                CompletedAt = DateTime.UtcNow
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            var paymentId = 0;
            if (paid > 0)
            {
                var payment = new Payment
                {
                    PaymentNumber = Unique("HPAY"),
                    OrderId = order.Id,
                    CustomerId = null,
                    Amount = paid,
                    Method = PaymentMethod.Cash,
                    Status = PaymentStatus.Completed,
                    ReceivedByUserId = Factory.AdminUserId
                };
                db.Payments.Add(payment);
                await db.SaveChangesAsync();
                paymentId = payment.Id;
            }

            return (order.Id, paymentId);
        });
    }

    // ---------------- API helpers (act) ----------------

    protected static object Item(int productId, int quantity, decimal discount = 0m) =>
        new { productId, quantity, discountAmount = discount };

    /// <summary>POST /api/orders (draft). Returns the order id and the server-calculated total.</summary>
    protected async Task<(int Id, decimal Total)> CreateOrderAsync(int? customerId, decimal orderDiscount = 0m,
        params object[] items)
    {
        var resp = await Admin.PostAsJsonAsync("/api/orders",
            new { customerId, discountAmount = orderDiscount, items });
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        var json = await resp.ReadJsonAsync();
        return (json.GetProperty("id").GetInt32(), json.GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>POST /api/orders/{id}/complete, optionally with an initial payment (Cash).</summary>
    protected Task<HttpResponseMessage> CompleteAsync(int orderId, decimal? initialPayment = null, HttpClient? client = null)
    {
        object? body = initialPayment is null
            ? new { }
            : new { initialPayment = new { amount = initialPayment.Value, method = "Cash" } };
        return (client ?? Admin).PostAsJsonAsync($"/api/orders/{orderId}/complete", body);
    }

    protected Task<HttpResponseMessage> PayAsync(int orderId, decimal amount, HttpClient? client = null) =>
        (client ?? Admin).PostAsJsonAsync("/api/payments", new { orderId, amount, method = "Cash" });

    /// <summary>Creates a customer order and completes it WITHOUT payment (credit sale). Returns (orderId, total).</summary>
    protected async Task<(int Id, decimal Total)> CreateCompletedCreditOrderAsync(int customerId, int productId, int quantity)
    {
        var (id, total) = await CreateOrderAsync(customerId, 0m, Item(productId, quantity));
        await (await CompleteAsync(id)).ShouldBeAsync(HttpStatusCode.OK);
        return (id, total);
    }

    protected async Task<int> StockAsync(int productId)
    {
        var resp = await Admin.GetAsync($"/api/inventory/stock/{productId}");
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        return (await resp.ReadJsonAsync()).GetProperty("currentStock").GetInt32();
    }

    protected Task<int> PaymentCountAsync() => Factory.WithDbAsync(db => db.Payments.CountAsync());
    protected Task<int> MovementCountAsync() => Factory.WithDbAsync(db => db.InventoryMovements.CountAsync());

    protected Task<decimal> PaidSumAsync(int orderId) => Factory.WithDbAsync(async db =>
        (await db.Payments.Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount)) ?? 0m);

    protected Task<OrderStatus> OrderStatusAsync(int orderId) => Factory.WithDbAsync(async db =>
        (await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId)).Status);
}

public static class HttpExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Asserts the status code and, on failure, includes the response body in the message.</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 600) body = body[..600];
        throw new Xunit.Sdk.XunitException(
            $"Expected HTTP {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString() ?? string.Empty;
    public static decimal Dec(this JsonElement e, string name) => e.GetProperty(name).GetDecimal();
    public static int Int(this JsonElement e, string name) => e.GetProperty(name).GetInt32();
}
