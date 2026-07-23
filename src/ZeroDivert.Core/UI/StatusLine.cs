namespace ZeroDivert.Core.UI;

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
