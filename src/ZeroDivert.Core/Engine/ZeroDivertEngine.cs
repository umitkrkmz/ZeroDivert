using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Detection;
using ZeroDivert.Core.Filters;

namespace ZeroDivert.Core.Engine;

/// <summary>
/// Main engine that coordinates packet capture and DPI bypass
/// </summary>
public class ZeroDivertEngine : IDisposable
{
    private readonly DpiBypassEngine _bypassEngine;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    private bool _running;
    private bool _disposed;
    private Task? _captureTask;

    // Statistics
    private long _packetsProcessed;
    private long _packetsModified;
    private long _bytesProcessed;
    private long _discordPackets;

    public event Action<string>? OnLog;
    public event Action<PacketStats>? OnStats;

    public bool IsRunning => _running;

    public ZeroDivertEngine(DpiBypassEngine? bypassEngine = null)
    {
        _bypassEngine = bypassEngine ?? DpiBypassEngine.CreateDefault();
        _bypassEngine.OnLog += msg => OnLog?.Invoke(msg);
    }

    public void Start(Func<string, nint> openHandle, Func<nint, byte[], Action<byte[], bool>, bool> captureLoop)
    {
        lock (_lock)
        {
            if (_running) throw new InvalidOperationException("Engine is already running");
            _running = true;
        }

        var filter = DiscordFilter.GenerateFilter();
        OnLog?.Invoke($"Starting with filter: {filter}");

        _captureTask = Task.Run(() => RunCaptureLoop(openHandle, captureLoop, filter));
    }

    private void RunCaptureLoop(
        Func<string, nint> openHandle,
        Func<nint, byte[], Action<byte[], bool>, bool> captureLoop,
        string filter)
    {
        nint handle = nint.Zero;

        try
        {
            handle = openHandle(filter);

            if (handle == nint.Zero || handle == -1)
            {
                OnLog?.Invoke("Failed to open WinDivert handle");
                return;
            }

            OnLog?.Invoke("WinDivert handle opened successfully");

            var buffer = new byte[65535];

            while (!_cts.Token.IsCancellationRequested)
            {
                var success = captureLoop(handle, buffer, ProcessPacket);

                if (!success && !_cts.Token.IsCancellationRequested)
                {
                    OnLog?.Invoke("Capture loop returned false");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Capture error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
            }
            OnLog?.Invoke("Capture loop ended");
        }
    }

    private void ProcessPacket(byte[] packet, bool isOutbound)
    {
        Interlocked.Increment(ref _packetsProcessed);
        Interlocked.Add(ref _bytesProcessed, packet.Length);

        var context = PacketInspector.Analyze(packet, isOutbound);

        // Check if this is Discord traffic
        if (context.IsClientHello)
        {
            var ipHeaderLen = context.IsIPv6 ? 40 : (packet[0] & 0x0F) * 4;
            var tcpHeaderLen = ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
            var payload = packet.AsSpan(ipHeaderLen + tcpHeaderLen);

            var sni = PacketInspector.ExtractSni(payload);

            if (DiscordFilter.IsDiscordSni(sni))
            {
                Interlocked.Increment(ref _discordPackets);
                OnLog?.Invoke($"Discord ClientHello detected: {sni}");

                var results = _bypassEngine.ProcessPacket(packet, context);
                if (results.Count > 1 || results[0].RecalcChecksum)
                {
                    Interlocked.Increment(ref _packetsModified);
                }

                // Results would be sent via callback in real implementation
            }
        }
        else if (context.IsUdp && DiscordFilter.IsVoicePort(context.DstPort))
        {
            Interlocked.Increment(ref _discordPackets);
            // Process UDP (voice) traffic
            var results = _bypassEngine.ProcessPacket(packet, context);
            if (results.Count > 1)
            {
                Interlocked.Increment(ref _packetsModified);
            }
        }

        // Report stats periodically
        if (_packetsProcessed % 1000 == 0)
        {
            OnStats?.Invoke(GetStats());
        }
    }

    public PacketStats GetStats() => new()
    {
        PacketsProcessed = Interlocked.Read(ref _packetsProcessed),
        PacketsModified = Interlocked.Read(ref _packetsModified),
        BytesProcessed = Interlocked.Read(ref _bytesProcessed),
        DiscordPackets = Interlocked.Read(ref _discordPackets)
    };

    public void Stop()
    {
        _cts.Cancel();
        _captureTask?.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts.Dispose();
    }
}

public readonly struct PacketStats
{
    public required long PacketsProcessed { get; init; }
    public required long PacketsModified { get; init; }
    public required long BytesProcessed { get; init; }
    public required long DiscordPackets { get; init; }
}
