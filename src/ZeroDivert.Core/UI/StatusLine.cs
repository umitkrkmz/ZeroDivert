using System.Text;

namespace ZeroDivert.Core.UI;

/// <summary>
/// Single-line status display that refreshes in place
/// </summary>
public class StatusLine : IDisposable
{
    private readonly object _lock = new();
    private readonly Timer _refreshTimer;
    private readonly StringBuilder _buffer = new();

    private string _status = "";
    private string _technique = "Auto";
    private long _packetsTotal;
    private long _packetsModified;
    private long _discordPackets;
    private long _bytesProcessed;
    private DateTime _startTime;
    private bool _disposed;
    private int _lastLineLength;

    public bool Enabled { get; set; } = true;

    public StatusLine()
    {
        _startTime = DateTime.UtcNow;
        _refreshTimer = new Timer(Refresh, null, 1000, 1000);
    }

    public void SetStatus(string status)
    {
        lock (_lock)
        {
            _status = status;
        }
    }

    public void SetTechnique(string technique)
    {
        lock (_lock)
        {
            _technique = technique;
        }
    }

    public void UpdateStats(long packetsTotal, long packetsModified, long discordPackets, long bytesProcessed)
    {
        Interlocked.Exchange(ref _packetsTotal, packetsTotal);
        Interlocked.Exchange(ref _packetsModified, packetsModified);
        Interlocked.Exchange(ref _discordPackets, discordPackets);
        Interlocked.Exchange(ref _bytesProcessed, bytesProcessed);
    }

    public void IncrementPackets() => Interlocked.Increment(ref _packetsTotal);
    public void IncrementModified() => Interlocked.Increment(ref _packetsModified);
    public void IncrementDiscord() => Interlocked.Increment(ref _discordPackets);
    public void AddBytes(long bytes) => Interlocked.Add(ref _bytesProcessed, bytes);

    private void Refresh(object? state)
    {
        if (!Enabled || _disposed) return;

        lock (_lock)
        {
            try
            {
                var elapsed = DateTime.UtcNow - _startTime;
                var pps = elapsed.TotalSeconds > 0 ? _packetsTotal / elapsed.TotalSeconds : 0;

                _buffer.Clear();
                _buffer.Append("\r"); // Return to start of line

                // Build status line
                _buffer.Append($"[{_technique}] ");
                _buffer.Append($"Pkts: {FormatNumber(_packetsTotal)} ");
                _buffer.Append($"| Discord: {FormatNumber(_discordPackets)} ");
                _buffer.Append($"| Modified: {FormatNumber(_packetsModified)} ");
                _buffer.Append($"| {FormatBytes(_bytesProcessed)} ");
                _buffer.Append($"| {pps:F0} pkt/s ");

                if (!string.IsNullOrEmpty(_status))
                {
                    _buffer.Append($"| {_status}");
                }

                // Pad with spaces to clear previous content
                var currentLength = _buffer.Length;
                if (currentLength < _lastLineLength)
                {
                    _buffer.Append(' ', _lastLineLength - currentLength);
                }
                _lastLineLength = currentLength;

                Console.Write(_buffer.ToString());
            }
            catch
            {
                // Console might be redirected
            }
        }
    }

    private static string FormatNumber(long number)
    {
        return number switch
        {
            >= 1_000_000 => $"{number / 1_000_000.0:F1}M",
            >= 1_000 => $"{number / 1_000.0:F1}K",
            _ => number.ToString()
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    public void WriteLine(string message)
    {
        lock (_lock)
        {
            // Clear current status line, print message, status will refresh
            Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
            Console.WriteLine(message);
            // Reset so next Refresh() starts clean on the new current line
            _lastLineLength = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Dispose();

        // Final newline
        Console.WriteLine();
    }
}

/// <summary>
/// Log entry for file logging
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";
    public string? Category { get; init; }
    public Dictionary<string, object>? Data { get; init; }
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
