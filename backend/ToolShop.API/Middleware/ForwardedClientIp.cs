using System.Net;

namespace ToolShop.API.Middleware;

/// <summary>
/// T2e: behind Nginx every request reaches the API from the proxy's address. This resolves the REAL client address from
/// X-Forwarded-For, but only when the direct peer is inside a trusted network (the proxy). A request from anywhere else
/// keeps its own address, so a client can never choose its own IP by sending the header. The LAST entry is used: it is the one
/// added by the proxy closest to the API (Nginx sets it to $remote_addr).
/// </summary>
public static class ForwardedClientIp
{
    /// <summary>Loopback and private ranges (Docker networks live here). The API container publishes no port to the host.</summary>
    public static readonly string[] DefaultTrustedNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static IReadOnlyList<System.Net.IPNetwork> ParseNetworks(IEnumerable<string> cidrs) =>
        [.. cidrs.Select(c => System.Net.IPNetwork.Parse(c.Trim()))];

    /// <summary>The client address to use, or null when the connection's own address must stay.</summary>
    public static IPAddress? Resolve(IPAddress? peer, string? forwardedFor, IReadOnlyList<System.Net.IPNetwork> trusted)
    {
        if (peer is null || string.IsNullOrWhiteSpace(forwardedFor)) return null;

        var direct = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        if (!trusted.Any(n => n.Contains(direct))) return null;

        var last = forwardedFor
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return last is not null && IPAddress.TryParse(last, out var ip) ? ip : null;
    }

    public static IApplicationBuilder UseTrustedForwardedClientIp(
        this IApplicationBuilder app, IReadOnlyList<System.Net.IPNetwork> trusted) =>
        app.Use(async (ctx, next) =>
        {
            var ip = Resolve(ctx.Connection.RemoteIpAddress, ctx.Request.Headers["X-Forwarded-For"].ToString(), trusted);
            if (ip is not null) ctx.Connection.RemoteIpAddress = ip;
            await next();
        });
}
