using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Tests.Infrastructure;

/// <summary>
/// Starts the real API in-process against a DEDICATED PostgreSQL test database.
/// Safety: the connection string comes from TOOLSHOP_TEST_CONNECTION and the database name MUST end with "_test",
/// so the real "toolshop_crm" database can never be wiped by these tests.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string[] Roles = ["Admin", "Manager", "Cashier", "Warehouse"];

    private readonly string _connectionString;
    private readonly string _password = "Tst-" + Guid.NewGuid().ToString("N") + "Aa1";
    private readonly Dictionary<string, string> _tokens = new();

    public string AdminUserId { get; private set; } = string.Empty;

    public ApiFactory()
    {
        _connectionString = Environment.GetEnvironmentVariable("TOOLSHOP_TEST_CONNECTION") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "TOOLSHOP_TEST_CONNECTION is not set. Point it at a dedicated test database whose name ends with '_test' " +
                "(see backend/scripts/Run-Tests.ps1).");
        }

        var dbName = new NpgsqlConnectionStringBuilder(_connectionString).Database ?? string.Empty;
        if (!dbName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run: database '{dbName}' does not end with '_test'. These tests DELETE all business data.");
        }

        // Environment variables win over appsettings and are read when the host is built.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("Jwt__Issuer", "toolshop-tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "toolshop-tests");
        Environment.SetEnvironmentVariable("Https__Redirect", "false");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitPerMinute", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitPerMinute", "1000000");
        // Cash shifts are exercised by the tests (the test database has the tables). RequireOpenShift stays off here; the
        // tests that need it switch it on for their own duration through CashShiftOptions.
        Environment.SetEnvironmentVariable("CashShifts__Enabled", "true");
        Environment.SetEnvironmentVariable("CashShifts__RequireOpenShift", "false");
        // Make sure the dev admin seeder is skipped; the tests create their own users.
        Environment.SetEnvironmentVariable("AdminUser__Username", "");
        Environment.SetEnvironmentVariable("AdminUser__Email", "");
        Environment.SetEnvironmentVariable("AdminUser__Password", "");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // T2d.4: MFA is on by default for Admin/Manager. The shared test server keeps it off (tests log in with a password
        // only); MfaTests start their own server with it on. An explicitly set empty value means "nobody".
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MfaRequiredRoles"] = "" }));
        // Test diagnostics: remember unhandled server errors so a 500 in a test can show its real cause.
        builder.ConfigureLogging(l => l.AddProvider(new ErrorCaptureProvider()));
    }

    public async Task InitializeAsync()
    {
        // Migrate BEFORE the host starts: Program.cs seeds roles on startup and needs the tables.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var scope = Services.CreateScope(); // builds the host (roles get seeded)
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in Roles)
        {
            var username = UserName(role);
            var user = await users.FindByNameAsync(username);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = username,
                    Email = username + "@test.local",
                    FullName = "Test " + role,
                    IsActive = true,
                    EmailConfirmed = true
                };
                var created = await users.CreateAsync(user, _password);
                if (!created.Succeeded)
                    throw new InvalidOperationException("Could not create test user: " +
                        string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            else
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var reset = await users.ResetPasswordAsync(user, token, _password);
                if (!reset.Succeeded)
                    throw new InvalidOperationException("Could not reset test user password.");
                user.IsActive = true;
                await users.UpdateAsync(user);
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);
            }

            if (!await users.IsInRoleAsync(user, role))
                await users.AddToRoleAsync(user, role);

            if (role == "Admin") AdminUserId = user.Id;
        }
    }

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    private static string UserName(string role) => "t_" + role.ToLowerInvariant();

    /// <summary>HTTP client authenticated as a test user with the given role (Admin, Manager, Cashier, Warehouse).</summary>
    public async Task<HttpClient> ClientAsync(string role)
    {
        var client = CreateClient();
        if (!_tokens.TryGetValue(role, out var token))
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login",
                new { usernameOrEmail = UserName(role), password = _password });
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            token = json.GetProperty("accessToken").GetString()!;
            _tokens[role] = token;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await work(db);
    }

    public Task WithDbAsync(Func<AppDbContext, Task> work) =>
        WithDbAsync<int>(async db => { await work(db); return 0; });

    /// <summary>Deletes ALL business data (orders, payments, stock, catalog, customers). Users and roles are kept.</summary>
    public Task ResetDataAsync() => WithDbAsync(async db =>
    {
        await db.IdempotencyRecords.ExecuteDeleteAsync(); // keys of money operations (no foreign keys)
        await db.CashShiftReturns.ExecuteDeleteAsync();   // cash-shift links reference payments / returns / shifts (Restrict)
        await db.CashShiftPayments.ExecuteDeleteAsync();
        await db.CashMovements.ExecuteDeleteAsync();
        await db.CashShifts.ExecuteDeleteAsync();         // the drawer itself (CashRegisters) is kept, like users and roles
        await db.OrderReturnItems.ExecuteDeleteAsync();   // ledger rows reference orders (Restrict) - delete first
        await db.OrderReturns.ExecuteDeleteAsync();
        await db.Payments.ExecuteDeleteAsync();
        await db.OrderItems.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.PurchaseItems.ExecuteDeleteAsync();
        await db.Purchases.ExecuteDeleteAsync();
        await db.InventoryMovements.ExecuteDeleteAsync();
        await db.Expenses.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Brands.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await db.Customers.ExecuteDeleteAsync();
        await db.Suppliers.ExecuteDeleteAsync();
        await db.AuditLogs.ExecuteDeleteAsync();

        // Users created by tests (everything except the four fixed t_* accounts) are removed too.
        var extraIds = db.Users.Where(u => !u.UserName!.StartsWith("t_")).Select(u => u.Id);
        await db.RefreshTokens.Where(r => extraIds.Contains(r.UserId)).ExecuteDeleteAsync();
        await db.UserRoles.Where(r => extraIds.Contains(r.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(u => !u.UserName!.StartsWith("t_")).ExecuteDeleteAsync();
    });
}

[CollectionDefinition("db")]
public sealed class DbCollection : ICollectionFixture<ApiFactory>
{
}


/// <summary>Collects Error-level log entries (with exception text) produced while the tests run.</summary>
public sealed class ErrorCaptureProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Errors = new();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Capture(categoryName);
    public void Dispose() { }

    public static string Dump()
    {
        // EF logs every failed command at Error level even when the service then retries successfully, so only the
        // entries written by the exception-handler middleware describe the request that really ended in a 500.
        var real = Errors.Where(e => e.Contains("ExceptionHandler") || e.Contains("Diagnostics")).TakeLast(2).ToList();
        var text = real.Count > 0
            ? "UNHANDLED (caused the 5xx):\n" + string.Join("\n---\n", real)
            : "No unhandled-exception entry found. Last errors:\n" + string.Join("\n---\n", Errors.TakeLast(2));
        return text.Length > 3500 ? text[..3500] : text;
    }

    private sealed class Capture(string category) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Error;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Errors.Enqueue($"[{category}] {formatter(state, exception)} {exception}");
        }
    }
}
