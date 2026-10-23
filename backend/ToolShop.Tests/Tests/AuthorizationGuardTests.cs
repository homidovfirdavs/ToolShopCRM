using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ToolShop.Tests.Tests;

/// <summary>
/// T2b guard rails (no database needed): access rules live in permission policies, and no API action is open by accident.
/// </summary>
public class AuthorizationGuardTests
{
    // Actions that are intentionally reachable without a token.
    private static readonly HashSet<string> AnonymousActions =
    [
        "AuthController.Login",
        "AuthController.Refresh",
        "AuthController.Logout",
        "AuthController.MfaSetup",    // T2d: proven by the short-lived MFA session token, not by a bearer token
        "AuthController.MfaEnable",
        "AuthController.MfaVerify",
        "HealthController.Get"
    ];

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any());

    [Fact]
    public void Access_rules_use_permission_policies_not_role_names()
    {
        var problems = new List<string>();
        foreach (var c in Controllers())
        {
            foreach (var a in c.GetCustomAttributes<AuthorizeAttribute>(true).Where(a => !string.IsNullOrEmpty(a.Roles)))
                problems.Add($"{c.Name} (class): Roles = \"{a.Roles}\"");

            foreach (var m in Actions(c))
                foreach (var a in m.GetCustomAttributes<AuthorizeAttribute>(true).Where(a => !string.IsNullOrEmpty(a.Roles)))
                    problems.Add($"{c.Name}.{m.Name}: Roles = \"{a.Roles}\"");
        }

        Assert.True(problems.Count == 0, "Use [Authorize(Policy = Permissions.X)] instead: " + string.Join("; ", problems));
    }

    [Fact]
    public void Every_action_requires_authorization_unless_it_is_on_the_anonymous_list()
    {
        var open = new List<string>();
        foreach (var c in Controllers())
        {
            var classProtected = c.GetCustomAttributes<AuthorizeAttribute>(true).Any();
            foreach (var m in Actions(c))
            {
                var isProtected = classProtected || m.GetCustomAttributes<AuthorizeAttribute>(true).Any();
                var name = $"{c.Name}.{m.Name}";
                if (!isProtected && !AnonymousActions.Contains(name)) open.Add(name);
            }
        }

        Assert.True(open.Count == 0, "Actions without [Authorize]: " + string.Join("; ", open));
    }

    [Fact]
    public void Anonymous_list_has_no_stale_entries()
    {
        var existing = Controllers().SelectMany(c => Actions(c).Select(m => $"{c.Name}.{m.Name}")).ToHashSet();
        var stale = AnonymousActions.Where(a => !existing.Contains(a)).ToList();
        Assert.True(stale.Count == 0, "Not found: " + string.Join("; ", stale));
    }
}
