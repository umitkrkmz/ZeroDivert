using System.Buffers.Binary;

namespace ZeroDivert.Core.Bypass;

/// <summary>
/// The technique GoodbyeDPI-Turkey runs on Turkish lines
/// (<c>goodbyedpi.exe -5 --set-ttl 5</c>), applied to Discord ClientHellos only.
/// Verified against GoodbyeDPI's src/goodbyedpi.c rather than its README:
///
/// 1. A decoy ClientHello (<see cref="FakeClientHello"/>, SNI www.w3.org) with the
///    real segment's sequence/ack numbers and a low TTL: DPI sees a harmless
///    handshake on this connection, the server never receives it.
/// 2. The real ClientHello split into exactly two segments after its first
///    <c>fragmentSize</c> bytes (GoodbyeDPI's "native frag", -e 2), sent in
///    reverse order (--reverse-frag): the tail (sequence + fragmentSize) first,
///    then the head. TCP flags are left as they were, like GoodbyeDPI does.
///
/// GoodbyeDPI applies this to every TLS handshake on the machine and its README
/// warns the fake-TTL mode can break sites; here only Discord connections get it.
/// <c>--set-ttl 5</c> replaces <c>--auto-ttl</c> (which would need inbound SYN-ACKs
/// to measure hop count). <c>--max-payload</c> is only a capture-filter speedup
/// there - ClientHellos are always handled regardless of size - so it has no
/// counterpart here.
/// </summary>
public sealed class GoodbyeDpiStrategy : IDpiBypassStrategy
{
    public const byte DefaultFakeTtl = 5;
    public const int DefaultFragmentSize = 2;

    private readonly byte _fakeTtl;
    private readonly int _fragmentSize;

    public GoodbyeDpiStrategy(byte fakeTtl = DefaultFakeTtl, int fragmentSize = DefaultFragmentSize)
    {
        _fakeTtl = fakeTtl;
        _fragmentSize = fragmentSize;
    }

    public string Name => "GoodbyeDPI uyumlu";
    public int Priority => 100;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || !context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLength = context.IsIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        var headerLength = ipHeaderLength + ((packet[ipHeaderLength + 12] >> 4) & 0x0F) * 4;
        var payloadLength = packet.Length - headerLength;
        if (payloadLength <= _fragmentSize)
            return [PacketResult.FromOriginal(packet)];

        var decoy = FakeClientHello.BuildDecoy(packet, headerLength, context.IsIPv6, _fakeTtl);
        var tail = Segment(packet, headerLength, ipHeaderLength, context.IsIPv6, _fragmentSize, payloadLength - _fragmentSize);
        var head = Segment(packet, headerLength, ipHeaderLength, context.IsIPv6, 0, _fragmentSize);

        return
        [
            PacketResult.Modified(decoy),
            PacketResult.Modified(tail),
            PacketResult.Modified(head)
        ];
    }

    /// <summary>The real segment cut down to payload[offset .. offset+length), sequence number advanced to match.</summary>
    private static byte[] Segment(ReadOnlySpan<byte> packet, int headerLength, int ipHeaderLength, bool isIPv6, int offset, int length)
    {
        var segment = new byte[headerLength + length];
        packet[..headerLength].CopyTo(segment);
        packet.Slice(headerLength + offset, length).CopyTo(segment.AsSpan(headerLength));

        if (isIPv6)
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(4), (ushort)(segment.Length - 40));
        else
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)segment.Length);

        var seq = BinaryPrimitives.ReadUInt32BigEndian(packet[(ipHeaderLength + 4)..]);
        BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(ipHeaderLength + 4), unchecked(seq + (uint)offset));
        return segment;
    }
}
