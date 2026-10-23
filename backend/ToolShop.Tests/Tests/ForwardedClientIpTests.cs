using System.Net;
using ToolShop.API.Middleware;

namespace ToolShop.Tests.Tests;

/// <summary>T2e: the client address from X-Forwarded-For is trusted only when the direct peer is the proxy.</summary>
public class ForwardedClientIpTests
{
    private static readonly IReadOnlyList<System.Net.IPNetwork> Trusted =
        ForwardedClientIp.ParseNetworks(ForwardedClientIp.DefaultTrustedNetworks);

    private static IPAddress? Resolve(string? peer, string? header) =>
        ForwardedClientIp.Resolve(peer is null ? null : IPAddress.Parse(peer), header, Trusted);

    [Theory]
    [InlineData("172.18.0.5", "203.0.113.7")]
    [InlineData("10.1.2.3", "203.0.113.7")]
    [InlineData("192.168.0.9", "203.0.113.7")]
    [InlineData("127.0.0.1", "203.0.113.7")]
    [InlineData("::ffff:172.18.0.5", "203.0.113.7")]
    public void A_trusted_proxy_supplies_the_client_address(string peer, string header) =>
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(peer, header));

    [Theory]
    [InlineData("203.0.113.50")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]   // just outside 172.16.0.0/12
    public void A_peer_outside_the_trusted_networks_cannot_choose_its_own_address(string peer) =>
        Assert.Null(Resolve(peer, "198.51.100.99"));

    [Fact]
    public void The_last_entry_wins_so_extra_values_sent_by_a_client_are_ignored()
    {
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve("172.18.0.5", "1.2.3.4, 9.9.9.9, 203.0.113.7"));
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve("172.18.0.5", " 203.0.113.7 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("203.0.113.7:8080")]
    public void A_missing_or_invalid_header_keeps_the_connection_address(string? header) =>
        Assert.Null(Resolve("172.18.0.5", header));

    [Fact]
    public void No_peer_address_means_nothing_is_trusted() =>
        Assert.Null(ForwardedClientIp.Resolve(null, "203.0.113.7", Trusted));

    [Fact]
    public void An_IPv6_client_address_is_accepted() =>
        Assert.Equal(IPAddress.Parse("2001:db8::1"), Resolve("172.18.0.5", "2001:db8::1"));

    [Fact]
    public void Custom_networks_replace_the_defaults()
    {
        var only = ForwardedClientIp.ParseNetworks(["172.20.0.0/16"]);
        Assert.NotNull(ForwardedClientIp.Resolve(IPAddress.Parse("172.20.5.5"), "203.0.113.7", only));
        Assert.Null(ForwardedClientIp.Resolve(IPAddress.Parse("172.18.5.5"), "203.0.113.7", only));
    }
}
