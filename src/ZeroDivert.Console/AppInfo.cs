using System.Diagnostics;
using System.Reflection;

namespace ZeroDivert.Console;

/// <summary>
/// App identity shared by the banner, the About screen, the update check, the
/// single-instance lock and the Inno Setup installer (which must use the same
/// mutex name, see installer/ZeroDivert.iss).
/// </summary>
internal static class AppInfo
{
    public const string RepoUrl = "https://github.com/umitkrkmz/ZeroDivert";
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/umitkrkmz/ZeroDivert/releases/latest";
    public const string License = "GPL-3.0-or-later";

    // Global\ so a second copy is caught even from another session; the app always
    // runs elevated, which is what creating a Global\ object requires.
    public const string SingleInstanceMutexName = @"Global\ZeroDivert.SingleInstance";
    public const string ShowConsoleEventName = @"Global\ZeroDivert.ShowConsole";

    /// <summary>
    /// Major.Minor.Patch from the csproj &lt;Version&gt;, so it never drifts from the build.
    /// </summary>
    public static Version Version { get; } = Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string VersionText => $"v{Version}";

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser or shell association; callers also print the URL.
        }
    }
}
