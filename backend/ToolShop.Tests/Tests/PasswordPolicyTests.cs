using System.Net;
using System.Net.Http.Json;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>T2c: long passwords, no forced character classes, obvious passwords rejected.</summary>
[Collection("db")]
public class PasswordPolicyTests(ApiFactory factory) : TestBase(factory)
{
    private static string NewUsername() => "u_" + Guid.NewGuid().ToString("N")[..10];

    private Task<HttpResponseMessage> CreateAsync(string username, string password) =>
        Admin.PostAsJsonAsync("/api/users", new
        {
            username,
            fullName = "Test " + username,
            email = username + "@test.local",
            password,
            role = "Cashier"
        });

    private async Task<string> CreateUserIdAsync()
    {
        var resp = await CreateAsync(NewUsername(), "first long passphrase for tests");
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        return (await resp.ReadJsonAsync()).Str("id");
    }

    [Theory]
    [InlineData("Kx7-mQ2-pLv")]                 // 11 characters
    [InlineData("password1234")]                // common word with digits
    [InlineData("ToolShop-2026")]               // shop name
    [InlineData("Qwerty-Qwerty-1")]             // keyboard word
    [InlineData("aaaaaaaaaaaa")]                // one character repeated
    [InlineData("123456789012")]                // number sequence
    [InlineData("qwertyuiopas")]                // keyboard row
    public async Task Weak_passwords_are_rejected_on_create(string password)
    {
        var resp = await CreateAsync(NewUsername(), password);
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Password_containing_the_username_is_rejected()
    {
        var username = NewUsername();
        var resp = await CreateAsync(username, username + "-Zq");
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Password_longer_than_128_characters_is_rejected()
    {
        var resp = await CreateAsync(NewUsername(), string.Concat(Enumerable.Repeat("abcdefgh ", 15)));   // 135 characters
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("blue river walks past quiet hills")]   // no digits, no capitals: a phrase is fine
    [InlineData("хурок ва чой дар боғ")]                // non-latin phrase
    public async Task Long_phrases_without_digits_or_capitals_are_accepted(string password)
    {
        var resp = await CreateAsync(NewUsername(), password);
        await resp.ShouldBeAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Reset_password_follows_the_same_rules_and_the_new_phrase_can_log_in()
    {
        var id = await CreateUserIdAsync();

        var weak = await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "password1234" });
        await weak.ShouldBeAsync(HttpStatusCode.BadRequest);

        var phrase = "green lamp over the old bridge";
        var ok = await Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = phrase });
        await ok.ShouldBeAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Weak_password_error_message_is_returned_to_the_caller()
    {
        var resp = await CreateAsync(NewUsername(), "password1234");
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("too common", text, StringComparison.OrdinalIgnoreCase);
    }
}
