using System.Runtime.InteropServices;

namespace ZeroDivert.Driver;

/// <summary>
/// WinDivert 2.2 Address structure
/// Total size: 80 bytes
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct WinDivertAddress
{
    [FieldOffset(0)]
    public long Timestamp;          // 8 bytes

    [FieldOffset(8)]
    private uint _layerEventFlags;  // Layer(8) + Event(8) + Flags(16)

    [FieldOffset(12)]
    private uint _reserved2;

    // Network layer data (union at offset 16)
    [FieldOffset(16)]
    public uint IfIdx;

    [FieldOffset(20)]
    public uint SubIfIdx;

    // Layer: bits 0-7
    public byte Layer
    {
        get => (byte)(_layerEventFlags & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & 0xFFFFFF00) | value;
    }

    // Event: bits 8-15
    public byte Event
    {
        get => (byte)((_layerEventFlags >> 8) & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & 0xFFFF00FF) | ((uint)value << 8);
    }

    // Sniffed: bit 16
    public bool Sniffed
    {
        get => (_layerEventFlags & 0x10000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x10000) : (_layerEventFlags & ~0x10000u);
    }

    // Outbound: bit 17
    public bool Outbound
    {
        get => (_layerEventFlags & 0x20000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x20000) : (_layerEventFlags & ~0x20000u);
    }

    // Loopback: bit 18
    public bool Loopback
    {
        get => (_layerEventFlags & 0x40000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x40000) : (_layerEventFlags & ~0x40000u);
    }

    // Impostor: bit 19
    public bool Impostor
    {
        get => (_layerEventFlags & 0x80000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x80000) : (_layerEventFlags & ~0x80000u);
    }

    // IPv6: bit 20
    public bool IPv6
    {
        get => (_layerEventFlags & 0x100000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x100000) : (_layerEventFlags & ~0x100000u);
    }

    // IPChecksum: bit 21
    public bool IPChecksum
    {
        get => (_layerEventFlags & 0x200000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x200000) : (_layerEventFlags & ~0x200000u);
    }

    // TCPChecksum: bit 22
    public bool TCPChecksum
    {
        get => (_layerEventFlags & 0x400000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x400000) : (_layerEventFlags & ~0x400000u);
    }

    // UDPChecksum: bit 23
    public bool UDPChecksum
    {
        get => (_layerEventFlags & 0x800000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x800000) : (_layerEventFlags & ~0x800000u);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct IPv4Header
{
    private byte _versionAndHeaderLength;
    public byte TOS;
    public ushort Length;
    public ushort Id;
    public ushort FragOff0;
    public byte TTL;
    public byte Protocol;
    public ushort Checksum;
    public fixed byte SrcAddr[4];
    public fixed byte DstAddr[4];

    public byte Version => (byte)((_versionAndHeaderLength >> 4) & 0x0F);
    public byte HeaderLength => (byte)((_versionAndHeaderLength & 0x0F) * 4);

    public void SetVersion(byte version) =>
        _versionAndHeaderLength = (byte)((_versionAndHeaderLength & 0x0F) | ((version & 0x0F) << 4));

    public void SetHeaderLength(byte length) =>
        _versionAndHeaderLength = (byte)((_versionAndHeaderLength & 0xF0) | ((length / 4) & 0x0F));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct IPv6Header
{
    private uint _versionTrafficClassFlowLabel;
    public ushort Length;
    public byte NextHdr;
    public byte HopLimit;
    public fixed byte SrcAddr[16];
    public fixed byte DstAddr[16];

    public byte Version => (byte)((_versionTrafficClassFlowLabel >> 28) & 0x0F);
    public byte TrafficClass => (byte)((_versionTrafficClassFlowLabel >> 20) & 0xFF);
    public uint FlowLabel => _versionTrafficClassFlowLabel & 0x000FFFFF;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TcpHeader
{
    public ushort SrcPort;
    public ushort DstPort;
    public uint SeqNum;
    public uint AckNum;
    private ushort _dataOffsetAndFlags;
    public ushort Window;
    public ushort Checksum;
    public ushort UrgPtr;

    public byte DataOffset => (byte)(((_dataOffsetAndFlags >> 8) >> 4) * 4);

    public bool FIN => ((_dataOffsetAndFlags >> 8) & 0x01) != 0;
    public bool SYN => ((_dataOffsetAndFlags >> 8) & 0x02) != 0;
    public bool RST => ((_dataOffsetAndFlags >> 8) & 0x04) != 0;
    public bool PSH => ((_dataOffsetAndFlags >> 8) & 0x08) != 0;
    public bool ACK => ((_dataOffsetAndFlags >> 8) & 0x10) != 0;
    public bool URG => ((_dataOffsetAndFlags >> 8) & 0x20) != 0;
    public bool ECE => ((_dataOffsetAndFlags >> 8) & 0x40) != 0;
    public bool CWR => ((_dataOffsetAndFlags >> 8) & 0x80) != 0;

    public void SetFlag(TcpFlags flag, bool value)
    {
        var flags = (ushort)(_dataOffsetAndFlags >> 8);
        if (value)
            flags |= (ushort)flag;
        else
            flags &= (ushort)~flag;
        _dataOffsetAndFlags = (ushort)((_dataOffsetAndFlags & 0x00FF) | (flags << 8));
    }

    public void SetDataOffset(byte offset)
    {
        var highByte = (byte)(_dataOffsetAndFlags >> 8);
        highByte = (byte)((highByte & 0x0F) | ((offset / 4) << 4));
        _dataOffsetAndFlags = (ushort)((_dataOffsetAndFlags & 0x00FF) | (highByte << 8));
    }
}

[Flags]
public enum TcpFlags : ushort
{
    FIN = 0x01,
    SYN = 0x02,
    RST = 0x04,
    PSH = 0x08,
    ACK = 0x10,
    URG = 0x20,
    ECE = 0x40,
    CWR = 0x80
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UdpHeader
{
    public ushort SrcPort;
    public ushort DstPort;
    public ushort Length;
    public ushort Checksum;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IcmpHeader
{
    public byte Type;
    public byte Code;
    public ushort Checksum;
    public uint Body;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IcmpV6Header
{
    public byte Type;
    public byte Code;
    public ushort Checksum;
    public uint Body;
}
