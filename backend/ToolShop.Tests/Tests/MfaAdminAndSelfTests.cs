using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ToolShop.Domain.Entities;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>T2d.2: an Admin resets another user's MFA; optional users switch MFA on and off themselves.</summary>
[Collection("db")]
public class MfaAdminAndSelfTests(ApiFactory factory) : TestBase(factory)
{
    private const string Phrase = "quiet lamp over the long bridge";

    private static string NewUsername() => "u_" + Guid.NewGuid().ToString("N")[..10];

    private WebApplicationFactory<Program> MfaServer() =>
        Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MfaRequiredRoles"] = "Admin,Manager" })));

    private async Task<(string Id, string Username)> CreateUserAsync(string role)
    {
        var username = NewUsername();
        var resp = await Admin.PostAsJsonAsync("/api/users", new
        {
            username,
            fullName = "Test " + username,
            email = username + "@test.local",
            password = Phrase,
            role
        });
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        return ((await resp.ReadJsonAsync()).Str("id"), username);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient c, string username) =>
        c.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = username, password = Phrase });

    private static HttpClient Bearer(WebApplicationFactory<Program> server, string token)
    {
        var c = server.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>Manager-style enrollment through the login challenge. Returns the key and the enable response.</summary>
    private async Task<(string Key, JsonElement Enabled)> EnrollThroughLoginAsync(HttpClient c, string username)
    {
        var mfaToken = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        var setup = await c.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken });
        await setup.ShouldBeAsync(HttpStatusCode.OK);
        var key = (await setup.ReadJsonAsync()).Str("sharedKey");
        var enable = await c.PostAsJsonAsync("/api/auth/mfa/enable", new { mfaToken, code = Totp.Code(key) });
        await enable.ShouldBeAsync(HttpStatusCode.OK);
        return (key, await enable.ReadJsonAsync());
    }

    /// <summary>Self-service enrollment of a signed-in Cashier. Returns the key and the enable response.</summary>
    private async Task<(string Key, JsonElement Enabled)> EnrollSelfAsync(HttpClient c)
    {
        var setup = await c.PostAsync("/api/auth/mfa/self/setup", null);
        await setup.ShouldBeAsync(HttpStatusCode.OK);
        var key = (await setup.ReadJsonAsync()).Str("sharedKey");
        var enable = await c.PostAsJsonAsync("/api/auth/mfa/self/enable", new { code = Totp.Code(key) });
        await enable.ShouldBeAsync(HttpStatusCode.OK);
        return (key, await enable.ReadJsonAsync());
    }

    private async Task<HttpClient> SignedInCashierAsync(string username)
    {
        var login = await LoginAsync(Anonymous, username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        var c = Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await login.ReadJsonAsync()).Str("accessToken"));
        return c;
    }

    // ---------- Admin reset ----------

    [Fact]
    public async Task Admin_resets_the_mfa_of_another_user_and_that_user_must_set_it_up_again()
    {
        await using var server = MfaServer();
        var (id, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();
        var (_, enabled) = await EnrollThroughLoginAsync(c, username);
        var oldRefresh = enabled.Str("refreshToken");

        var before = await (await Admin.GetAsync("/api/users")).ReadJsonAsync();
        Assert.True(before.EnumerateArray().Single(u => u.Str("id") == id).GetProperty("mfaEnabled").GetBoolean());

        var reset = await Admin.PostAsync($"/api/users/{id}/mfa/reset", null);
        await reset.ShouldBeAsync(HttpStatusCode.NoContent);

        var after = await (await Admin.GetAsync("/api/users")).ReadJsonAsync();
        Assert.False(after.EnumerateArray().Single(u => u.Str("id") == id).GetProperty("mfaEnabled").GetBoolean());

        // old sessions are gone, the next login asks for a fresh setup
        var refresh = await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var login = await (await LoginAsync(c, username)).ReadJsonAsync();
        Assert.True(login.GetProperty("mfaSetupRequired").GetBoolean());

        var audit = await Factory.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == "UserMfaReset").ToListAsync());
        Assert.Single(audit);
        Assert.Equal(Factory.AdminUserId, audit[0].UserId);
    }

    [Fact]
    public async Task Admin_cannot_reset_their_own_mfa_and_others_cannot_use_the_endpoint()
    {
        var own = await Admin.PostAsync($"/api/users/{Factory.AdminUserId}/mfa/reset", null);
        await own.ShouldBeAsync(HttpStatusCode.BadRequest);

        var (id, _) = await CreateUserAsync("Cashier");
        foreach (var client in new[] { Manager, Cashier, Warehouse })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/users/{id}/mfa/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.PostAsync($"/api/users/{id}/mfa/reset", null)).StatusCode);
    }

    // ---------- self-service for an optional role ----------

    [Fact]
    public async Task Cashier_can_switch_mfa_on_and_then_must_use_it_to_log_in()
    {
        var (_, username) = await CreateUserAsync("Cashier");
        var c = await SignedInCashierAsync(username);

        var (key, enabled) = await EnrollSelfAsync(c);
        Assert.Equal(10, enabled.GetProperty("recoveryCodes").GetArrayLength());

        var login = await (await LoginAsync(Anonymous, username)).ReadJsonAsync();
        Assert.True(login.GetProperty("mfaRequired").GetBoolean());
        Assert.False(login.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(login.TryGetProperty("accessToken", out _));

        var verify = await Anonymous.PostAsJsonAsync("/api/auth/mfa/verify",
            new { mfaToken = login.Str("mfaToken"), code = Totp.Code(key) });
        await verify.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cashier_can_switch_mfa_off_with_password_and_code()
    {
        var (_, username) = await CreateUserAsync("Cashier");
        var c = await SignedInCashierAsync(username);
        var (key, enabled) = await EnrollSelfAsync(c);

        using var withMfaToken = Factory.CreateClient();
        withMfaToken.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", enabled.Str("accessToken"));

        var wrongPassword = await withMfaToken.PostAsJsonAsync("/api/auth/mfa/self/disable",
            new { password = "not the password at all", code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var off = await withMfaToken.PostAsJsonAsync("/api/auth/mfa/self/disable",
            new { password = Phrase, code = Totp.Code(key) });
        await off.ShouldBeAsync(HttpStatusCode.NoContent);

        var login = await LoginAsync(Anonymous, username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False((await login.ReadJsonAsync()).TryGetProperty("mfaRequired", out _));
    }

    [Fact]
    public async Task A_role_that_requires_mfa_cannot_switch_it_off()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var (key, enabled) = await EnrollThroughLoginAsync(server.CreateClient(), username);

        using var manager = Bearer(server, enabled.Str("accessToken"));
        var off = await manager.PostAsJsonAsync("/api/auth/mfa/self/disable", new { password = Phrase, code = Totp.Code(key) });
        await off.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task New_recovery_codes_replace_the_old_ones()
    {
        var (_, username) = await CreateUserAsync("Cashier");
        var c = await SignedInCashierAsync(username);
        var (key, enabled) = await EnrollSelfAsync(c);
        var oldCode = enabled.GetProperty("recoveryCodes")[0].GetString()!;

        using var authed = Factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enabled.Str("accessToken"));
        var fresh = await authed.PostAsJsonAsync("/api/auth/mfa/self/recovery-codes", new { code = Totp.Code(key) });
        await fresh.ShouldBeAsync(HttpStatusCode.OK);
        var newCodes = (await fresh.ReadJsonAsync()).GetProperty("recoveryCodes");
        Assert.Equal(10, newCodes.GetArrayLength());

        var t1 = (await (await LoginAsync(Anonymous, username)).ReadJsonAsync()).Str("mfaToken");
        var oldTry = await Anonymous.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken = t1, recoveryCode = oldCode });
        Assert.Equal(HttpStatusCode.Unauthorized, oldTry.StatusCode);

        var t2 = (await (await LoginAsync(Anonymous, username)).ReadJsonAsync()).Str("mfaToken");
        var newTry = await Anonymous.PostAsJsonAsync("/api/auth/mfa/verify",
            new { mfaToken = t2, recoveryCode = newCodes[0].GetString() });
        await newTry.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Self_service_endpoints_need_a_signed_in_user()
    {
        foreach (var path in new[] { "setup", "enable", "disable", "recovery-codes" })
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await Anonymous.PostAsJsonAsync($"/api/auth/mfa/self/{path}", new { })).StatusCode);
    }
}
