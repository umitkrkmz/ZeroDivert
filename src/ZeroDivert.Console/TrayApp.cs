using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Spectre.Console;

namespace ZeroDivert.Console;

/// <summary>
/// Owns the tray icon and the monitoring session's start/stop lifecycle for every
/// launch mode (interactive menu, plain CLI flags, or headless "--tray" auto-start).
/// A tray icon is always shown; closing the console window ("X") just hides it
/// instead of exiting the process — Ctrl+C (or the tray's "Çıkış") is what actually
/// stops monitoring and ends the app. Whether monitoring was running when the app
/// last exited is persisted via <see cref="AppStateStore"/>, so a headless launch
/// resumes the same state instead of re-probing anything.
/// </summary>
internal static class TrayApp
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool ConsoleCtrlHandler(int ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int CtrlCloseEvent = 2;

    private static NotifyIcon? _notifyIcon;
    private static ToolStripMenuItem? _toggleItem;
    private static ToolStripMenuItem? _consoleItem;
    private static CancellationTokenSource? _runCts;
    private static Task? _runTask;
    private static bool _consoleVisible = true;
    private static SynchronizationContext? _uiContext;
    private static readonly ManualResetEventSlim ExitSignal = new(false);
    private static ConsoleCtrlHandler? _closeHandler; // rooted so the GC can't collect the delegate

    public static int Run(bool startHidden, (bool verbose, bool noLog, bool noStatus)? initialOptions)
    {
        StartTrayThread();
        InstallCloseHandler();

        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            StopAndExit();
        };

        if (startHidden)
        {
            Program.PrintBanner();
            AnsiConsole.MarkupLine("[grey]Tepsi modunda başlatıldı — sağ tık menüsünden Konsolu Göster ile bu pencereyi tekrar açabilirsiniz.[/]");
            ToggleConsole(visible: false);
        }
        else
        {
            AnsiConsole.MarkupLine("[green][[+]][/] Running with Administrator privileges");
        }

        if (initialOptions is { } opts)
        {
            StartMonitoring(opts.verbose, opts.noLog, opts.noStatus);
        }
        else
        {
            var state = AppStateStore.Load();
            if (state.WasRunning)
            {
                StartMonitoring(verbose: false, noLog: false, noStatus: true);
            }
        }

        ExitSignal.Wait();
        return 0;
    }

    private static void StartTrayThread()
    {
        using var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            _uiContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_uiContext);

            var menu = new ContextMenuStrip();
            _toggleItem = new ToolStripMenuItem("Başlat", null, (_, _) => ToggleRunning());
            _consoleItem = new ToolStripMenuItem("Konsolu Gizle", null, (_, _) => ToggleConsole(!_consoleVisible));
            var exitItem = new ToolStripMenuItem("Çıkış", null, (_, _) => StopAndExit());

            menu.Items.Add(_toggleItem);
            menu.Items.Add(_consoleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "ZeroDivert - Durduruldu",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => ToggleConsole(!_consoleVisible);

            ready.Set();
            Application.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        ready.Wait();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // Fall through to the built-in fallback below.
        }

        return SystemIcons.Application;
    }

    private static void InstallCloseHandler()
    {
        _closeHandler = ctrlType =>
        {
            if (ctrlType != CtrlCloseEvent) return false;

            // The user clicked the console window's "X". Hide instead of letting
            // the default action (process termination) happen — monitoring keeps
            // running in the background, controllable from the tray icon.
            ToggleConsole(visible: false);
            return true;
        };
        SetConsoleCtrlHandler(_closeHandler, true);
    }

    private static void ToggleRunning()
    {
        if (_runTask is { IsCompleted: false })
        {
            StopMonitoring(persistState: true);
        }
        else
        {
            StartMonitoring(verbose: false, noLog: false, noStatus: true);
        }
    }

    private static void StartMonitoring(bool verbose, bool noLog, bool noStatus)
    {
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        // Optimistic UI state — corrected by the continuation below if the run
        // fails immediately (e.g. WinDivert couldn't open), so the tray never gets
        // stuck claiming "Durdur" for a session that isn't actually running.
        SetTrayState(running: true);
        AppStateStore.Save(new AppState { WasRunning = true });

        // WinDivert.Recv is a blocking native call, so this needs a dedicated
        // thread rather than a pooled one.
        _runTask = Task.Factory.StartNew(
            () => Program.RunMonitoringSessionAsync(verbose, noLog, noStatus, ct).GetAwaiter().GetResult(),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        _runTask.ContinueWith(t =>
        {
            if (!t.IsFaulted) return;

            var message = t.Exception?.GetBaseException().Message ?? "Bilinmeyen hata";
            _uiContext?.Post(_ =>
            {
                _notifyIcon?.ShowBalloonTip(5000, "ZeroDivert", $"Başlatılamadı: {message}", ToolTipIcon.Error);
                SetTrayState(running: false);
                _runCts?.Dispose();
                _runCts = null;
                _runTask = null;
                AppStateStore.Save(new AppState { WasRunning = false });
            }, null);
        }, TaskScheduler.Default);
    }

    private static void StopMonitoring(bool persistState)
    {
        if (_runCts != null)
        {
            Program.RequestStop(_runCts);
            try
            {
                _runTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best-effort shutdown; the run task already logs its own failures.
            }
        }

        _runCts?.Dispose();
        _runCts = null;
        _runTask = null;

        SetTrayState(running: false);

        if (persistState)
        {
            AppStateStore.Save(new AppState { WasRunning = false });
        }
    }

    private static void SetTrayState(bool running)
    {
        void Apply()
        {
            if (_toggleItem != null) _toggleItem.Text = running ? "Durdur" : "Başlat";
            if (_notifyIcon != null) _notifyIcon.Text = running ? "ZeroDivert - Çalışıyor" : "ZeroDivert - Durduruldu";
        }

        if (_uiContext != null)
        {
            _uiContext.Post(_ => Apply(), null);
        }
        else
        {
            Apply();
        }
    }

    private static void ToggleConsole(bool visible)
    {
        _consoleVisible = visible;
        ShowWindow(GetConsoleWindow(), visible ? SwShow : SwHide);
        if (_consoleItem != null)
        {
            _consoleItem.Text = visible ? "Konsolu Gizle" : "Konsolu Göster";
        }
    }

    private static void StopAndExit()
    {
        StopMonitoring(persistState: true);

        _uiContext?.Post(_ =>
        {
            if (_notifyIcon == null) return;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }, null);

        ExitSignal.Set();
    }
}
