using System.Diagnostics;

namespace ZeroDivert.Console;

/// <summary>
/// Registers/unregisters a Task Scheduler task that launches ZeroDivert in tray
/// mode ("--tray") at user logon with elevated ("highest") privileges. A scheduled
/// task with RunLevel=Highest elevates without a UAC prompt on every login (unlike
/// a plain "Run" registry key pointed at an admin-manifested exe), which is the
/// whole point of "install as auto-start" here. This deliberately does not use a
/// real Windows Service — services run in Session 0 and cannot show a tray icon.
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "ZeroDivert";

    public static bool IsInstalled()
    {
        var (code, _, _) = RunSchtasks("/query", "/tn", TaskName);
        return code == 0;
    }

    public static (bool Success, string Message) Install()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return (false, "Uygulama yolu tespit edilemedi.");
        }

        var (code, _, error) = RunSchtasks(
            "/create", "/tn", TaskName,
            "/tr", $"\"{exePath}\" --tray",
            "/sc", "onlogon",
            "/rl", "highest",
            "/f");

        return code == 0
            ? (true, "Başlangıçta otomatik başlatma kuruldu.")
            : (false, string.IsNullOrWhiteSpace(error) ? $"schtasks çıkış kodu {code}" : error);
    }

    public static (bool Success, string Message) Uninstall()
    {
        var (code, _, error) = RunSchtasks("/delete", "/tn", TaskName, "/f");
        return code == 0
            ? (true, "Başlangıçta otomatik başlatma kaldırıldı.")
            : (false, string.IsNullOrWhiteSpace(error) ? $"schtasks çıkış kodu {code}" : error);
    }

    private static (int ExitCode, string Output, string Error) RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, "", "schtasks başlatılamadı");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        return (process.ExitCode, stdout, stderr);
    }
}
