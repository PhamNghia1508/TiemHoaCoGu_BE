using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace HoaSap.API.Security;

/// <summary>One trust boundary for scheme, redirects and both rate limiters.</summary>
public static class TrustedProxyConfiguration
{
    public static ForwardedHeadersOptions CreateOptions(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions();
        // Never inherit the framework's implicit loopback trust.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        if (!configuration.GetValue<bool>("RateLimiting:TrustForwardedHeaders")) return options;

        var proxies = configuration.GetSection("RateLimiting:TrustedProxies").Get<string[]>() ?? [];
        if (proxies.Length == 0)
            throw new InvalidOperationException("RateLimiting:TrustedProxies must contain explicit proxy IP addresses when forwarded headers are enabled.");
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException("RateLimiting:TrustedProxies must contain only valid IP addresses.");
            options.KnownProxies.Add(address);
            // Kestrel can represent an IPv4 peer on a dual-mode socket as IPv6.
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                options.KnownProxies.Add(address.MapToIPv6());
        }
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Read the closest hop, not a spoofable leftmost address in a client-supplied chain.
        options.ForwardLimit = 1;
        return options;
    }
}
