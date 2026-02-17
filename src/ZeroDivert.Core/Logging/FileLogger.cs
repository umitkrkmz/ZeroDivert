using System.Text.Json;
using System.Threading.Channels;
using ZeroDivert.Core.UI;

namespace ZeroDivert.Core.Logging;

/// <summary>
/// Async file logger with buffered writes
/// </summary>
public class FileLogger : IAsyncDisposable
{
    private readonly string _logDirectory;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();

    private StreamWriter? _currentWriter;
    private string? _currentLogPath;
    private DateTime _currentDate;

    public LogLevel MinLevel { get; set; } = LogLevel.Info;

    public FileLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZeroDivert",
            "logs"
        );

        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void Log(LogLevel level, string message, string? category = null, Dictionary<string, object>? data = null)
    {
        if (level < MinLevel) return;

        _channel.Writer.TryWrite(new LogEntry
        {
            Level = level,
            Message = message,
            Category = category,
            Data = data
        });
    }

    public void Debug(string message, string? category = null) => Log(LogLevel.Debug, message, category);
    public void Info(string message, string? category = null) => Log(LogLevel.Info, message, category);
    public void Warning(string message, string? category = null) => Log(LogLevel.Warning, message, category);
    public void Error(string message, string? category = null) => Log(LogLevel.Error, message, category);

    public void LogPacket(string sni, string technique, int fragmentCount, bool success)
    {
        Log(LogLevel.Debug, "Packet processed", "Packet", new Dictionary<string, object>
        {
            ["sni"] = sni,
            ["technique"] = technique,
            ["fragments"] = fragmentCount,
            ["success"] = success
        });
    }

    public void LogStats(long total, long discord, long modified, long bytes)
    {
        Log(LogLevel.Info, "Stats snapshot", "Stats", new Dictionary<string, object>
        {
            ["total_packets"] = total,
            ["discord_packets"] = discord,
            ["modified_packets"] = modified,
            ["bytes_processed"] = bytes
        });
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                await EnsureWriterAsync();
                await WriteEntryAsync(entry);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore write errors
            }
        }
    }

    private async Task EnsureWriterAsync()
    {
        var today = DateTime.UtcNow.Date;

        if (_currentWriter != null && _currentDate == today)
            return;

        if (_currentWriter != null)
        {
            await _currentWriter.DisposeAsync();
        }

        _currentDate = today;
        _currentLogPath = Path.Combine(_logDirectory, $"zerodivert_{today:yyyy-MM-dd}.log");
        _currentWriter = new StreamWriter(_currentLogPath, append: true)
        {
            AutoFlush = false
        };
    }

    private async Task WriteEntryAsync(LogEntry entry)
    {
        if (_currentWriter == null) return;

        var line = FormatEntry(entry);
        await _currentWriter.WriteLineAsync(line);

        // Flush periodically or on error
        if (entry.Level >= LogLevel.Warning)
        {
            await _currentWriter.FlushAsync();
        }
    }

    private static string FormatEntry(LogEntry entry)
    {
        var level = entry.Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???"
        };

        var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var category = entry.Category != null ? $"[{entry.Category}] " : "";

        var dataStr = "";
        if (entry.Data != null && entry.Data.Count > 0)
        {
            dataStr = " " + JsonSerializer.Serialize(entry.Data);
        }

        return $"{timestamp} [{level}] {category}{entry.Message}{dataStr}";
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { }

        if (_currentWriter != null)
        {
            await _currentWriter.FlushAsync();
            await _currentWriter.DisposeAsync();
        }

        _cts.Dispose();
    }

    public string GetLogDirectory() => _logDirectory;
}
