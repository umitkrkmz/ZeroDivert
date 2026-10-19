using System.Text.Json;

namespace ZeroDivert.Console;

internal sealed record UpdateInfo(string LatestTag, string ReleaseUrl);

/// <summary>
/// Looks up the latest published GitHub release once per run, in the background,
/// and only reports it — downloading and installing stays with the user. The only
/// request made is an anonymous GET to the GitHub API; nothing about the user or
/// their machine is sent beyond what any HTTP request carries. Can be turned off
/// in Ayarlar (<see cref="AppState.CheckForUpdates"/>).
/// </summary>
internal static class UpdateChecker
{
    public enum CheckStatus { Disabled, Checking, UpToDate, UpdateAvailable, Failed }

    private sealed record Result(bool Succeeded, UpdateInfo? Update);

    private static Task<Result>? _check;

    public static void StartIfEnabled()
    {
        if (_check == null && AppStateStore.Load().CheckForUpdates)
        {
            _check = CheckAsync();
        }
    }

    public static CheckStatus Status => _check switch
    {
        null => CheckStatus.Disabled,
        { IsCompleted: false } => CheckStatus.Checking,
        { Result.Succeeded: false } => CheckStatus.Failed,
        { Result.Update: not null } => CheckStatus.UpdateAvailable,
        _ => CheckStatus.UpToDate
    };

    public static UpdateInfo? Available => Status == CheckStatus.UpdateAvailable ? _check!.Result.Update : null;

    /// <summary>
    /// Runs <paramref name="onFound"/> (on a pool thread) once a newer release is
    /// known — immediately if the check already finished.
    /// </summary>
    public static void OnUpdateFound(Action<UpdateInfo> onFound)
    {
        _check?.ContinueWith(t =>
        {
            if (t.Result.Update is { } update) onFound(update);
        }, TaskScheduler.Default);
    }

    private static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ZeroDivert/{AppInfo.Version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            // /releases/latest already skips drafts and pre-releases.
            using var doc = JsonDocument.Parse(await http.GetStringAsync(AppInfo.LatestReleaseApiUrl));
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var url = doc.RootElement.GetProperty("html_url").GetString();

            if (tag == null || url == null || !TryParseTag(tag, out var latest))
            {
                return new Result(false, null);
            }

            return new Result(true, latest > AppInfo.Version ? new UpdateInfo(tag, url) : null);
        }
        catch
        {
            // Offline, rate-limited or GitHub unreachable: not worth bothering the user.
            return new Result(false, null);
        }
    }

    /// <summary>"v1.2.0" / "v1.2.0-rc.1" -> 1.2.0</summary>
    private static bool TryParseTag(string tag, out Version version)
    {
        var core = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (Version.TryParse(core, out var parsed))
        {
            version = AppInfo.Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
