namespace ZeroDivert.Core.Bypass;

/// <summary>
/// DPI bypass strategy interface
/// </summary>
public interface IDpiBypassStrategy
{
    /// <summary>
    /// Strategy name for logging/identification
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Priority (higher = try first)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this strategy applies to TCP packets
    /// </summary>
    bool SupportsTcp { get; }

    /// <summary>
    /// Whether this strategy applies to UDP packets
    /// </summary>
    bool SupportsUdp { get; }

    /// <summary>
    /// Process a packet and return modified packet(s)
    /// </summary>
    /// <param name="packet">Original packet data</param>
    /// <param name="context">Packet context information</param>
    /// <returns>List of packets to send (can be multiple for fragmentation)</returns>
    IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context);
}

/// <summary>
/// Context information about a packet
/// </summary>
public readonly struct PacketContext
{
    public required bool IsOutbound { get; init; }
    public required bool IsTcp { get; init; }
    public required bool IsUdp { get; init; }
    public required bool IsIPv6 { get; init; }
    public required ushort SrcPort { get; init; }
    public required ushort DstPort { get; init; }
    public required bool IsSyn { get; init; }
    public required bool IsRst { get; init; }
    public required bool IsClientHello { get; init; }
    public required bool HasHttpHost { get; init; }
}

/// <summary>
/// Result of packet processing
/// </summary>
public readonly struct PacketResult
{
    public required byte[] Data { get; init; }
    public required bool RecalcChecksum { get; init; }
    public int DelayMs { get; init; }

    public static PacketResult FromOriginal(ReadOnlySpan<byte> packet) => new()
    {
        Data = packet.ToArray(),
        RecalcChecksum = false,
        DelayMs = 0
    };

    public static PacketResult Modified(byte[] data, bool recalcChecksum = true, int delayMs = 0) => new()
    {
        Data = data,
        RecalcChecksum = recalcChecksum,
        DelayMs = delayMs
    };
}
