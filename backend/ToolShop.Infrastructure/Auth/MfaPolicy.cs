using Microsoft.Extensions.Configuration;

namespace ToolShop.Infrastructure.Auth;

/// <summary>
/// T2d: which roles must use a second factor. Read from Security:MfaRequiredRoles (comma separated, e.g. "Admin,Manager")
/// on every call, so it follows the live configuration.
/// </summary>
public class MfaPolicy(IConfiguration configuration)
{
    public const string ConfigKey = "Security:MfaRequiredRoles";

    public HashSet<string> RequiredRoles() =>
        (configuration[ConfigKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsRequiredFor(IEnumerable<string> roles)
    {
        var required = RequiredRoles();
        return required.Count > 0 && roles.Any(r => required.Contains(r));
    }
}
