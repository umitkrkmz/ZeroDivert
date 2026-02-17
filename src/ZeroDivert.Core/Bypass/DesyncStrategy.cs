using System.Buffers.Binary;

namespace ZeroDivert.Core.Bypass;

/// <summary>
/// TCP Desync strategy - manipulates TCP flags and timing to confuse DPI
/// Techniques: disorder, split, fake
/// </summary>
public class DesyncStrategy : IDpiBypassStrategy
{
    public string Name => "TCP Desync";
    public int Priority => 95;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly DesyncMode _mode;
    private readonly int _splitPosition;

    public DesyncStrategy(DesyncMode mode = DesyncMode.Split, int splitPosition = 3)
    {
        _mode = mode;
        _splitPosition = splitPosition;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound)
            return [PacketResult.FromOriginal(packet)];

        if (!context.IsClientHello && !context.HasHttpHost)
            return [PacketResult.FromOriginal(packet)];

        return _mode switch
        {
            DesyncMode.Split => ProcessSplit(packet, context),
            DesyncMode.Disorder => ProcessDisorder(packet, context),
            DesyncMode.Fake => ProcessFake(packet, context),
            _ => [PacketResult.FromOriginal(packet)]
        };
    }

    private IReadOnlyList<PacketResult> ProcessSplit(ReadOnlySpan<byte> packet, PacketContext context)
    {
        var ipHeaderLen = GetIpHeaderLength(packet, context.IsIPv6);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        if (payloadLen <= _splitPosition)
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>();
        var seqNum = GetTcpSeqNum(packet, ipHeaderLen);

        // First part
        var firstLen = headerLen + _splitPosition;
        var first = new byte[firstLen];
        packet[..firstLen].CopyTo(first);
        SetIpTotalLength(first, context.IsIPv6, (ushort)firstLen);
        ClearTcpPshFlag(first, ipHeaderLen);
        results.Add(PacketResult.Modified(first, recalcChecksum: true));

        // Second part
        var secondPayloadLen = payloadLen - _splitPosition;
        var second = new byte[headerLen + secondPayloadLen];
        packet[..headerLen].CopyTo(second);
        packet[(headerLen + _splitPosition)..].CopyTo(second.AsSpan(headerLen));
        SetIpTotalLength(second, context.IsIPv6, (ushort)second.Length);
        SetTcpSeqNum(second, ipHeaderLen, seqNum + (uint)_splitPosition);
        results.Add(PacketResult.Modified(second, recalcChecksum: true));

        return results;
    }

    private IReadOnlyList<PacketResult> ProcessDisorder(ReadOnlySpan<byte> packet, PacketContext context)
    {
        // Same as split but send second part first
        var split = ProcessSplit(packet, context);
        if (split.Count != 2)
            return split;

        return [split[1], split[0]]; // Reverse order
    }

    private IReadOnlyList<PacketResult> ProcessFake(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (context.IsIPv6)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = GetIpHeaderLength(packet, false);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;

        var results = new List<PacketResult>();

        // 1. Fake packet with bad checksum (will be dropped by server but confuse DPI)
        var fakePacket = packet.ToArray();
        // Corrupt checksum intentionally
        fakePacket[ipHeaderLen + 16] ^= 0xFF;
        fakePacket[ipHeaderLen + 17] ^= 0xFF;
        results.Add(PacketResult.Modified(fakePacket, recalcChecksum: false));

        // 2. Real packet
        results.Add(PacketResult.FromOriginal(packet));

        return results;
    }

    private static int GetIpHeaderLength(ReadOnlySpan<byte> packet, bool isIPv6) =>
        isIPv6 ? 40 : (packet[0] & 0x0F) * 4;

    private static int GetTcpHeaderLength(ReadOnlySpan<byte> packet, int ipHeaderLen) =>
        ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;

    private static uint GetTcpSeqNum(ReadOnlySpan<byte> packet, int ipHeaderLen) =>
        BinaryPrimitives.ReadUInt32BigEndian(packet[(ipHeaderLen + 4)..]);

    private static void SetTcpSeqNum(Span<byte> packet, int ipHeaderLen, uint seqNum) =>
        BinaryPrimitives.WriteUInt32BigEndian(packet[(ipHeaderLen + 4)..], seqNum);

    private static void SetIpTotalLength(Span<byte> packet, bool isIPv6, ushort length)
    {
        if (isIPv6)
            BinaryPrimitives.WriteUInt16BigEndian(packet[4..], (ushort)(length - 40));
        else
            BinaryPrimitives.WriteUInt16BigEndian(packet[2..], length);
    }

    private static void ClearTcpPshFlag(Span<byte> packet, int ipHeaderLen) =>
        packet[ipHeaderLen + 13] &= 0xF7;
}

public enum DesyncMode
{
    Split,
    Disorder,
    Fake
}
