using System.Buffers.Binary;
using System.Net;
using ZeroDivert.Core.Filters;
using ZeroDivert.Driver;
using static ZeroDivert.Tests.DnsTestPackets;

namespace ZeroDivert.Tests;

/// <summary>
/// Runs the capture filter through WinDivert's own compiler and evaluator. A
/// syntax error would otherwise only show up as "monitoring failed to start" on a
/// real, elevated run.
/// </summary>
public class CaptureFilterTests
{
    private static readonly IPAddress Server = IPAddress.Parse("162.159.128.233");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void Filter_Compiles(bool interceptDns)
    {
        byte* error = null;
        uint errorPos = 0;
        var ok = WinDivertNative.HelperCompileFilter(
            DiscordFilter.GenerateFilter(interceptDns), (int)WinDivertLayer.Network, null, 0, &error, &errorPos);

        Assert.True(ok, ok ? null : $"{new string((sbyte*)error)} at position {errorPos}");
    }

    [Fact]
    public void Matches_OutboundHttps_And_InboundResetsOnly()
    {
        var filter = DiscordFilter.GenerateFilter(interceptDns: false);

        Assert.True(Matches(filter, Tcp(ClientV4, Server, ClientPort, 443, TcpAck), outbound: true));
        Assert.True(Matches(filter, Tcp(Server, ClientV4, 443, ClientPort, TcpRst), outbound: false));

        Assert.False(Matches(filter, Tcp(Server, ClientV4, 443, ClientPort, TcpAck), outbound: false), "inbound data");
        Assert.False(Matches(filter, Tcp(ClientV4, Server, ClientPort, 80, TcpAck), outbound: true), "plain HTTP");
        Assert.False(Matches(filter, Udp(ClientV4, Server, ClientPort, 50005, [1, 2, 3]), outbound: true), "voice/game UDP");
        Assert.False(Matches(filter, QueryV4("discord.com"), outbound: true), "DNS without DoH");
    }

    [Fact]
    public void WithDns_AlsoMatchesDnsQueries_ButNotLoopback()
    {
        var filter = DiscordFilter.GenerateFilter(interceptDns: true);

        Assert.True(Matches(filter, QueryV4("discord.com"), outbound: true));
        Assert.True(Matches(filter, QueryV6("discord.com"), outbound: true));
        Assert.False(Matches(filter, QueryV4("discord.com"), outbound: true, loopback: true));
        Assert.False(Matches(filter, Udp(ClientV4, Server, ClientPort, 50005, [1, 2, 3]), outbound: true));
    }

    private const byte TcpAck = 0x10;
    private const byte TcpRst = 0x04;

    private static unsafe bool Matches(string filter, byte[] packet, bool outbound, bool loopback = false)
    {
        var addr = new WinDivertAddress
        {
            Outbound = outbound,
            Loopback = loopback,
            IPv6 = packet[0] >> 4 == 6
        };

        fixed (byte* p = packet)
        {
            return WinDivertNative.HelperEvalFilter(filter, p, (uint)packet.Length, &addr);
        }
    }

    private static byte[] Tcp(IPAddress src, IPAddress dst, ushort srcPort, ushort dstPort, byte flags)
    {
        var packet = new byte[40];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 40);
        packet[8] = 64;
        packet[9] = 6;
        src.GetAddressBytes().CopyTo(packet, 12);
        dst.GetAddressBytes().CopyTo(packet, 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), dstPort);
        packet[32] = 0x50; // data offset 5
        packet[33] = flags;
        return packet;
    }
}
