using System.Runtime.InteropServices;

namespace ZeroDivert.Driver;

/// <summary>
/// WinDivert native P/Invoke declarations
/// </summary>
public static partial class WinDivertNative
{
    private const string DllName = "WinDivert.dll";

    // Layer constants
    public const int WINDIVERT_LAYER_NETWORK = 0;
    public const int WINDIVERT_LAYER_NETWORK_FORWARD = 1;
    public const int WINDIVERT_LAYER_FLOW = 2;
    public const int WINDIVERT_LAYER_SOCKET = 3;
    public const int WINDIVERT_LAYER_REFLECT = 4;

    // Flag constants
    public const ulong WINDIVERT_FLAG_SNIFF = 0x0001;
    public const ulong WINDIVERT_FLAG_DROP = 0x0002;
    public const ulong WINDIVERT_FLAG_RECV_ONLY = 0x0004;
    public const ulong WINDIVERT_FLAG_READ_ONLY = WINDIVERT_FLAG_RECV_ONLY;
    public const ulong WINDIVERT_FLAG_SEND_ONLY = 0x0008;
    public const ulong WINDIVERT_FLAG_WRITE_ONLY = WINDIVERT_FLAG_SEND_ONLY;
    public const ulong WINDIVERT_FLAG_NO_INSTALL = 0x0010;
    public const ulong WINDIVERT_FLAG_FRAGMENTS = 0x0020;

    // Param constants
    public const int WINDIVERT_PARAM_QUEUE_LENGTH = 0;
    public const int WINDIVERT_PARAM_QUEUE_TIME = 1;
    public const int WINDIVERT_PARAM_QUEUE_SIZE = 2;
    public const int WINDIVERT_PARAM_VERSION_MAJOR = 3;
    public const int WINDIVERT_PARAM_VERSION_MINOR = 4;

    // Shutdown constants
    public const int WINDIVERT_SHUTDOWN_RECV = 0x1;
    public const int WINDIVERT_SHUTDOWN_SEND = 0x2;
    public const int WINDIVERT_SHUTDOWN_BOTH = 0x3;

    [LibraryImport(DllName, EntryPoint = "WinDivertOpen", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint Open(string filter, int layer, short priority, ulong flags);

    [LibraryImport(DllName, EntryPoint = "WinDivertClose")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Close(nint handle);

    [LibraryImport(DllName, EntryPoint = "WinDivertRecv")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool Recv(
        nint handle,
        byte* pPacket,
        uint packetLen,
        uint* pRecvLen,
        WinDivertAddress* pAddr);

    [LibraryImport(DllName, EntryPoint = "WinDivertRecvEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool RecvEx(
        nint handle,
        byte* pPacket,
        uint packetLen,
        uint* pRecvLen,
        ulong flags,
        WinDivertAddress* pAddr,
        uint* pAddrLen,
        nint lpOverlapped);

    [LibraryImport(DllName, EntryPoint = "WinDivertSend")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool Send(
        nint handle,
        byte* pPacket,
        uint packetLen,
        uint* pSendLen,
        WinDivertAddress* pAddr);

    [LibraryImport(DllName, EntryPoint = "WinDivertSendEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SendEx(
        nint handle,
        byte* pPacket,
        uint packetLen,
        uint* pSendLen,
        ulong flags,
        WinDivertAddress* pAddr,
        uint addrLen,
        nint lpOverlapped);

    [LibraryImport(DllName, EntryPoint = "WinDivertShutdown")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shutdown(nint handle, int how);

    [LibraryImport(DllName, EntryPoint = "WinDivertSetParam")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetParam(nint handle, int param, ulong value);

    [LibraryImport(DllName, EntryPoint = "WinDivertGetParam")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetParam(nint handle, int param, out ulong value);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperCalcChecksums")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool HelperCalcChecksums(
        byte* pPacket,
        uint packetLen,
        WinDivertAddress* pAddr,
        ulong flags);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperParsePacket")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool HelperParsePacket(
        byte* pPacket,
        uint packetLen,
        IPv4Header** ppIpHdr,
        IPv6Header** ppIpv6Hdr,
        byte* pProtocol,
        IcmpHeader** ppIcmpHdr,
        IcmpV6Header** ppIcmpv6Hdr,
        TcpHeader** ppTcpHdr,
        UdpHeader** ppUdpHdr,
        byte** ppData,
        uint* pDataLen,
        byte** ppNext,
        uint* pNextLen);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperHashPacket")]
    public static unsafe partial ulong HelperHashPacket(
        byte* pPacket,
        uint packetLen,
        ulong seed);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperNtohIPv4Address")]
    public static partial uint HelperNtohIPv4Address(uint address);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperHtonIPv4Address")]
    public static partial uint HelperHtonIPv4Address(uint address);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperNtohIPv6Address")]
    public static unsafe partial void HelperNtohIPv6Address(uint* inAddr, uint* outAddr);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperHtonIPv6Address")]
    public static unsafe partial void HelperHtonIPv6Address(uint* inAddr, uint* outAddr);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperCompileFilter", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool HelperCompileFilter(
        string filter,
        int layer,
        byte* pObject,
        uint objLen,
        byte** ppErrorStr,
        uint* pErrorPos);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperEvalFilter", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool HelperEvalFilter(
        string filter,
        byte* pPacket,
        uint packetLen,
        WinDivertAddress* pAddr);

    [LibraryImport(DllName, EntryPoint = "WinDivertHelperFormatFilter", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool HelperFormatFilter(
        string filter,
        int layer,
        byte* pBuffer,
        uint bufLen);
}
