using System.Text.Json;

namespace ZeroDivert.Console;

public sealed class AppState
{
    /// <summary>
    /// Whether monitoring was actively running the last time the app exited.
    /// Used by tray mode to resume automatically after an auto-start login, and
    /// to stay stopped if the user had stopped it deliberately last time.
    /// </summary>
    public bool WasRunning { get; set; }
}

/// <summary>
/// Persists the last known run state to disk so tray mode doesn't have to guess
/// (or run any network/DPI probing) on startup — it just picks up where it left off.
/// </summary>
public static class AppStateStore
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZeroDivert",
        "state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var json = File.ReadAllText(StatePath);
                var state = JsonSerializer.Deserialize<AppState>(json);
                if (state != null)
                {
                    return state;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable state file — fall back to a safe default below.
        }

        return new AppState();
    }

    public static void Save(AppState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Best-effort: losing the last-known state just means the next launch
            // won't auto-resume, which is a safe fallback, not a crash.
        }
    }
}
