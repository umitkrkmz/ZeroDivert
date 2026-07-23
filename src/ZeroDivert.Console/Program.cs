using System.Runtime.InteropServices;
using Spectre.Console;
using Color = Spectre.Console.Color;
using ZeroDivert.Core.Adaptive;
using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;
using ZeroDivert.Core.Detection;
using ZeroDivert.Core.Filters;
using ZeroDivert.Core.Logging;
using ZeroDivert.Driver;

namespace ZeroDivert.Console;

internal static class Program
{
    private static SpectreStatusPanel? _panel;
    private static FileLogger? _logger;
    private static bool _verbose;
    private static long _bytesProcessed;
    private static WinDivertHandle? _handle;

    [STAThread]
    private static int Main(string[] args)
    {
        System.Console.Title = "ZeroDivert - Discord DPI Bypass";
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Nothing works without admin, so bail out before showing any menu.
        if (!IsAdministrator())
        {
            PrintBanner();
            WriteError("This application requires Administrator privileges!");
            WriteError("Please run as Administrator.");
            return 1;
        }

        // Background/tray launch: from the auto-start scheduled task, or manually
        // with --tray. Skips the console menu entirely and resumes whatever state
        // AppStateStore has saved (see TrayApp.Run).
        var startHidden = args.Contains("--tray");

        // With no arguments and an interactive terminal, drive everything (run
        // options, DNS settings) from an on-screen menu instead of expecting CLI
        // flags to be memorized.
        var interactive = !startHidden && args.Length == 0
            && !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;

        (bool verbose, bool noLog, bool noStatus)? initialOptions = null;

        if (interactive)
        {
            var menuResult = RunHomeMenu();
            if (menuResult is null)
            {
                return 0; // "Çıkış" was selected before starting anything
            }

            initialOptions = menuResult.Value;
        }
        else if (!startHidden)
        {
            PrintBanner();
            initialOptions = (
                args.Contains("-v") || args.Contains("--verbose"),
                args.Contains("--no-log"),
                args.Contains("--no-status"));
        }
        // else (startHidden): leave initialOptions null so TrayApp resumes whatever
        // AppStateStore last saved instead of always starting fresh.

        // From here on a tray icon is always present (TrayApp.Run). Closing the
        // console window ("X") just hides it — monitoring keeps running in the
        // background. Ctrl+C (or the tray's "Çıkış") is what actually stops
        // monitoring and ends the process.
        return TrayApp.Run(startHidden, initialOptions);
    }

    /// <summary>
    /// Cancels the given token source and unblocks the blocking WinDivert Recv()
    /// call immediately, so the monitoring loop notices the stop request right
    /// away instead of waiting for the next packet. Shared by the console Ctrl+C
    /// handler and the tray "Durdur" menu item.
    /// </summary>
    internal static void RequestStop(CancellationTokenSource cts)
    {
        cts.Cancel();
        // This can race with the monitoring loop's own `using var handle`
        // disposal on shutdown, so ignore the case where the handle is already gone.
        try
        {
            _handle?.Shutdown();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Full monitoring session: logging, adaptive engine, live panel (if enabled),
    /// and the packet loop. Used both by the interactive console flow and by
    /// TrayApp, which runs it on a dedicated background thread.
    /// </summary>
    internal static async Task RunMonitoringSessionAsync(bool verbose, bool noLog, bool noStatus, CancellationToken ct)
    {
        _verbose = verbose;

        if (!noLog)
        {
            _logger = new FileLogger();
            _logger.Info("ZeroDivert started");
            WriteInfo($"Logs: {_logger.GetLogDirectory()}");
        }

        var profileManager = new ProfileManager();
        WriteInfo($"Profile: {profileManager.GetProfilePath()}");

        var adaptiveEngine = new AdaptiveEngine(profileManager);
        adaptiveEngine.OnLog += msg => WriteInfo($"[Adaptive] {msg}");
        adaptiveEngine.OnTechniqueChanged += t => _panel?.SetTechnique(t.ToString());

        await adaptiveEngine.InitializeAsync();

        if (!noStatus && !verbose)
        {
            _panel = new SpectreStatusPanel();
            _panel.SetTechnique(adaptiveEngine.CurrentTechnique.ToString());
            _panel.Start();
        }

        try
        {
            await RunAsync(adaptiveEngine, ct);
        }
        catch (Exception ex)
        {
            WriteError($"Fatal error: {ex.Message}");
            _logger?.Error($"Fatal error: {ex}");
            throw;
        }
        finally
        {
            _panel?.Dispose();
            _panel = null;

            if (_logger != null)
            {
                _logger.Info("ZeroDivert stopped");
                await _logger.DisposeAsync();
                _logger = null;
            }
        }
    }

    private static async Task RunAsync(AdaptiveEngine adaptiveEngine, CancellationToken ct)
    {
        var filter = DiscordFilter.GenerateFilter();
        WriteInfo($"Filter: {filter}");

        _handle = WinDivertHandle.Open(filter, WinDivertLayer.Network, 0, WinDivertFlag.None);
        using var handle = _handle;

        // Set queue parameters for better performance
        handle.SetParam(WinDivertParam.QueueLength, 16384);
        handle.SetParam(WinDivertParam.QueueTime, 8000);
        handle.SetParam(WinDivertParam.QueueSize, 33554432); // 32MB

        WriteSuccess("WinDivert initialized successfully!");

        if (_panel != null)
        {
            WriteInfo("Monitoring Discord traffic... Press Ctrl+C to stop.");
            System.Console.WriteLine(); // Empty line before status
        }
        else
        {
            WriteInfo("Monitoring Discord traffic... Press Ctrl+C to stop.\n");
        }

        var buffer = new byte[65535];
        var addr = new WinDivertAddress();
        var engine = adaptiveEngine.GetEngine();

        // Stats logging timer
        var statsTimer = new System.Timers.Timer(60000); // Log stats every minute
        statsTimer.Elapsed += (_, _) =>
        {
            if (_panel != null)
            {
                // Status line handles display
            }
            else if (_verbose)
            {
                // Verbose mode prints stats
            }
        };
        statsTimer.Start();

        long packetsTotal = 0;
        long packetsModified = 0;
        long discordPackets = 0;

        // Tracks Discord ClientHellos we've sent (by local port) that haven't been
        // confirmed yet, so the outcome can be attributed to the technique currently
        // in use and reported back to the adaptive engine. An entry is cleared by:
        //   - an inbound RST on that port -> explicit failure (DPI reset the connection)
        //   - any other inbound packet on that port -> the server responded, treat as OK
        //   - no inbound packet at all before helloTimeout -> silent failure (DPI is
        //     blackholing the packets rather than sending a RST, which a pure
        //     RST-based check would never notice)
        var pendingHellos = new Dictionary<ushort, DateTime>();
        var helloTimeout = TimeSpan.FromSeconds(6);
        var lastPendingSweep = DateTime.UtcNow;
        var pendingSweepInterval = TimeSpan.FromSeconds(1);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!handle.Recv(buffer, out var recvLen, ref addr))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 995) break; // Operation aborted
                    continue;
                }

                var packet = buffer.AsSpan(0, (int)recvLen);
                packetsTotal++;
                Interlocked.Add(ref _bytesProcessed, recvLen);

                _panel?.UpdateStats(packetsTotal, packetsModified, discordPackets, _bytesProcessed);

                // Analyze packet
                var context = PacketInspector.Analyze(packet, addr.Outbound);

                if (!addr.Outbound && context.IsTcp && pendingHellos.Remove(context.DstPort, out _))
                {
                    if (context.IsRst)
                    {
                        adaptiveEngine.ReportFailure("Connection reset shortly after ClientHello");
                    }
                    // Any other inbound packet on this port means the server responded
                    // (SYN-ACK/data) -> the handshake is progressing, nothing to report.
                }

                if (DateTime.UtcNow - lastPendingSweep > pendingSweepInterval)
                {
                    lastPendingSweep = DateTime.UtcNow;
                    var cutoff = DateTime.UtcNow - helloTimeout;
                    foreach (var stalePort in pendingHellos.Where(p => p.Value < cutoff).Select(p => p.Key).ToList())
                    {
                        pendingHellos.Remove(stalePort);
                        adaptiveEngine.ReportFailure("No response within timeout (possible silent DPI drop)");
                    }
                }

                var shouldProcess = false;
                string? sni = null;

                // Check for Discord traffic
                if (context.IsTcp && context.IsClientHello)
                {
                    var ipHeaderLen = context.IsIPv6 ? 40 : (packet[0] & 0x0F) * 4;
                    var tcpHeaderLen = ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
                    var payload = packet[(ipHeaderLen + tcpHeaderLen)..];

                    sni = PacketInspector.ExtractSni(payload);
                    shouldProcess = DiscordFilter.IsDiscordSni(sni);

                    if (shouldProcess)
                    {
                        discordPackets++;
                        _panel?.SetStatus($"TLS -> {sni}");

                        if (_verbose)
                        {
                            _panel?.WriteLine($"[Discord] ClientHello -> {sni}");
                        }

                        _logger?.LogPacket(sni ?? "unknown", adaptiveEngine.CurrentTechnique.ToString(), 0, true);
                    }
                }
                else if (context.IsUdp && DiscordFilter.IsVoicePort(context.DstPort))
                {
                    shouldProcess = true;
                    discordPackets++;

                    if (_verbose)
                    {
                        _panel?.WriteLine($"[Discord] UDP Voice -> :{context.DstPort}");
                    }
                }

                if (shouldProcess && addr.Outbound)
                {
                    if (context.IsClientHello)
                    {
                        pendingHellos[context.SrcPort] = DateTime.UtcNow;
                    }

                    // Apply DPI bypass
                    var results = engine.ProcessPacket(packet, context);

                    if (_verbose && results.Count > 0)
                    {
                        _panel?.WriteLine($"[Bypass] {results.Count} packets, Orig={packet.Length}B, New={results[0].Data.Length}B");
                    }

                    foreach (var result in results)
                    {
                        if (result.DelayMs > 0)
                            await Task.Delay(result.DelayMs, ct);

                        if (result.RecalcChecksum)
                        {
                            WinDivertHandle.CalcChecksums(result.Data, ref addr);
                        }

                        handle.Send(result.Data, out _, ref addr);
                    }

                    if (results.Count > 1 || results[0].RecalcChecksum)
                    {
                        packetsModified++;
                        adaptiveEngine.ReportSuccess();
                    }
                }
                else
                {
                    // Pass through unmodified
                    handle.Send(packet, out _, ref addr);
                }
            }
        }
        finally
        {
            statsTimer.Stop();
            handle.Shutdown();
            _handle = null;

            _logger?.LogStats(packetsTotal, discordPackets, packetsModified, _bytesProcessed);
        }
    }

    internal static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("ZeroDivert").Centered().Color(Color.Cyan1));
        AnsiConsole.Write(new Rule("[grey]Discord DPI Bypass Tool v1.1.0[/]").Centered());
        AnsiConsole.WriteLine();
    }

    private static SelectionPrompt<string> CreateMenu(string title) =>
        new SelectionPrompt<string>()
            .Title($"[bold]{title}[/] [grey](ok tuşları ile gezin, Enter ile seçin)[/]")
            .HighlightStyle(new Style(foreground: Color.SpringGreen1, decoration: Decoration.Bold))
            .PageSize(10)
            .WrapAround();

    // ── Home menu ──────────────────────────────────────────────────────────

    private const string StartChoice = "▶  Başlat (Otomatik - önerilen)";
    private const string SettingsChoice = "⚙  Ayarlar";
    private const string InfoChoice = "ℹ  Log ve Profil Bilgisi";
    private const string ExitChoice = "✕  Çıkış";

    /// <summary>
    /// Returns the chosen run options, or null if the user picked "Çıkış".
    /// </summary>
    private static (bool verbose, bool noLog, bool noStatus)? RunHomeMenu()
    {
        var options = (verbose: false, noLog: false, noStatus: false);

        while (true)
        {
            AnsiConsole.Clear();
            PrintBanner();

            var choice = AnsiConsole.Prompt(
                CreateMenu("Ana Menü")
                    .AddChoices(StartChoice, SettingsChoice, InfoChoice, ExitChoice));

            switch (choice)
            {
                case StartChoice:
                    AnsiConsole.Clear();
                    PrintBanner();
                    return options;
                case SettingsChoice:
                    options = RunSettingsMenu(options);
                    break;
                case InfoChoice:
                    ShowInfoScreen();
                    break;
                default:
                    return null;
            }
        }
    }

    private static (bool verbose, bool noLog, bool noStatus) RunSettingsMenu(
        (bool verbose, bool noLog, bool noStatus) options)
    {
        const string runOptionsChoice = "Çalışma seçenekleri (-v / --no-log / --no-status)";
        const string dnsChoice = "DNS Ayarları";
        const string autoStartChoice = "Başlangıçta Otomatik Başlat (tepsi simgesi)";
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintBanner();
            AnsiConsole.MarkupLine("[grey]Ayarlar[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                CreateMenu("Ayarlar")
                    .AddChoices(runOptionsChoice, dnsChoice, autoStartChoice, backChoice));

            switch (choice)
            {
                case var c when c == runOptionsChoice:
                    options = PromptForOptions(options);
                    break;
                case var c when c == dnsChoice:
                    RunDnsMenu();
                    break;
                case var c when c == autoStartChoice:
                    RunAutoStartMenu();
                    break;
                default:
                    return options;
            }
        }
    }

    private static void RunAutoStartMenu()
    {
        const string installChoice = "Kur (Windows'a giriş yapınca otomatik başlat)";
        const string uninstallChoice = "Kaldır";
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintBanner();
            AnsiConsole.MarkupLine("[grey]Ayarlar[/] [grey]>[/] [bold]Başlangıçta Otomatik Başlat[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "Kurulduğunda ZeroDivert, Windows'a giriş yaptığınızda tepsi simgesiyle arka planda başlar.");
            AnsiConsole.MarkupLine("Son bıraktığınız durumu ([green]çalışıyor[/] / [yellow]durduruldu[/]) hatırlar.");
            AnsiConsole.WriteLine();

            var installed = AutoStartManager.IsInstalled();
            AnsiConsole.MarkupLine(installed
                ? "Durum: [green]Kurulu[/]"
                : "Durum: [grey]Kurulu değil[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                CreateMenu("Başlangıçta Otomatik Başlat")
                    .AddChoices(installed ? uninstallChoice : installChoice, backChoice));

            if (choice == backChoice) return;

            var result = choice == installChoice
                ? AutoStartManager.Install()
                : AutoStartManager.Uninstall();

            AnsiConsole.MarkupLine(result.Success
                ? $"[green]{Markup.Escape(result.Message)}[/]"
                : $"[red]Hata: {Markup.Escape(result.Message)}[/]");
            AnsiConsole.MarkupLine("[grey]Devam etmek için bir tuşa basın...[/]");
            System.Console.ReadKey(true);
        }
    }

    private static void RunDnsMenu()
    {
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintBanner();
            AnsiConsole.MarkupLine("[grey]Ayarlar[/] [grey]>[/] [bold]DNS[/]");
            AnsiConsole.WriteLine();

            var adapters = DnsSettingsManager.ListAdapters();
            if (adapters.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]Aktif ağ adaptörü bulunamadı.[/]");
                AnsiConsole.MarkupLine("[grey]Devam etmek için bir tuşa basın...[/]");
                System.Console.ReadKey(true);
                return;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Adaptör");
            table.AddColumn("DNS Kaynağı");
            table.AddColumn("DNS Sunucuları");
            foreach (var adapter in adapters)
            {
                table.AddRow(
                    Markup.Escape(adapter.Name),
                    adapter.IsDhcp ? "[grey]Otomatik (DHCP)[/]" : "[yellow]El ile[/]",
                    adapter.DnsServers.Count > 0 ? Markup.Escape(string.Join(", ", adapter.DnsServers)) : "[grey]-[/]");
            }
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var adapterChoices = adapters.Select(a => a.Name).Append(backChoice).ToArray();
            var selectedAdapter = AnsiConsole.Prompt(
                CreateMenu("DNS Ayarları")
                    .AddChoices(adapterChoices));

            if (selectedAdapter == backChoice) return;

            const string cloudflareChoice = "Cloudflare (1.1.1.1 / 1.0.0.1)";
            const string googleChoice = "Google (8.8.8.8 / 8.8.4.4)";
            const string customChoice = "Özel DNS gir";
            const string dhcpChoice = "Otomatik (DHCP) - varsayılana dön";

            var dnsChoice = AnsiConsole.Prompt(
                CreateMenu($"{Markup.Escape(selectedAdapter)} için DNS seçin")
                    .AddChoices(cloudflareChoice, googleChoice, customChoice, dhcpChoice, backChoice));

            (bool ok, string message) result;
            switch (dnsChoice)
            {
                case cloudflareChoice:
                    result = DnsSettingsManager.SetDns(selectedAdapter, "1.1.1.1", "1.0.0.1");
                    break;
                case googleChoice:
                    result = DnsSettingsManager.SetDns(selectedAdapter, "8.8.8.8", "8.8.4.4");
                    break;
                case customChoice:
                    var (primary, secondary) = PromptCustomDns();
                    result = DnsSettingsManager.SetDns(selectedAdapter, primary, secondary);
                    break;
                case dhcpChoice:
                    result = DnsSettingsManager.ResetToDhcp(selectedAdapter);
                    break;
                default:
                    continue;
            }

            AnsiConsole.MarkupLine(result.ok
                ? $"[green]{Markup.Escape(result.message)}[/]"
                : $"[red]Hata: {Markup.Escape(result.message)}[/]");
            AnsiConsole.MarkupLine("[grey]Devam etmek için bir tuşa basın...[/]");
            System.Console.ReadKey(true);
        }
    }

    private static (string primary, string? secondary) PromptCustomDns()
    {
        var primary = AnsiConsole.Prompt(
            new TextPrompt<string>("Birincil DNS adresi:")
                .Validate(v => System.Net.IPAddress.TryParse(v, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Geçersiz IP adresi[/]")));

        var secondary = AnsiConsole.Prompt(
            new TextPrompt<string>("İkincil DNS adresi (opsiyonel, boş bırakabilirsiniz):")
                .AllowEmpty()
                .Validate(v => string.IsNullOrWhiteSpace(v) || System.Net.IPAddress.TryParse(v, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Geçersiz IP adresi[/]")));

        return (primary, string.IsNullOrWhiteSpace(secondary) ? null : secondary);
    }

    private static void ShowInfoScreen()
    {
        AnsiConsole.Clear();
        PrintBanner();
        AnsiConsole.MarkupLine("[grey]Log ve Profil Bilgisi[/]");
        AnsiConsole.WriteLine();

        var profileManager = new ProfileManager();
        var profilePath = profileManager.GetProfilePath();
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZeroDivert", "logs");

        var latestLog = Directory.Exists(logDir)
            ? new DirectoryInfo(logDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;

        var table = new Table().Border(TableBorder.Rounded).HideHeaders();
        table.AddColumn("");
        table.AddColumn("");
        table.AddRow("[grey]Profil dosyası[/]", Markup.Escape(profilePath));
        table.AddRow(
            "[grey]Profil durumu[/]",
            File.Exists(profilePath) ? "[green]Kayıtlı[/]" : "[yellow]Yok (ilk çalıştırmada kalibrasyon başlar)[/]");
        table.AddRow("[grey]Log klasörü[/]", Markup.Escape(logDir));
        table.AddRow("[grey]Son log dosyası[/]", latestLog != null ? Markup.Escape(latestLog.Name) : "[grey]-[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (File.Exists(profilePath))
        {
            // Read-only: LoadOrCreateAsync doesn't write anything back unless SaveAsync
            // is called, which it isn't here.
            var profile = profileManager.LoadOrCreateAsync().GetAwaiter().GetResult();

            var profileTable = new Table().Border(TableBorder.Rounded).HideHeaders();
            profileTable.AddColumn("");
            profileTable.AddColumn("");
            profileTable.AddRow("[grey]Teknik[/]", Markup.Escape(profile.Strategy.Technique.ToString()));
            profileTable.AddRow(
                "[grey]Doğrulanmış[/]",
                profile.IsVerified ? "[green]Evet[/]" : "[yellow]Hayır (kalibrasyon devam ediyor)[/]");
            profileTable.AddRow("[grey]Başarı oranı[/]", $"%{profile.SuccessRate}");
            profileTable.AddRow("[grey]Test geçmişi[/]", $"{profile.TestHistory.Count} kayıt");
            profileTable.AddRow("[grey]Oluşturulma[/]", profile.CreatedAt.ToLocalTime().ToString("g"));
            profileTable.AddRow("[grey]Son kullanım[/]", profile.LastUsed.ToLocalTime().ToString("g"));
            profileTable.AddRow("[grey]Son test[/]", profile.LastTested.ToLocalTime().ToString("g"));
            if (!string.IsNullOrEmpty(profile.NetworkFingerprint))
            {
                profileTable.AddRow("[grey]Ağ parmak izi[/]", Markup.Escape(profile.NetworkFingerprint));
            }

            AnsiConsole.Write(profileTable);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[grey]Devam etmek için bir tuşa basın...[/]");
        System.Console.ReadKey(true);
    }

    private static (bool verbose, bool noLog, bool noStatus) PromptForOptions(
        (bool verbose, bool noLog, bool noStatus) current)
    {
        const string verboseChoice = "Detaylı çıktı (-v)";
        const string noLogChoice = "Log kaydı olmadan (--no-log)";
        const string noStatusChoice = "Canlı durum panelini kapat (--no-status)";

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Çalışma seçeneklerini belirleyin [grey](boşluk ile işaretle, Enter ile onayla)[/]")
            .NotRequired()
            .AddChoices(verboseChoice, noLogChoice, noStatusChoice);

        if (current.verbose) prompt.Select(verboseChoice);
        if (current.noLog) prompt.Select(noLogChoice);
        if (current.noStatus) prompt.Select(noStatusChoice);

        var selected = AnsiConsole.Prompt(prompt);

        return (
            selected.Contains(verboseChoice),
            selected.Contains(noLogChoice),
            selected.Contains(noStatusChoice));
    }

    private static void WriteInfo(string message)
    {
        if (_panel != null)
        {
            _panel.WriteLine($"[*] {message}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[blue][[*]][/] {Markup.Escape(message)}");
        }
    }

    private static void WriteSuccess(string message)
    {
        if (_panel != null)
        {
            _panel.WriteLine($"[+] {message}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green][[+]][/] {Markup.Escape(message)}");
        }
    }

    private static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red][[-]][/] {Markup.Escape(message)}");
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
