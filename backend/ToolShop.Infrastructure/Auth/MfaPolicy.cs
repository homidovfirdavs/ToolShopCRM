using Microsoft.Extensions.Configuration;

namespace ToolShop.Infrastructure.Auth;

/// <summary>
/// T2d: which roles must use a second factor. Read from Security:MfaRequiredRoles (comma separated) on every call,
/// so it follows the live configuration.
/// T2d.4: when the key is NOT set at all, Admin and Manager must use MFA. Only an explicitly set value changes that
/// (an empty value or a role name nobody has, such as "none", switches enforcement off - allowed only in Development/Testing).
/// </summary>
public class MfaPolicy(IConfiguration configuration)
{
    public const string ConfigKey = "Security:MfaRequiredRoles";
    public const string DefaultRoles = "Admin,Manager";

    /// <summary>Roles that may never run without MFA outside Development/Testing.</summary>
    public static readonly string[] MandatoryRoles = ["Admin", "Manager"];

    public HashSet<string> RequiredRoles() =>
        (configuration[ConfigKey] ?? DefaultRoles)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsRequiredFor(IEnumerable<string> roles)
    {
        var required = RequiredRoles();
        return required.Count > 0 && roles.Any(r => required.Contains(r));
    }

    /// <summary>
    /// Startup guard. Returns a message when the configuration would let Admin or Manager sign in without MFA in an
    /// environment other than Development/Testing (the host must then refuse to start), otherwise null.
    /// </summary>
    public static string? EnforcementProblem(IConfiguration configuration, string environmentName)
    {
        if (environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
            environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
            return null;

        var required = new MfaPolicy(configuration).RequiredRoles();
        var missing = MandatoryRoles.Where(r => !required.Contains(r)).ToArray();
        return missing.Length == 0
            ? null
            : $"{ConfigKey} must include {string.Join(" and ", MandatoryRoles)} outside Development/Testing " +
              $"(missing: {string.Join(", ", missing)}). Remove the setting to use the default \"{DefaultRoles}\".";
    }
}
