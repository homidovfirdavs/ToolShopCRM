using Microsoft.AspNetCore.Identity;
using ToolShop.Domain.Entities;

namespace ToolShop.Infrastructure.Identity;

/// <summary>
/// T2c: extra password rules on top of Identity's length rule. No forced digits / capitals: a long phrase is fine.
/// This is a plain rule set, not a database of leaked passwords.
/// </summary>
public sealed class ToolShopPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const int MaxLength = 128;
    private const int MinDistinctCharacters = 5;
    private const int MinUserInfoLength = 4;

    // Words that make a password guessable even with digits or symbols around them.
    private static readonly string[] BannedParts =
        ["toolshop", "password", "passw0rd", "qwerty", "admin", "letmein", "iloveyou", "welcome", "parol"];

    // Letters-only stems of very common passwords ("Dragon-2024!" -> "dragon").
    private static readonly HashSet<string> CommonStems = new(StringComparer.Ordinal)
    {
        "monkey", "dragon", "master", "shadow", "sunshine", "princess", "superman", "football", "baseball",
        "trustno", "abc", "abcdef", "abcdefgh", "freedom", "whatever", "mustang", "michael", "jordan", "hello",
        "login", "starwars", "computer", "internet", "service", "secret", "changeme", "default", "test", "guest"
    };

    private static readonly string[] KeyboardRows = ["qwertyuiop", "asdfghjkl", "zxcvbnm"];

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Task.FromResult(IdentityResult.Success); // Identity's own validator reports the empty password

        var errors = new List<IdentityError>();
        var lower = password.ToLowerInvariant();

        if (password.Length > MaxLength)
            errors.Add(Error("PasswordTooLong", $"Password must be at most {MaxLength} characters."));

        if (password.Distinct().Count() < MinDistinctCharacters)
            errors.Add(Error("PasswordTooRepetitive", "Password uses too few different characters."));

        if (IsDigitSequence(password))
            errors.Add(Error("PasswordSequential", "Password must not be a simple number sequence."));

        var stem = new string(lower.Where(char.IsLetter).ToArray());
        if (BannedParts.Any(b => lower.Contains(b)) || CommonStems.Contains(stem) || IsKeyboardRun(stem))
            errors.Add(Error("PasswordTooCommon", "Password is too common or easy to guess."));

        if (ContainsUserInfo(lower, user))
            errors.Add(Error("PasswordContainsUserInfo", "Password must not contain the username or email name."));

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray()));
    }

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    private static bool IsDigitSequence(string p)
    {
        if (p.Length < 2 || !p.All(char.IsDigit)) return false;
        var step = p[1] - p[0];
        if (step is not (0 or 1 or -1)) return false;
        for (var i = 1; i < p.Length; i++)
        {
            var d = p[i] - p[i - 1];
            var wrapped = (p[i - 1] == '9' && p[i] == '0') || (p[i - 1] == '0' && p[i] == '9');
            if (d != step && !(wrapped && Math.Abs(step) == 1)) return false;
        }
        return true;
    }

    private static bool IsKeyboardRun(string stem)
    {
        if (stem.Length < 6) return false;
        var reversed = new string(stem.Reverse().ToArray());
        return KeyboardRows.Any(r => r.Contains(stem) || r.Contains(reversed));
    }

    private static bool ContainsUserInfo(string lowerPassword, ApplicationUser user)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(user.UserName)) candidates.Add(user.UserName.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var at = user.Email.IndexOf('@');
            candidates.Add((at > 0 ? user.Email[..at] : user.Email).ToLowerInvariant());
        }
        return candidates.Any(c => c.Length >= MinUserInfoLength && lowerPassword.Contains(c));
    }
}
