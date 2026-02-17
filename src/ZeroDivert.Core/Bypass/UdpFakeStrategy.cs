using System.Buffers.Binary;

namespace ZeroDivert.Core.Bypass;

/// <summary>
/// UDP Fake strategy for QUIC/UDP traffic (Discord voice, etc.)
/// Sends fake UDP packets to confuse DPI
/// </summary>
public class UdpFakeStrategy : IDpiBypassStrategy
{
    public string Name => "UDP Fake";
    public int Priority => 80;
    public bool SupportsTcp => false;
    public bool SupportsUdp => true;

    private readonly int _fakeCount;

    public UdpFakeStrategy(int fakeCount = 1)
    {
        _fakeCount = fakeCount;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsUdp || !context.IsOutbound)
            return [PacketResult.FromOriginal(packet)];

        // Discord uses UDP for voice (ports 50000-65535 typically)
        // Also QUIC on 443
        if (context.DstPort != 443 && context.DstPort < 50000)
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>();

        // Send fake packets first
        for (var i = 0; i < _fakeCount; i++)
        {
            var fake = CreateFakePacket(packet, context.IsIPv6);
            results.Add(PacketResult.Modified(fake, recalcChecksum: true));
        }

        // Send real packet
        results.Add(PacketResult.FromOriginal(packet));

        return results;
    }

    private static byte[] CreateFakePacket(ReadOnlySpan<byte> packet, bool isIPv6)
    {
        var ipHeaderLen = isIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        const int udpHeaderLen = 8;
        var headerLen = ipHeaderLen + udpHeaderLen;

        // Create minimal fake payload
        var fakePayload = new byte[8]; // Random-looking data
        Random.Shared.NextBytes(fakePayload);

        var fakePacket = new byte[headerLen + fakePayload.Length];
        packet[..headerLen].CopyTo(fakePacket);
        fakePayload.CopyTo(fakePacket.AsSpan(headerLen));

        // Update lengths
        if (isIPv6)
        {
            BinaryPrimitives.WriteUInt16BigEndian(fakePacket.AsSpan(4), (ushort)(fakePacket.Length - 40));
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(fakePacket.AsSpan(2), (ushort)fakePacket.Length);
            // Set low TTL so it gets dropped
            fakePacket[8] = 1;
        }

        // Update UDP length
        BinaryPrimitives.WriteUInt16BigEndian(fakePacket.AsSpan(ipHeaderLen + 4), (ushort)(udpHeaderLen + fakePayload.Length));

        return fakePacket;
    }
}
