using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>Admin-only user management: create, change role, deactivate/activate, reset password.</summary>
[Collection("db")]
public class UserManagementTests(ApiFactory factory) : TestBase(factory)
{
    private static string NewPassword() => "Aa1-" + Guid.NewGuid().ToString("N")[..12];
    private static string NewUsername() => "u_" + Guid.NewGuid().ToString("N")[..10];

    private async Task<(string Id, string Username, string Password)> CreateUserAsync(string role = "Cashier")
    {
        var username = NewUsername();
        var password = NewPassword();
        var resp = await Admin.PostAsJsonAsync("/api/users", new
        {
            username,
            fullName = "Test " + username,
            email = username + "@test.local",
            password,
            role
        });
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        var json = await resp.ReadJsonAsync();
        return (json.Str("id"), username, password);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        Anonymous.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = username, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    // ---------- access control ----------

    [Fact]
    public async Task Only_admin_can_use_the_users_api()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/users")).StatusCode);
        foreach (var client in new[] { Manager, Cashier, Warehouse })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/users", new { })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync("/api/users")).StatusCode);
    }

    // ---------- create ----------

    [Fact]
    public async Task Created_user_can_log_in_with_the_chosen_role()
    {
        var (_, username, password) = await CreateUserAsync("Warehouse");

        var login = await LoginAsync(username, password);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        var roles = (await login.ReadJsonAsync()).GetProperty("user").GetProperty("roles");
        Assert.Equal(1, roles.GetArrayLength());
        Assert.Equal("Warehouse", roles[0].GetString());
    }

    [Fact]
    public async Task Create_rejects_weak_password_duplicate_username_and_unknown_role()
    {
        var (_, username, _) = await CreateUserAsync();

        var weak = await Admin.PostAsJsonAsync("/api/users", new
        { username = NewUsername(), fullName = "X", email = NewUsername() + "@t.local", password = "short", role = "Cashier" });
        await weak.ShouldBeAsync(HttpStatusCode.BadRequest);

        var dup = await Admin.PostAsJsonAsync("/api/users", new
        { username, fullName = "X", email = NewUsername() + "@t.local", password = NewPassword(), role = "Cashier" });
        await dup.ShouldBeAsync(HttpStatusCode.Conflict);

        var badRole = await Admin.PostAsJsonAsync("/api/users", new
        { username = NewUsername(), fullName = "X", email = NewUsername() + "@t.local", password = NewPassword(), role = "SuperUser" });
        await badRole.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    // ---------- deactivate / activate ----------

    [Fact]
    public async Task Deactivated_user_loses_access_immediately_and_can_be_reactivated()
    {
        var (id, username, password) = await CreateUserAsync();

        var login = await LoginAsync(username, password);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        var body = await login.ReadJsonAsync();
        var access = body.Str("accessToken");
        var refresh = body.Str("refreshToken");

        var userClient = Factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/auth/me")).StatusCode);

        await (await Admin.PostAsync($"/api/users/{id}/deactivate", null)).ShouldBeAsync(HttpStatusCode.OK);

        // the existing access token is rejected right away; login and refresh fail too
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(refresh)).StatusCode);

        // the user row is kept (deactivate, never delete)
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Users.CountAsync(u => u.Id == id)));

        await (await Admin.PostAsync($"/api/users/{id}/activate", null)).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(username, password)).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_or_change_the_role_of_themselves()
    {
        var me = await (await Admin.GetAsync("/api/auth/me")).ReadJsonAsync();
        var myId = me.Str("id");

        await (await Admin.PostAsync($"/api/users/{myId}/deactivate", null)).ShouldBeAsync(HttpStatusCode.Conflict);

        var role = await Admin.PutAsJsonAsync($"/api/users/{myId}", new
        { fullName = "Me", email = "t_admin@test.local", role = "Cashier" });
        await role.ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unknown_user_returns_404()
    {
        await (await Admin.PostAsync("/api/users/does-not-exist/deactivate", null)).ShouldBeAsync(HttpStatusCode.NotFound);
        await (await Admin.PostAsJsonAsync("/api/users/does-not-exist/reset-password", new { newPassword = NewPassword() }))
            .ShouldBeAsync(HttpStatusCode.NotFound);
    }

    // ---------- role change ----------

    [Fact]
    public async Task Role_change_applies_after_a_new_login_and_ends_old_sessions()
    {
        var (id, username, password) = await CreateUserAsync("Cashier");
        var first = await (await LoginAsync(username, password)).ReadJsonAsync();
        var oldRefresh = first.Str("refreshToken");

        var put = await Admin.PutAsJsonAsync($"/api/users/{id}", new
        { fullName = "Promoted", email = username + "@test.local", role = "Manager" });
        await put.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal("Manager", (await put.ReadJsonAsync()).Str("role"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(oldRefresh)).StatusCode);

        var again = await (await LoginAsync(username, password)).ReadJsonAsync();
        Assert.Equal("Manager", again.GetProperty("user").GetProperty("roles")[0].GetString());
    }

    // ---------- reset password ----------

    [Fact]
    public async Task Reset_password_changes_the_password_and_revokes_sessions()
    {
        var (id, username, oldPassword) = await CreateUserAsync();
        var refresh = (await (await LoginAsync(username, oldPassword)).ReadJsonAsync()).Str("refreshToken");

        var weak = await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "abc" });
        await weak.ShouldBeAsync(HttpStatusCode.BadRequest);

        var newPassword = NewPassword();
        await (await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword }))
            .ShouldBeAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, oldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(username, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(refresh)).StatusCode);
    }

    [Fact]
    public async Task Reset_password_unlocks_a_locked_out_account()
    {
        var (id, username, password) = await CreateUserAsync();
        for (var i = 0; i < 5; i++)
            await LoginAsync(username, "wrong-" + i);
        // locked: even the correct password is refused
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(username, password)).StatusCode);

        var fresh = NewPassword();
        await (await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = fresh }))
            .ShouldBeAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(username, fresh)).StatusCode);
    }

    // ---------- audit ----------

    [Fact]
    public async Task Actions_are_audited_without_leaking_passwords()
    {
        var (id, _, password) = await CreateUserAsync();
        var fresh = NewPassword();
        await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = fresh });
        await Admin.PostAsync($"/api/users/{id}/deactivate", null);

        var logs = await Factory.WithDbAsync(db =>
            db.AuditLogs.Where(a => a.EntityName == "User" && a.EntityId == id).ToListAsync());

        var actions = logs.Select(l => l.Action).ToList();
        Assert.Contains("UserCreated", actions);
        Assert.Contains("UserPasswordReset", actions);
        Assert.Contains("UserDeactivated", actions);
        Assert.All(logs, l => Assert.Equal(Factory.AdminUserId, l.UserId));
        Assert.All(logs, l =>
        {
            Assert.DoesNotContain(password, l.Description ?? "");
            Assert.DoesNotContain(fresh, l.Description ?? "");
        });
    }
}
