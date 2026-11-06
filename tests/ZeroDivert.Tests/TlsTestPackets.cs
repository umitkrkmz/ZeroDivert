using System.Buffers.Binary;
using System.Net;
using System.Text;
using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Detection;

namespace ZeroDivert.Tests;

/// <summary>Outbound TCP segments carrying a TLS ClientHello, as the capture loop sees them.</summary>
internal static class TlsTestPackets
{
    public static readonly IPAddress ClientV4 = IPAddress.Parse("192.168.1.10");
    public static readonly IPAddress ServerV4 = IPAddress.Parse("162.159.128.233");
    public static readonly IPAddress ClientV6 = IPAddress.Parse("2a01:358::10");
    public static readonly IPAddress ServerV6 = IPAddress.Parse("2606:4700::6810:84e5");

    public const ushort ClientPort = 50123;
    public const uint Seq = 0x10000000;
    public const uint Ack = 0x20000000;
    public const byte ClientTtl = 128;

    /// <summary>A TLS record holding a ClientHello for <paramref name="sni"/>, padded to <paramref name="paddingLength"/>.</summary>
    public static byte[] ClientHello(string sni = "gateway.discord.gg", int paddingLength = 0)
    {
        var name = Encoding.ASCII.GetBytes(sni);
        var extensions = new List<byte>();
        // server_name
        extensions.AddRange([0x00, 0x00]);
        AddU16(extensions, name.Length + 5);
        AddU16(extensions, name.Length + 3);
        extensions.Add(0x00);
        AddU16(extensions, name.Length);
        extensions.AddRange(name);
        if (paddingLength > 0)
        {
            extensions.AddRange([0x00, 0x15]); // padding
            AddU16(extensions, paddingLength);
            extensions.AddRange(new byte[paddingLength]);
        }

        var body = new List<byte> { 0x03, 0x03 };
        body.AddRange(new byte[32]);             // random
        body.Add(0);                             // session id
        body.AddRange([0x00, 0x02, 0x13, 0x01]); // cipher suites
        body.AddRange([0x01, 0x00]);             // compression
        AddU16(body, extensions.Count);
        body.AddRange(extensions);

        var handshake = new List<byte> { 0x01, 0, (byte)(body.Count >> 8), (byte)body.Count };
        handshake.AddRange(body);

        var record = new List<byte> { 0x16, 0x03, 0x01 };
        AddU16(record, handshake.Count);
        record.AddRange(handshake);
        return [.. record];
    }

    public static byte[] TcpSegment(byte[] payload, bool ipv6 = false, byte flags = 0x18 /* PSH|ACK */)
    {
        var ipLength = ipv6 ? 40 : 20;
        var packet = new byte[ipLength + 20 + payload.Length];

        if (ipv6)
        {
            packet[0] = 0x60;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)(20 + payload.Length));
            packet[6] = 6;
            packet[7] = ClientTtl;
            ClientV6.GetAddressBytes().CopyTo(packet, 8);
            ServerV6.GetAddressBytes().CopyTo(packet, 24);
        }
        else
        {
            packet[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 0x4242);
            packet[8] = ClientTtl;
            packet[9] = 6;
            ClientV4.GetAddressBytes().CopyTo(packet, 12);
            ServerV4.GetAddressBytes().CopyTo(packet, 16);
        }

        var tcp = packet.AsSpan(ipLength);
        BinaryPrimitives.WriteUInt16BigEndian(tcp, ClientPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], 443);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], Seq);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..], Ack);
        tcp[12] = 0x50;
        tcp[13] = flags;
        payload.CopyTo(tcp[20..]);
        return packet;
    }

    public static PacketContext Context(byte[] packet) => PacketInspector.Analyze(packet, isOutbound: true);

    public static int IpHeaderLength(byte[] packet) => packet[0] >> 4 == 6 ? 40 : 20;
    public static byte Ttl(byte[] packet) => packet[0] >> 4 == 6 ? packet[7] : packet[8];
    public static uint SeqOf(byte[] packet) => BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(IpHeaderLength(packet) + 4));
    public static uint AckOf(byte[] packet) => BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(IpHeaderLength(packet) + 8));
    public static byte[] PayloadOf(byte[] packet) => packet[(IpHeaderLength(packet) + 20)..];

    public static int IpLengthField(byte[] packet) => packet[0] >> 4 == 6
        ? BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)) + 40
        : BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2));

    private static void AddU16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }
}
