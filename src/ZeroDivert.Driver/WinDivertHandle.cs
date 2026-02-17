using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeroDivert.Core;

namespace ZeroDivert.Driver;

/// <summary>
/// Managed wrapper for WinDivert handle
/// </summary>
public sealed class WinDivertHandle : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public nint Handle => _handle;
    public bool IsValid => _handle != nint.Zero && _handle != -1;

    private WinDivertHandle(nint handle)
    {
        _handle = handle;
    }

    public static WinDivertHandle Open(string filter, WinDivertLayer layer = WinDivertLayer.Network, short priority = 0, WinDivertFlag flags = 0)
    {
        var handle = WinDivertNative.Open(filter, (int)layer, priority, (ulong)flags);

        if (handle == nint.Zero || handle == -1)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"WinDivertOpen failed with error {error}");
        }

        return new WinDivertHandle(handle);
    }

    public unsafe bool Recv(Span<byte> packet, out uint recvLen, ref WinDivertAddress addr)
    {
        ThrowIfDisposed();

        fixed (byte* pPacket = packet)
        fixed (uint* pRecvLen = &recvLen)
        fixed (WinDivertAddress* pAddr = &addr)
        {
            return WinDivertNative.Recv(_handle, pPacket, (uint)packet.Length, pRecvLen, pAddr);
        }
    }

    public unsafe bool RecvEx(Span<byte> packet, out uint recvLen, WinDivertFlag flags, ref WinDivertAddress addr)
    {
        ThrowIfDisposed();

        uint addrLen = (uint)Marshal.SizeOf<WinDivertAddress>();

        fixed (byte* pPacket = packet)
        fixed (uint* pRecvLen = &recvLen)
        fixed (WinDivertAddress* pAddr = &addr)
        {
            return WinDivertNative.RecvEx(_handle, pPacket, (uint)packet.Length, pRecvLen, (ulong)flags, pAddr, &addrLen, nint.Zero);
        }
    }

    public unsafe bool Send(ReadOnlySpan<byte> packet, out uint sendLen, ref WinDivertAddress addr)
    {
        ThrowIfDisposed();

        fixed (byte* pPacket = packet)
        fixed (uint* pSendLen = &sendLen)
        fixed (WinDivertAddress* pAddr = &addr)
        {
            return WinDivertNative.Send(_handle, pPacket, (uint)packet.Length, pSendLen, pAddr);
        }
    }

    public unsafe bool SendEx(ReadOnlySpan<byte> packet, out uint sendLen, WinDivertFlag flags, ref WinDivertAddress addr)
    {
        ThrowIfDisposed();

        uint addrLen = (uint)Marshal.SizeOf<WinDivertAddress>();

        fixed (byte* pPacket = packet)
        fixed (uint* pSendLen = &sendLen)
        fixed (WinDivertAddress* pAddr = &addr)
        {
            return WinDivertNative.SendEx(_handle, pPacket, (uint)packet.Length, pSendLen, (ulong)flags, pAddr, addrLen, nint.Zero);
        }
    }

    public bool Shutdown(WinDivertShutdown how = WinDivertShutdown.Both)
    {
        ThrowIfDisposed();
        return WinDivertNative.Shutdown(_handle, (int)how);
    }

    public bool SetParam(WinDivertParam param, ulong value)
    {
        ThrowIfDisposed();
        return WinDivertNative.SetParam(_handle, (int)param, value);
    }

    public bool GetParam(WinDivertParam param, out ulong value)
    {
        ThrowIfDisposed();
        return WinDivertNative.GetParam(_handle, (int)param, out value);
    }

    public static unsafe void CalcChecksums(Span<byte> packet, ref WinDivertAddress addr, ulong flags = 0)
    {
        fixed (byte* pPacket = packet)
        fixed (WinDivertAddress* pAddr = &addr)
        {
            WinDivertNative.HelperCalcChecksums(pPacket, (uint)packet.Length, pAddr, flags);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (IsValid)
        {
            WinDivertNative.Close(_handle);
            _handle = nint.Zero;
        }

        _disposed = true;
    }
}

public enum WinDivertLayer
{
    Network = 0,
    NetworkForward = 1,
    Flow = 2,
    Socket = 3,
    Reflect = 4
}

[Flags]
public enum WinDivertFlag : ulong
{
    None = 0,
    Sniff = 0x0001,
    Drop = 0x0002,
    RecvOnly = 0x0004,
    SendOnly = 0x0008,
    NoInstall = 0x0010,
    Fragments = 0x0020
}

public enum WinDivertShutdown
{
    Recv = 1,
    Send = 2,
    Both = 3
}

public enum WinDivertParam
{
    QueueLength = 0,
    QueueTime = 1,
    QueueSize = 2,
    VersionMajor = 3,
    VersionMinor = 4
}
