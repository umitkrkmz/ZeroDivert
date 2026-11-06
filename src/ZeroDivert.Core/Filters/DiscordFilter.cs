namespace ZeroDivert.Core.Filters;

/// <summary>
/// Discord-specific traffic filter
/// </summary>
public static class DiscordFilter
{
    // Discord domains for SNI matching
    private static readonly string[] DiscordDomains =
    [
        "discord.com",
        "discord.gg",
        "discordapp.com",
        "discordapp.net",
        "discord.media",
        "discordcdn.com",
        "discord.dev",
        "discord.new",
        "discord.gift",
        "discordstatus.com",
        "dis.gd",
        "discord.co",
        "discord.design",
        "discord-activities.com",
        "discordactivities.com",
        "discordsays.com",
        "discordmerch.com"
    ];

    /// <summary>
    /// WinDivert filter for the capture loop:
    /// - outbound TCP to 443: every TLS connection, so Discord ClientHellos can be
    ///   found by SNI, and so HandshakeTracker can see the client continue a
    ///   handshake (i.e. the server answered);
    /// - inbound RSTs from port 443: the only inbound packets needed, to notice DPI
    ///   resetting a Discord handshake. They are few, so this costs nothing.
    /// - with <paramref name="interceptDns"/>: outbound DNS queries (UDP 53, any
    ///   app, IPv4 and IPv6) for DnsInterceptor. Loopback is excluded so a local
    ///   DNS proxy's own traffic is left alone.
    /// No other UDP and no port 80: Discord's voice traffic is left alone
    /// (GoodbyeDPI doesn't touch it either), and Discord is HTTPS-only.
    /// </summary>
    public static string GenerateFilter(bool interceptDns = false) =>
        "(outbound and tcp.DstPort == 443) or " +
        "(inbound and tcp.SrcPort == 443 and tcp.Rst)" +
        (interceptDns ? " or (outbound and !loopback and udp.DstPort == 53)" : "");

    /// <summary>
    /// Checks if SNI matches Discord domain
    /// </summary>
    public static bool IsDiscordSni(string? sni)
    {
        if (string.IsNullOrEmpty(sni)) return false;

        foreach (var domain in DiscordDomains)
        {
            if (sni.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                sni.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
