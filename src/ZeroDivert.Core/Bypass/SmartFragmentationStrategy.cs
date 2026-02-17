using System.Buffers.Binary;

namespace ZeroDivert.Core.Bypass;

/// <summary>
/// Smart TCP fragmentation - only fragments first N bytes where SNI is located
/// This dramatically reduces packet count while still bypassing DPI
/// </summary>
public class SmartFragmentationStrategy : IDpiBypassStrategy
{
    public string Name => "Smart Fragmentation";
    public int Priority => 100;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly int _fragmentSize;
    private readonly int _maxFragmentBytes;

    /// <summary>
    /// Create smart fragmentation strategy
    /// </summary>
    /// <param name="fragmentSize">Size of each fragment (default: 2 bytes)</param>
    /// <param name="maxFragmentBytes">Only fragment first N bytes of payload (default: 50)</param>
    public SmartFragmentationStrategy(int fragmentSize = 2, int maxFragmentBytes = 50)
    {
        _fragmentSize = fragmentSize;
        _maxFragmentBytes = maxFragmentBytes;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound)
            return [PacketResult.FromOriginal(packet)];

        // Only fragment ClientHello or HTTP requests
        if (!context.IsClientHello && !context.HasHttpHost)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = GetIpHeaderLength(packet, context.IsIPv6);
        var tcpHeaderLen = GetTcpHeaderLength(packet, ipHeaderLen);
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        if (payloadLen <= _fragmentSize)
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>();
        var payload = packet[headerLen..];
        var header = packet[..headerLen];
        var seqNum = GetTcpSeqNum(packet, ipHeaderLen);

        // Calculate how much to fragment
        var bytesToFragment = Math.Min(_maxFragmentBytes, payloadLen);
        var remainingBytes = payloadLen - bytesToFragment;

        // Fragment the first N bytes
        var offset = 0;
        while (offset < bytesToFragment)
        {
            var chunkSize = Math.Min(_fragmentSize, bytesToFragment - offset);
            var chunk = payload.Slice(offset, chunkSize);

            var newPacket = new byte[headerLen + chunkSize];
            header.CopyTo(newPacket);
            chunk.CopyTo(newPacket.AsSpan(headerLen));

            SetIpTotalLength(newPacket, context.IsIPv6, (ushort)newPacket.Length);
            SetTcpSeqNum(newPacket, ipHeaderLen, seqNum + (uint)offset);

            // Clear PSH flag for intermediate fragments
            ClearTcpPshFlag(newPacket, ipHeaderLen);

            results.Add(PacketResult.Modified(newPacket, recalcChecksum: true));
            offset += chunkSize;
        }

        // Send remaining payload as single packet
        if (remainingBytes > 0)
        {
            var remainingPacket = new byte[headerLen + remainingBytes];
            header.CopyTo(remainingPacket);
            payload.Slice(bytesToFragment, remainingBytes).CopyTo(remainingPacket.AsSpan(headerLen));

            SetIpTotalLength(remainingPacket, context.IsIPv6, (ushort)remainingPacket.Length);
            SetTcpSeqNum(remainingPacket, ipHeaderLen, seqNum + (uint)bytesToFragment);

            // Set PSH flag on last packet
            SetTcpPshFlag(remainingPacket, ipHeaderLen);

            results.Add(PacketResult.Modified(remainingPacket, recalcChecksum: true));
        }

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
        packet[ipHeaderLen + 13] &= 0xF7; // Clear PSH (0x08)

    private static void SetTcpPshFlag(Span<byte> packet, int ipHeaderLen) =>
        packet[ipHeaderLen + 13] |= 0x08; // Set PSH
}
