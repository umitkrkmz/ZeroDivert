namespace ZeroDivert.Core.Bypass;

/// <summary>
/// Fake TTL: before the real ClientHello, send a decoy ClientHello
/// (<see cref="FakeClientHello"/>) with a low TTL - DPI on the path sees it, the
/// server doesn't. The real packet is sent unchanged right after.
///
/// The TTL has to outlive the home router and reach the ISP's DPI box: with the
/// old default of 1 the decoy died at the user's own modem and was never seen by
/// DPI. 5 is what GoodbyeDPI-Turkey uses. Works for IPv6 (hop limit) as well.
/// </summary>
public class FakeTtlStrategy : IDpiBypassStrategy
{
    public const byte DefaultFakeTtl = 5;

    private readonly byte _fakeTtl;

    public FakeTtlStrategy(byte fakeTtl = DefaultFakeTtl)
    {
        _fakeTtl = fakeTtl;
    }

    public string Name => "Fake TTL";
    public int Priority => 90;
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        if (!context.IsTcp || !context.IsOutbound || !context.IsClientHello)
            return [PacketResult.FromOriginal(packet)];

        var ipHeaderLength = context.IsIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        var headerLength = ipHeaderLength + ((packet[ipHeaderLength + 12] >> 4) & 0x0F) * 4;

        return
        [
            PacketResult.Modified(FakeClientHello.BuildDecoy(packet, headerLength, context.IsIPv6, _fakeTtl)),
            // Unmodified, but must still be sent after the decoy - not the "original"
            // marker, so DpiBypassEngine treats the pair as a modification.
            PacketResult.Modified(packet.ToArray(), recalcChecksum: false)
        ];
    }
}
