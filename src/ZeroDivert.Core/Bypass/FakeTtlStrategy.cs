using System.Buffers.Binary;

namespace ZeroDivert.Core.Bypass;

/// <summary>
/// Fake TTL strategy - sends fake packets with low TTL that expire before reaching DPI
/// but the DPI might process them and get confused
/// </summary>
public class FakeTtlStrategy : IDpiBypassStrategy
{
    public string Name => "Fake TTL";
    public int Priority => 90;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly byte _fakeTtl;
    private readonly byte _realTtl;

    public FakeTtlStrategy(byte fakeTtl = 1, byte realTtl = 64)
    {
        _fakeTtl = fakeTtl;
        _realTtl = realTtl;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || context.IsIPv6)
            return [PacketResult.FromOriginal(packet)];

        // Only apply to ClientHello
        if (!context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLen = (packet[0] & 0x0F) * 4;
        var tcpHeaderLen = ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
        var headerLen = ipHeaderLen + tcpHeaderLen;
        var payloadLen = packet.Length - headerLen;

        if (payloadLen < 6) // Need at least TLS record header
            return [PacketResult.FromOriginal(packet)];

        var results = new List<PacketResult>();

        // 1. Send fake packet with low TTL and wrong data
        var fakePacket = CreateFakePacket(packet, ipHeaderLen, tcpHeaderLen);
        results.Add(PacketResult.Modified(fakePacket, recalcChecksum: true));

        // 2. Send real packet (unmodified or with adjusted TTL)
        var realPacket = packet.ToArray();
        realPacket[8] = _realTtl; // Set TTL
        results.Add(PacketResult.Modified(realPacket, recalcChecksum: true, delayMs: 1));

        return results;
    }

    private byte[] CreateFakePacket(ReadOnlySpan<byte> packet, int ipHeaderLen, int tcpHeaderLen)
    {
        var headerLen = ipHeaderLen + tcpHeaderLen;

        // Create packet with minimal fake payload
        var fakePayload = new byte[] { 0x16, 0x03, 0x01, 0x00, 0x01, 0x00 }; // Fake TLS
        var fakePacket = new byte[headerLen + fakePayload.Length];

        // Copy headers
        packet[..headerLen].CopyTo(fakePacket);
        fakePayload.CopyTo(fakePacket.AsSpan(headerLen));

        // Set low TTL
        fakePacket[8] = _fakeTtl;

        // Update IP total length
        BinaryPrimitives.WriteUInt16BigEndian(fakePacket.AsSpan(2), (ushort)fakePacket.Length);

        return fakePacket;
    }
}
