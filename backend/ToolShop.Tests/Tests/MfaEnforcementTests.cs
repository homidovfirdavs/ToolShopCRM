using Microsoft.Extensions.Configuration;
using ToolShop.Infrastructure.Auth;

namespace ToolShop.Tests.Tests;

/// <summary>T2d.4: MFA is on by default for Admin and Manager, and the host refuses to start without it outside Development/Testing.</summary>
public class MfaEnforcementTests
{
    private static IConfiguration Config(string? value)
    {
        var data = new Dictionary<string, string?>();
        if (value is not null) data["Security:MfaRequiredRoles"] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void Without_the_setting_Admin_and_Manager_must_use_MFA()
    {
        var policy = new MfaPolicy(Config(null));
        Assert.True(policy.IsRequiredFor(["Admin"]));
        Assert.True(policy.IsRequiredFor(["Manager"]));
        Assert.False(policy.IsRequiredFor(["Cashier"]));
        Assert.False(policy.IsRequiredFor(["Warehouse"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    public void An_explicit_value_switches_it_off_when_it_names_no_real_role(string value)
    {
        var policy = new MfaPolicy(Config(value));
        Assert.False(policy.IsRequiredFor(["Admin"]));
        Assert.False(policy.IsRequiredFor(["Manager"]));
    }

    [Fact]
    public void An_explicit_list_is_used_as_written()
    {
        var policy = new MfaPolicy(Config("Admin, Cashier"));
        Assert.True(policy.IsRequiredFor(["Cashier"]));
        Assert.True(policy.IsRequiredFor(["Admin"]));
        Assert.False(policy.IsRequiredFor(["Manager"]));
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "Admin,Manager")]
    [InlineData("Production", "Manager, Admin, Cashier")]
    [InlineData("Staging", null)]
    public void Production_like_environments_start_when_Admin_and_Manager_are_covered(string env, string? value) =>
        Assert.Null(MfaPolicy.EnforcementProblem(Config(value), env));

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "none")]
    [InlineData("Production", "Admin")]
    [InlineData("Production", "Manager,Cashier")]
    [InlineData("Staging", "")]
    public void Production_like_environments_refuse_to_start_without_MFA_for_Admin_or_Manager(string env, string value)
    {
        var problem = MfaPolicy.EnforcementProblem(Config(value), env);
        Assert.NotNull(problem);
        Assert.Contains("Security:MfaRequiredRoles", problem);
    }

    [Theory]
    [InlineData("Development", "")]
    [InlineData("development", "none")]
    [InlineData("Testing", "")]
    [InlineData("Testing", "none")]
    public void Development_and_Testing_may_switch_it_off(string env, string value) =>
        Assert.Null(MfaPolicy.EnforcementProblem(Config(value), env));
}
