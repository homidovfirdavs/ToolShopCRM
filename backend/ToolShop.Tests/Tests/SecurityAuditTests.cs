using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>T2a: security-relevant audit events (failed login, lockout, expense and expense-category changes).</summary>
[Collection("db")]
public class SecurityAuditTests(ApiFactory factory) : TestBase(factory)
{
    private static string NewPassword() => "Aa1-" + Guid.NewGuid().ToString("N")[..12];
    private static string NewUsername() => "u_" + Guid.NewGuid().ToString("N")[..10];

    private async Task<(string Id, string Username, string Password)> CreateUserAsync()
    {
        var username = NewUsername();
        var password = NewPassword();
        var resp = await Admin.PostAsJsonAsync("/api/users", new
        {
            username,
            fullName = "Test " + username,
            email = username + "@test.local",
            password,
            role = "Cashier"
        });
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        return ((await resp.ReadJsonAsync()).Str("id"), username, password);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        Anonymous.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = username, password });

    private Task<List<ToolShop.Domain.Entities.AuditLog>> AuditAsync(string actionPrefix) =>
        Factory.WithDbAsync(db => db.AuditLogs.Where(a => a.Action.StartsWith(actionPrefix)).ToListAsync());

    [Fact]
    public async Task Wrong_password_is_audited_without_storing_the_password()
    {
        var (id, username, _) = await CreateUserAsync();
        var typed = "Wrong-" + Guid.NewGuid().ToString("N")[..10];

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, typed)).StatusCode);

        var logs = await AuditAsync("Auth.");
        var entry = Assert.Single(logs);
        Assert.Equal("Auth.LoginFailed", entry.Action);
        Assert.Equal(id, entry.UserId);
        Assert.DoesNotContain(typed, entry.Description ?? "");
    }

    [Fact]
    public async Task Unknown_user_is_audited_without_storing_the_typed_name()
    {
        var typed = "nobody_" + Guid.NewGuid().ToString("N")[..10];

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(typed, "irrelevant-Pass-1")).StatusCode);

        var entry = Assert.Single(await AuditAsync("Auth."));
        Assert.Equal("Auth.LoginFailed", entry.Action);
        Assert.Null(entry.UserId);
        Assert.DoesNotContain(typed, entry.Description ?? "");
    }

    [Fact]
    public async Task Deactivated_user_login_is_audited()
    {
        var (id, username, password) = await CreateUserAsync();
        await Admin.PostAsync($"/api/users/{id}/deactivate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, password)).StatusCode);

        var entry = Assert.Single(await AuditAsync("Auth."));
        Assert.Equal("Auth.LoginFailed", entry.Action);
        Assert.Equal(id, entry.UserId);
    }

    [Fact]
    public async Task Lockout_is_audited_once_and_later_attempts_are_logged_as_refused()
    {
        var (id, username, password) = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, "Bad-" + i)).StatusCode);

        var afterLock = await AuditAsync("Auth.");
        Assert.Equal(4, afterLock.Count(a => a.Action == "Auth.LoginFailed"));
        var locked = Assert.Single(afterLock, a => a.Action == "Auth.LockedOut");
        Assert.Equal(id, locked.UserId);

        // Even the correct password is refused while locked, and that is recorded as a failure, not as a new lockout.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, password)).StatusCode);

        var all = await AuditAsync("Auth.");
        Assert.Equal(5, all.Count(a => a.Action == "Auth.LoginFailed"));
        Assert.Single(all, a => a.Action == "Auth.LockedOut");
    }

    [Fact]
    public async Task Successful_login_still_writes_only_the_login_event()
    {
        var (_, username, password) = await CreateUserAsync();

        await (await LoginAsync(username, password)).ShouldBeAsync(HttpStatusCode.OK);

        var logs = await AuditAsync("Auth.");
        Assert.Empty(logs);
        Assert.Single(await AuditAsync("Login"));
    }

    [Fact]
    public async Task Expense_category_changes_are_audited()
    {
        var name = "Cat " + Guid.NewGuid().ToString("N")[..8];
        var create = await Admin.PostAsJsonAsync("/api/expense-categories", new { name, description = "d" });
        await create.ShouldBeAsync(HttpStatusCode.OK);
        var id = (await create.ReadJsonAsync()).Int("id");

        await (await Admin.PutAsJsonAsync($"/api/expense-categories/{id}", new { name = name + " 2", description = "d" }))
            .ShouldBeAsync(HttpStatusCode.OK);
        await (await Admin.DeleteAsync($"/api/expense-categories/{id}")).ShouldBeAsync(HttpStatusCode.NoContent);

        var logs = (await AuditAsync("ExpenseCategory.")).Where(a => a.EntityId == id.ToString()).ToList();
        var actions = logs.Select(l => l.Action).ToList();
        Assert.Contains("ExpenseCategory.Create", actions);
        Assert.Contains("ExpenseCategory.Update", actions);
        Assert.Contains("ExpenseCategory.Deactivate", actions);
        Assert.All(logs, l => Assert.Equal(Factory.AdminUserId, l.UserId));
    }

    [Fact]
    public async Task Expense_create_update_and_void_are_audited()
    {
        var cat = await Admin.PostAsJsonAsync("/api/expense-categories",
            new { name = "Cat " + Guid.NewGuid().ToString("N")[..8], description = "d" });
        await cat.ShouldBeAsync(HttpStatusCode.OK);
        var categoryId = (await cat.ReadJsonAsync()).Int("id");

        var create = await Admin.PostAsJsonAsync("/api/expenses", new
        {
            categoryId,
            amount = 50m,
            expenseDate = DateTime.UtcNow,
            description = "Audit test",
            referenceNumber = (string?)null,
            notes = (string?)null
        });
        await create.ShouldBeAsync(HttpStatusCode.OK);
        var id = (await create.ReadJsonAsync()).Int("id");

        await (await Admin.PutAsJsonAsync($"/api/expenses/{id}", new
        {
            categoryId,
            amount = 60m,
            expenseDate = DateTime.UtcNow,
            description = "Audit test 2",
            referenceNumber = (string?)null,
            notes = (string?)null
        })).ShouldBeAsync(HttpStatusCode.OK);

        await (await Admin.PostAsJsonAsync($"/api/expenses/{id}/void", new { reason = "test" }))
            .ShouldBeAsync(HttpStatusCode.OK);

        var actions = (await AuditAsync("Expense")).Where(a => a.EntityName == "Expense").Select(a => a.Action).ToList();
        Assert.Contains("ExpenseCreated", actions);
        Assert.Contains("ExpenseUpdated", actions);
        Assert.Contains("ExpenseVoided", actions);
    }
}
