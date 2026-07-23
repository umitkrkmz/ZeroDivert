using Spectre.Console;
using Spectre.Console.Rendering;
using Panel = Spectre.Console.Panel;

namespace ZeroDivert.Console;

/// <summary>
/// Live-updating Spectre.Console dashboard: current bypass technique, packet
/// counters, and a rolling window of recent log lines.
/// </summary>
public sealed class SpectreStatusPanel : IDisposable
{
    private const int MaxLogLines = 12;

    private readonly object _lock = new();
    private readonly Queue<string> _logLines = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private Task? _renderTask;

    private string _technique = "Auto";
    private string _status = "";
    private long _packetsTotal;
    private long _packetsModified;
    private long _discordPackets;
    private long _bytesProcessed;
    private bool _disposed;

    public void Start()
    {
        _renderTask = AnsiConsole.Live(BuildRenderable())
            .StartAsync(async ctx =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    ctx.UpdateTarget(BuildRenderable());
                    try
                    {
                        await Task.Delay(400, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                ctx.UpdateTarget(BuildRenderable());
            });
    }

    public void SetStatus(string status)
    {
        lock (_lock) _status = status;
    }

    public void SetTechnique(string technique)
    {
        lock (_lock) _technique = technique;
    }

    public void UpdateStats(long packetsTotal, long packetsModified, long discordPackets, long bytesProcessed)
    {
        lock (_lock)
        {
            _packetsTotal = packetsTotal;
            _packetsModified = packetsModified;
            _discordPackets = discordPackets;
            _bytesProcessed = bytesProcessed;
        }
    }

    public void WriteLine(string message)
    {
        lock (_lock)
        {
            _logLines.Enqueue($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(message)}");
            while (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }
        }
    }

    private IRenderable BuildRenderable()
    {
        lock (_lock)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            var pps = elapsed.TotalSeconds > 0 ? _packetsTotal / elapsed.TotalSeconds : 0;

            var stats = new Table().Border(TableBorder.Rounded).Expand();
            stats.AddColumn(new TableColumn("[grey]Teknik[/]"));
            stats.AddColumn(new TableColumn("[grey]Paket[/]"));
            stats.AddColumn(new TableColumn("[grey]Discord[/]"));
            stats.AddColumn(new TableColumn("[grey]Değiştirilen[/]"));
            stats.AddColumn(new TableColumn("[grey]Veri[/]"));
            stats.AddColumn(new TableColumn("[grey]Hız[/]"));
            stats.AddRow(
                $"[cyan]{Markup.Escape(_technique)}[/]",
                FormatNumber(_packetsTotal),
                FormatNumber(_discordPackets),
                FormatNumber(_packetsModified),
                FormatBytes(_bytesProcessed),
                $"{pps:F0} pkt/s");

            var statusText = string.IsNullOrEmpty(_status) ? "İzleniyor..." : _status;
            var statusPanel = new Panel(new Markup($"[bold]{Markup.Escape(statusText)}[/]"))
                .Header("[grey]Durum[/]")
                .Expand();

            IRenderable logContent = _logLines.Count > 0
                ? new Rows(_logLines.Select(l => (IRenderable)new Markup(l)))
                : new Markup("[grey]henüz olay yok[/]");

            var logPanel = new Panel(logContent)
                .Header("[grey]Son olaylar[/]")
                .Expand();

            return new Rows(stats, statusPanel, logPanel);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try
        {
            _renderTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown of the render loop.
        }

        _cts.Dispose();
    }
}
