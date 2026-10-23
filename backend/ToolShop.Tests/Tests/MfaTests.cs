using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Auth;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>T2d.1: second factor (TOTP + recovery codes) for roles listed in Security:MfaRequiredRoles.</summary>
[Collection("db")]
public class MfaTests(ApiFactory factory) : TestBase(factory)
{
    private const string Phrase = "quiet lamp over the long bridge";

    private static string NewUsername() => "u_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>A second server on the same database where Admin and Manager must use MFA.</summary>
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

    /// <summary>Full first-time enrollment: login, setup, enable. Returns the shared key and the enable response.</summary>
    private async Task<(string Key, JsonElement Enabled)> EnrollAsync(HttpClient c, string username)
    {
        var login = await LoginAsync(c, username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        var mfaToken = (await login.ReadJsonAsync()).Str("mfaToken");

        var setup = await c.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken });
        await setup.ShouldBeAsync(HttpStatusCode.OK);
        var key = (await setup.ReadJsonAsync()).Str("sharedKey");

        var enable = await c.PostAsJsonAsync("/api/auth/mfa/enable", new { mfaToken, code = Totp.Code(key) });
        await enable.ShouldBeAsync(HttpStatusCode.OK);
        return (key, await enable.ReadJsonAsync());
    }

    private Task<List<AuditLog>> AuditAsync(string action) =>
        Factory.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == action).ToListAsync());

    [Fact]
    public async Task Manager_login_returns_a_challenge_and_no_tokens_and_the_challenge_is_not_an_access_token()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();

        var login = await LoginAsync(c, username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        var body = await login.ReadJsonAsync();

        Assert.True(body.GetProperty("mfaRequired").GetBoolean());
        Assert.True(body.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(body.TryGetProperty("accessToken", out _));

        using var withChallenge = Bearer(server, body.Str("mfaToken"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await withChallenge.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Enrollment_gives_working_tokens_and_ten_recovery_codes()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");

        var (_, enabled) = await EnrollAsync(server.CreateClient(), username);

        Assert.Equal(10, enabled.GetProperty("recoveryCodes").GetArrayLength());
        using var client = Bearer(server, enabled.Str("accessToken"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Single(await AuditAsync("Auth.MfaEnabled"));
    }

    [Fact]
    public async Task After_enrollment_login_needs_a_code_and_a_wrong_code_is_audited()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();
        var (key, _) = await EnrollAsync(c, username);

        var login = await LoginAsync(c, username);
        var body = await login.ReadJsonAsync();
        Assert.True(body.GetProperty("mfaRequired").GetBoolean());
        Assert.False(body.GetProperty("mfaSetupRequired").GetBoolean());
        var mfaToken = body.Str("mfaToken");

        var wrong = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken, code = Totp.WrongCode(key) });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Single(await AuditAsync("Auth.MfaFailed"));

        var good = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken, code = Totp.Code(key) });
        await good.ShouldBeAsync(HttpStatusCode.OK);
        using var client = Bearer(server, (await good.ReadJsonAsync()).Str("accessToken"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_codes_block_even_the_right_code_for_a_while()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();
        var (key, _) = await EnrollAsync(c, username);

        var mfaToken = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        for (var i = 0; i < 5; i++)
        {
            var r = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken, code = Totp.WrongCode(key) });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        // A fresh password login does not clear the block.
        var again = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        var blocked = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken = again, code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotEmpty(await AuditAsync("Auth.MfaBlocked"));
    }

    [Fact]
    public async Task A_recovery_code_works_exactly_once()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();
        var (_, enabled) = await EnrollAsync(c, username);
        var recovery = enabled.GetProperty("recoveryCodes")[0].GetString()!;

        var t1 = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        var first = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken = t1, recoveryCode = recovery });
        await first.ShouldBeAsync(HttpStatusCode.OK);

        var t2 = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        var second = await c.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken = t2, recoveryCode = recovery });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Single(await AuditAsync("Auth.RecoveryCodeUsed"));
    }

    [Fact]
    public async Task Cashier_without_mfa_still_logs_in_normally()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Cashier");

        var login = await LoginAsync(server.CreateClient(), username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False((await login.ReadJsonAsync()).TryGetProperty("mfaRequired", out _));
    }

    [Fact]
    public async Task Cashier_who_switched_mfa_on_must_use_it()
    {
        await using var server = MfaServer();
        var (id, username) = await CreateUserAsync("Cashier");
        using (var scope = server.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await um.FindByIdAsync(id))!;
            await um.ResetAuthenticatorKeyAsync(user);
            await um.SetTwoFactorEnabledAsync(user, true);
        }

        var body = await (await LoginAsync(server.CreateClient(), username)).ReadJsonAsync();
        Assert.True(body.GetProperty("mfaRequired").GetBoolean());
        Assert.False(body.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(body.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task A_manager_token_without_the_mfa_claim_is_rejected()
    {
        await using var server = MfaServer();
        var (id, _) = await CreateUserAsync("Manager");

        using var scope = server.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var user = (await um.FindByIdAsync(id))!;
        var roles = await um.GetRolesAsync(user);

        using var noMfa = Bearer(server, tokens.GenerateAccessToken(user, roles, mfaVerified: false).AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noMfa.GetAsync("/api/orders")).StatusCode);

        using var withMfa = Bearer(server, tokens.GenerateAccessToken(user, roles, mfaVerified: true).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await withMfa.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Refresh_is_refused_for_a_required_role_that_has_no_mfa()
    {
        await using var server = MfaServer();
        var (id, _) = await CreateUserAsync("Manager");

        string raw;
        using (var scope = server.Services.CreateScope())
            raw = await scope.ServiceProvider.GetRequiredService<RefreshTokenService>().IssueAsync(id);

        var resp = await server.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = raw });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Invalid_sessions_and_double_enrollment_are_rejected()
    {
        await using var server = MfaServer();
        var (_, username) = await CreateUserAsync("Manager");
        var c = server.CreateClient();

        var garbage = await c.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);

        var (_, enabled) = await EnrollAsync(c, username);
        var accessAsSession = await c.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken = enabled.Str("accessToken") });
        Assert.Equal(HttpStatusCode.Unauthorized, accessAsSession.StatusCode);

        var fresh = (await (await LoginAsync(c, username)).ReadJsonAsync()).Str("mfaToken");
        var again = await c.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken = fresh });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Default_server_is_unchanged_when_no_role_requires_mfa()
    {
        var (_, username) = await CreateUserAsync("Manager");
        var login = await LoginAsync(Anonymous, username);
        await login.ShouldBeAsync(HttpStatusCode.OK);
        Assert.False((await login.ReadJsonAsync()).TryGetProperty("mfaRequired", out _));
    }
}

/// <summary>RFC 6238 time-based one-time passwords (SHA-1, 6 digits, 30 s), the format authenticator apps use.</summary>
internal static class Totp
{
    private static byte[] Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var bytes = new List<byte>();
        foreach (var ch in text.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(ch);
            if (value < 0) continue;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }

    public static string Code(string base32Key, int stepOffset = 0)
    {
        var step = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds / 30 + stepOffset;
        var message = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(message);

        using var hmac = new HMACSHA1(Base32Decode(base32Key));
        var hash = hmac.ComputeHash(message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>A six-digit code that is not valid in the window the server accepts (+-2 steps).</summary>
    public static string WrongCode(string base32Key)
    {
        var valid = Enumerable.Range(-3, 7).Select(i => Code(base32Key, i)).ToHashSet();
        return Enumerable.Range(0, 1000).Select(i => i.ToString("D6")).First(c => !valid.Contains(c));
    }
}
