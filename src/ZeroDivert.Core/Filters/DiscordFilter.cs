using System.Net;

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

    // Discord IP ranges (approximate, may need updates)
    // These are Cloudflare and Discord's own ranges
    private static readonly (uint Start, uint End)[] DiscordIpRanges =
    [
        // Discord's own ranges
        (ToUint("162.159.128.0"), ToUint("162.159.135.255")),
        (ToUint("162.159.136.0"), ToUint("162.159.143.255")),
        (ToUint("162.159.152.0"), ToUint("162.159.159.255")),

        // Common Cloudflare ranges used by Discord
        (ToUint("104.16.0.0"), ToUint("104.31.255.255")),
        (ToUint("172.64.0.0"), ToUint("172.71.255.255")),
        (ToUint("141.101.64.0"), ToUint("141.101.127.255")),
    ];

    // Ports used by Discord
    public static readonly ushort[] DiscordPorts = [443, 80];
    public static readonly (ushort Min, ushort Max) VoicePortRange = (50000, 65535);

    /// <summary>
    /// Generates WinDivert filter string for Discord traffic
    /// </summary>
    public static string GenerateFilter()
    {
        // Filter for HTTPS (443) and HTTP (80) outbound
        // We catch outbound to process before DPI sees it
        return "outbound and " +
               "(tcp.DstPort == 443 or tcp.DstPort == 80 or " +
               "(udp.DstPort >= 50000 and udp.DstPort <= 65535) or udp.DstPort == 443)";
    }

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

    /// <summary>
    /// Checks if destination IP is in Discord's range
    /// </summary>
    public static bool IsDiscordIp(ReadOnlySpan<byte> ipv4Address)
    {
        if (ipv4Address.Length != 4) return false;

        var ip = (uint)((ipv4Address[0] << 24) | (ipv4Address[1] << 16) | (ipv4Address[2] << 8) | ipv4Address[3]);

        foreach (var (start, end) in DiscordIpRanges)
        {
            if (ip >= start && ip <= end) return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the port is a Discord voice port
    /// </summary>
    public static bool IsVoicePort(ushort port) =>
        port >= VoicePortRange.Min && port <= VoicePortRange.Max;

    private static uint ToUint(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }
}
