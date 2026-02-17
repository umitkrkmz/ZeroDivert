using System.Buffers.Binary;
using System.Text;
using ZeroDivert.Core.Bypass;

namespace ZeroDivert.Core.Detection;

/// <summary>
/// Inspects packets to detect TLS ClientHello, HTTP requests, and extract context
/// </summary>
public static class PacketInspector
{
    // TLS record types
    private const byte TlsHandshake = 0x16;
    private const byte TlsClientHello = 0x01;

    // HTTP methods
    private static readonly byte[][] HttpMethods =
    [
        "GET "u8.ToArray(),
        "POST "u8.ToArray(),
        "HEAD "u8.ToArray(),
        "PUT "u8.ToArray(),
        "DELETE "u8.ToArray(),
        "CONNECT "u8.ToArray(),
        "OPTIONS "u8.ToArray()
    ];

    public static PacketContext Analyze(ReadOnlySpan<byte> packet, bool isOutbound)
    {
        if (packet.Length < 20) // Minimum IP header
        {
            return CreateEmptyContext(isOutbound);
        }

        var version = (packet[0] >> 4) & 0x0F;
        var isIPv6 = version == 6;

        int ipHeaderLen;
        byte protocol;
        int payloadStart;

        if (isIPv6)
        {
            if (packet.Length < 40) return CreateEmptyContext(isOutbound);
            ipHeaderLen = 40;
            protocol = packet[6]; // Next Header
            payloadStart = 40;
        }
        else
        {
            ipHeaderLen = (packet[0] & 0x0F) * 4;
            protocol = packet[9];
            payloadStart = ipHeaderLen;
        }

        var isTcp = protocol == 6;
        var isUdp = protocol == 17;

        if (!isTcp && !isUdp)
        {
            return CreateEmptyContext(isOutbound);
        }

        if (packet.Length < payloadStart + 8) // Minimum TCP/UDP header
        {
            return CreateEmptyContext(isOutbound);
        }

        var srcPort = BinaryPrimitives.ReadUInt16BigEndian(packet[payloadStart..]);
        var dstPort = BinaryPrimitives.ReadUInt16BigEndian(packet[(payloadStart + 2)..]);

        var isSyn = false;
        var isClientHello = false;
        var hasHttpHost = false;

        if (isTcp)
        {
            var tcpHeaderLen = ((packet[payloadStart + 12] >> 4) & 0x0F) * 4;
            var tcpFlags = packet[payloadStart + 13];
            isSyn = (tcpFlags & 0x02) != 0 && (tcpFlags & 0x10) == 0; // SYN without ACK

            var dataStart = payloadStart + tcpHeaderLen;
            if (packet.Length > dataStart)
            {
                var payload = packet[dataStart..];
                isClientHello = IsClientHello(payload);
                hasHttpHost = HasHttpHostHeader(payload);
            }
        }

        return new PacketContext
        {
            IsOutbound = isOutbound,
            IsTcp = isTcp,
            IsUdp = isUdp,
            IsIPv6 = isIPv6,
            SrcPort = srcPort,
            DstPort = dstPort,
            IsSyn = isSyn,
            IsClientHello = isClientHello,
            HasHttpHost = hasHttpHost
        };
    }

    private static bool IsClientHello(ReadOnlySpan<byte> payload)
    {
        // TLS record: [type(1)][version(2)][length(2)][handshake_type(1)]
        if (payload.Length < 6) return false;

        return payload[0] == TlsHandshake &&  // Handshake record
               payload[1] == 0x03 &&           // TLS version major (3)
               payload[5] == TlsClientHello;   // ClientHello message
    }

    private static bool HasHttpHostHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16) return false;

        // Check if starts with HTTP method
        var isHttp = false;
        foreach (var method in HttpMethods)
        {
            if (payload.Length >= method.Length && payload[..method.Length].SequenceEqual(method))
            {
                isHttp = true;
                break;
            }
        }

        if (!isHttp) return false;

        // Look for "Host:" header
        var hostPattern = "Host:"u8;
        return payload.IndexOf(hostPattern) >= 0;
    }

    /// <summary>
    /// Extracts SNI (Server Name Indication) from TLS ClientHello
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> tlsPayload)
    {
        if (!IsClientHello(tlsPayload)) return null;

        try
        {
            // Skip TLS record header (5 bytes) + handshake header (4 bytes) + version (2) + random (32)
            var offset = 5 + 4 + 2 + 32;

            if (tlsPayload.Length <= offset) return null;

            // Session ID length
            var sessionIdLen = tlsPayload[offset];
            offset += 1 + sessionIdLen;

            if (tlsPayload.Length <= offset + 2) return null;

            // Cipher suites length
            var cipherSuitesLen = BinaryPrimitives.ReadUInt16BigEndian(tlsPayload[offset..]);
            offset += 2 + cipherSuitesLen;

            if (tlsPayload.Length <= offset + 1) return null;

            // Compression methods length
            var compressionLen = tlsPayload[offset];
            offset += 1 + compressionLen;

            if (tlsPayload.Length <= offset + 2) return null;

            // Extensions length
            var extensionsLen = BinaryPrimitives.ReadUInt16BigEndian(tlsPayload[offset..]);
            offset += 2;

            var extensionsEnd = offset + extensionsLen;

            while (offset + 4 <= extensionsEnd && offset + 4 <= tlsPayload.Length)
            {
                var extType = BinaryPrimitives.ReadUInt16BigEndian(tlsPayload[offset..]);
                var extLen = BinaryPrimitives.ReadUInt16BigEndian(tlsPayload[(offset + 2)..]);
                offset += 4;

                if (extType == 0) // SNI extension
                {
                    if (offset + 5 > tlsPayload.Length) return null;

                    // SNI list length (2) + name type (1) + name length (2)
                    var nameLen = BinaryPrimitives.ReadUInt16BigEndian(tlsPayload[(offset + 3)..]);
                    offset += 5;

                    if (offset + nameLen > tlsPayload.Length) return null;

                    return Encoding.ASCII.GetString(tlsPayload.Slice(offset, nameLen));
                }

                offset += extLen;
            }
        }
        catch
        {
            // Malformed packet
        }

        return null;
    }

    private static PacketContext CreateEmptyContext(bool isOutbound) => new()
    {
        IsOutbound = isOutbound,
        IsTcp = false,
        IsUdp = false,
        IsIPv6 = false,
        SrcPort = 0,
        DstPort = 0,
        IsSyn = false,
        IsClientHello = false,
        HasHttpHost = false
    };
}
