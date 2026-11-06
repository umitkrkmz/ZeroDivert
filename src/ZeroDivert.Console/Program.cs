using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Spectre.Console;
using Color = Spectre.Console.Color;
using ZeroDivert.Core.Adaptive;
using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;
using ZeroDivert.Core.Detection;
using ZeroDivert.Core.Dns;
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

        // Only one copy may run: two would each open a WinDivert handle and process
        // the same Discord packets twice. A second launch (e.g. clicking the Start
        // menu shortcut while ZeroDivert sits in the tray) just asks the running copy
        // to show its window and exits. The installer also checks this mutex to stop
        // upgrades/uninstalls while the app is running.
        using var instanceMutex = new Mutex(initiallyOwned: true, AppInfo.SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var showSignal = EventWaitHandle.OpenExisting(AppInfo.ShowConsoleEventName);
                showSignal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The other copy is still starting up; nothing to signal yet.
            }

            return 0;
        }

        ListenForShowConsoleRequests();
        UpdateChecker.StartIfEnabled();

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
    /// Brings this copy's console window back when a second copy is launched (see
    /// the single-instance check in Main).
    /// </summary>
    private static void ListenForShowConsoleRequests()
    {
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowConsoleEventName);
        var listener = new Thread(() =>
        {
            while (showSignal.WaitOne())
            {
                TrayApp.ShowConsole();
            }
        })
        {
            IsBackground = true,
            Name = "ShowConsoleListener"
        };
        listener.Start();
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

        await adaptiveEngine.InitializeAsync(AppStateStore.Load().PinnedTechnique);

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
        // Answer every app's plain DNS over DoH while monitoring (see DnsInterceptor).
        using var dnsInterceptor = AppStateStore.Load().DnsOverHttps
            ? new DnsInterceptor(DoHClient.DefaultServerUrl)
            : null;
        if (dnsInterceptor != null)
        {
            dnsInterceptor.OnLog += WriteInfo;
            WriteInfo($"DNS: tüm uygulamaların DNS sorguları DoH ile cevaplanıyor ({DoHClient.DefaultServerUrl})");
        }

        var filter = DiscordFilter.GenerateFilter(interceptDns: dnsInterceptor != null);
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

        // Judges each Discord handshake from what the filter actually captures (our
        // outbound packets + inbound RSTs) and reports the outcome to the adaptive
        // engine, which rotates techniques on repeated failures.
        var handshakes = new HandshakeTracker();
        var lastTimeoutSweep = DateTime.UtcNow;

        // ClientHellos split across TCP segments with the SNI not in the first one:
        // those connections can't be recognized (see PacketInspector.IsPartialClientHello).
        long unreadableSni = 0;

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
                _panel?.UpdateDns(dnsInterceptor != null, dnsInterceptor?.AnsweredViaDoH ?? 0, dnsInterceptor?.FellBackToPlainDns ?? 0);

                var context = PacketInspector.Analyze(packet, addr.Outbound);

                if (context.IsUdp)
                {
                    // Only outbound DNS queries match the filter's UDP clause. The
                    // interceptor answers asynchronously, so the loop never waits on DoH.
                    var queryAddr = addr;
                    if (dnsInterceptor?.TryIntercept(packet, addr.Outbound,
                            (bytes, asInboundReply) => InjectFromAnyThread(bytes, queryAddr, asInboundReply)) != true)
                    {
                        handle.Send(packet, out _, ref addr);
                    }

                    continue;
                }

                if (!addr.Outbound)
                {
                    // Only inbound RSTs from port 443 match the filter.
                    if (context.IsTcp && context.IsRst &&
                        handshakes.InboundReset(context.DstPort) == HandshakeOutcome.Reset)
                    {
                        adaptiveEngine.ReportFailure("Connection reset after ClientHello");
                    }

                    handle.Send(packet, out _, ref addr);
                    continue;
                }

                var now = DateTime.UtcNow;
                if (now - lastTimeoutSweep > TimeSpan.FromSeconds(1))
                {
                    lastTimeoutSweep = now;
                    for (var timedOut = handshakes.TakeTimedOut(now); timedOut > 0; timedOut--)
                    {
                        adaptiveEngine.ReportFailure("No response within timeout (possible silent DPI drop)");
                    }
                }

                if (!context.IsTcp || !TryGetTcpPayload(packet, context.IsIPv6, out var payloadOffset, out var seq)
                    || payloadOffset >= packet.Length)
                {
                    handle.Send(packet, out _, ref addr);
                    continue;
                }

                if (handshakes.OutboundData(context.SrcPort, seq) == HandshakeOutcome.Succeeded)
                {
                    adaptiveEngine.ReportSuccess();
                }

                var payload = packet[payloadOffset..];
                var sni = context.IsClientHello ? PacketInspector.ExtractSni(payload) : null;
                if (sni == null && context.IsClientHello && PacketInspector.IsPartialClientHello(payload))
                {
                    unreadableSni++;
                    if (unreadableSni is 1 or 10 or 100 or 1000)
                    {
                        WriteInfo($"TLS: SNI'si ilk TCP segmentinde olmayan ClientHello sayısı: {unreadableSni} (bu bağlantılar Discord olarak tanınamıyor)");
                    }
                }

                if (!DiscordFilter.IsDiscordSni(sni))
                {
                    // Pass through unmodified
                    handle.Send(packet, out _, ref addr);
                    continue;
                }

                discordPackets++;
                _panel?.SetStatus($"TLS -> {sni}");
                if (_verbose)
                {
                    _panel?.WriteLine($"[Discord] ClientHello -> {sni}");
                }
                _logger?.LogPacket(sni!, adaptiveEngine.CurrentTechnique.ToString(), 0, true);

                handshakes.ClientHelloSent(context.SrcPort, seq, BinaryPrimitives.ReadUInt16BigEndian(payload[3..]), now);

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
                }
            }
        }
        finally
        {
            // First, so DoH answers still in flight are dropped rather than sent
            // through a handle that is about to close.
            dnsInterceptor?.Dispose();

            statsTimer.Stop();
            handle.Shutdown();
            _handle = null;

            _logger?.LogStats(packetsTotal, discordPackets, packetsModified, _bytesProcessed);
            _logger?.Info($"Split ClientHellos with SNI outside the first segment: {unreadableSni}");
        }
    }

    /// <summary>
    /// Sends a packet built off the capture thread (a DoH answer, or the original DNS
    /// query when DoH failed). <paramref name="addr"/> is a copy of the query's own
    /// address; a reply keeps its interface indexes but is marked inbound, the same
    /// way WinDivert's netfilter sample injects replies.
    /// </summary>
    private static void InjectFromAnyThread(byte[] packet, WinDivertAddress addr, bool asInboundReply)
    {
        var handle = _handle;
        if (handle == null) return;

        if (asInboundReply)
        {
            addr.Outbound = false;
        }

        try
        {
            WinDivertHandle.CalcChecksums(packet, ref addr);
            handle.Send(packet, out _, ref addr);
        }
        catch (ObjectDisposedException)
        {
            // Monitoring stopped while the answer was in flight; nothing to send through.
        }
    }

    /// <summary>Offset of the TCP payload and the segment's sequence number.</summary>
    private static bool TryGetTcpPayload(ReadOnlySpan<byte> packet, bool isIPv6, out int payloadOffset, out uint seq)
    {
        payloadOffset = 0;
        seq = 0;
        var ipHeaderLen = isIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        if (packet.Length < ipHeaderLen + 20) return false;

        seq = BinaryPrimitives.ReadUInt32BigEndian(packet[(ipHeaderLen + 4)..]);
        payloadOffset = ipHeaderLen + ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
        return payloadOffset <= packet.Length;
    }

    internal static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("ZeroDivert").Centered().Color(Color.Cyan1));
        AnsiConsole.Write(new Rule($"[grey]Discord DPI Bypass Tool {AppInfo.VersionText}[/]").Centered());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// One-line header for sub-screens. The Figlet banner is 6 lines; repeating it
    /// above a table and a menu pushed screens like DNS past the 30 rows of a default
    /// console window, scrolling the top out of view.
    /// </summary>
    private static void PrintHeader()
    {
        AnsiConsole.Write(new Rule($"[cyan1]ZeroDivert[/] [grey]{AppInfo.VersionText}[/]").LeftJustified());
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
    private const string AboutChoice = "?  Hakkında";
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

            // The update check runs in the background, so this shows up on the next
            // redraw once it has finished.
            if (UpdateChecker.Available is { } update)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Yeni sürüm var: {Markup.Escape(update.LatestTag)}[/] [grey](indirmek için: Hakkında)[/]");
                AnsiConsole.WriteLine();
            }

            var choice = AnsiConsole.Prompt(
                CreateMenu("Ana Menü")
                    .AddChoices(StartChoice, SettingsChoice, InfoChoice, AboutChoice, ExitChoice));

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
                case AboutChoice:
                    ShowAboutScreen();
                    break;
                default:
                    return null;
            }
        }
    }

    private static void ShowAboutScreen()
    {
        const string downloadChoice = "Yeni sürümün indirme sayfasını aç";
        const string repoChoice = "GitHub sayfasını aç";
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintHeader();
            AnsiConsole.MarkupLine("[grey]Hakkında[/]");
            AnsiConsole.WriteLine();

            var update = UpdateChecker.Available;
            var updateText = UpdateChecker.Status switch
            {
                UpdateChecker.CheckStatus.Disabled => "[grey]Kapalı (Ayarlar'dan açılabilir)[/]",
                UpdateChecker.CheckStatus.Checking => "[grey]Kontrol ediliyor...[/]",
                UpdateChecker.CheckStatus.Failed => "[grey]Kontrol edilemedi (internet bağlantısı yok ya da GitHub'a ulaşılamadı)[/]",
                UpdateChecker.CheckStatus.UpdateAvailable => $"[yellow]Yeni sürüm var: {Markup.Escape(update!.LatestTag)}[/]",
                _ => "[green]Güncel[/]"
            };

            var table = new Table().Border(TableBorder.Rounded).HideHeaders();
            table.AddColumn("");
            table.AddColumn("");
            table.AddRow("[grey]Sürüm[/]", AppInfo.VersionText);
            table.AddRow("[grey]Güncelleme[/]", updateText);
            table.AddRow("[grey]Lisans[/]", AppInfo.License);
            table.AddRow("[grey]Kaynak kod[/]", AppInfo.RepoUrl);
            if (update != null)
            {
                table.AddRow("[grey]İndirme[/]", Markup.Escape(update.ReleaseUrl));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var choices = new List<string>();
            if (update != null) choices.Add(downloadChoice);
            choices.Add(repoChoice);
            choices.Add(backChoice);

            var choice = AnsiConsole.Prompt(CreateMenu("Hakkında").AddChoices(choices));
            switch (choice)
            {
                case downloadChoice:
                    AppInfo.OpenInBrowser(update!.ReleaseUrl);
                    break;
                case repoChoice:
                    AppInfo.OpenInBrowser(AppInfo.RepoUrl);
                    break;
                default:
                    return;
            }
        }
    }

    private static (bool verbose, bool noLog, bool noStatus) RunSettingsMenu(
        (bool verbose, bool noLog, bool noStatus) options)
    {
        const string runOptionsChoice = "Çalışma seçenekleri (-v / --no-log / --no-status)";
        const string dnsChoice = "DNS Ayarları";
        const string autoStartChoice = "Başlangıçta Otomatik Başlat (tepsi simgesi)";
        const string techniqueChoicePrefix = "Bypass tekniği: ";
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintHeader();
            AnsiConsole.MarkupLine("[grey]Ayarlar[/]");
            AnsiConsole.WriteLine();

            var state = AppStateStore.Load();
            var techniqueChoice = techniqueChoicePrefix + TechniqueLabel(state.PinnedTechnique);
            var checkForUpdates = state.CheckForUpdates;
            var dnsOverHttps = state.DnsOverHttps;
            var dohChoice = dnsOverHttps
                ? "DNS koruması (DoH): Açık (kapatmak için seçin)"
                : "DNS koruması (DoH): Kapalı (açmak için seçin)";
            var updateCheckChoice = checkForUpdates
                ? "Güncelleme kontrolü: Açık (kapatmak için seçin)"
                : "Güncelleme kontrolü: Kapalı (açmak için seçin)";

            var choice = AnsiConsole.Prompt(
                CreateMenu("Ayarlar")
                    .AddChoices(runOptionsChoice, techniqueChoice, dohChoice, dnsChoice, autoStartChoice, updateCheckChoice, backChoice));

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
                case var c when c == techniqueChoice:
                    PromptForTechnique(state.PinnedTechnique);
                    break;
                case var c when c == dohChoice:
                    // Read when monitoring starts, so it applies from the next Başlat.
                    AppStateStore.Update(s => s.DnsOverHttps = !dnsOverHttps);
                    break;
                case var c when c == updateCheckChoice:
                    AppStateStore.Update(s => s.CheckForUpdates = !checkForUpdates);
                    // Takes effect on the next launch when turned off; turning it on
                    // starts a check right away.
                    UpdateChecker.StartIfEnabled();
                    break;
                default:
                    return options;
            }
        }
    }

    private static readonly (BypassTechnique? Technique, string Label)[] TechniqueChoices =
    [
        (null, "Otomatik (önerilen)"),
        (BypassTechnique.TcpFragmentation, "TCP Fragmentasyon"),
        (BypassTechnique.TcpDesync, "TCP Desync"),
        (BypassTechnique.FakeTtl, "Fake TTL"),
        (BypassTechnique.GoodbyeDpi, "GoodbyeDPI uyumlu")
    ];

    private static string TechniqueLabel(BypassTechnique? technique) =>
        TechniqueChoices.FirstOrDefault(c => c.Technique == technique).Label ?? technique.ToString()!;

    private static void PromptForTechnique(BypassTechnique? current)
    {
        AnsiConsole.Clear();
        PrintHeader();
        AnsiConsole.MarkupLine("[grey]Ayarlar[/] [grey]>[/] [bold]Bypass tekniği[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Otomatik[/]: ZeroDivert teknikleri sırayla dener; Discord bağlantısı art arda başarısız olursa bir sonrakine geçer ve çalışanı hatırlar.");
        AnsiConsole.MarkupLine("Belirli bir tekniği seçerseniz otomatik geçiş yapılmaz; karşılaştırmalı test için kullanışlıdır.");
        AnsiConsole.MarkupLine($"Şu an: [green]{Markup.Escape(TechniqueLabel(current))}[/]. Değişiklik bir sonraki [bold]Başlat[/]'ta geçerli olur.");
        AnsiConsole.WriteLine();

        var labels = TechniqueChoices.Select(c => c.Label).Append("◀  Geri").ToArray();
        var chosen = AnsiConsole.Prompt(CreateMenu("Bypass tekniği").AddChoices(labels));

        var match = TechniqueChoices.FirstOrDefault(c => c.Label == chosen);
        if (match.Label == null) return; // Geri

        AppStateStore.Update(s => s.PinnedTechnique = match.Technique);
    }

    private static void RunAutoStartMenu()
    {
        const string installChoice = "Kur (Windows'a giriş yapınca otomatik başlat)";
        const string uninstallChoice = "Kaldır";
        const string backChoice = "◀  Geri";

        while (true)
        {
            AnsiConsole.Clear();
            PrintHeader();
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
            PrintHeader();
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

            WriteDnsTable(adapters);
            WriteDnsWarnings(adapters);

            var adapterChoices = adapters.Select(a => a.Name).Append(backChoice).ToArray();
            var selectedName = AnsiConsole.Prompt(CreateMenu("DNS Ayarları").AddChoices(adapterChoices));
            if (selectedName == backChoice) return;
            var adapter = adapters.First(a => a.Name == selectedName);

            var cloudflareChoice = PresetLabel(DnsSettingsManager.Cloudflare, adapter);
            var googleChoice = PresetLabel(DnsSettingsManager.Google, adapter);
            const string customChoice = "Özel DNS gir";
            const string dhcpChoice = "Otomatik (DHCP) - varsayılana dön (IPv4 ve IPv6)";

            var dnsChoice = AnsiConsole.Prompt(
                CreateMenu($"{Markup.Escape(adapter.Name)} için DNS seçin")
                    .AddChoices(cloudflareChoice, googleChoice, customChoice, dhcpChoice, backChoice));

            DnsApplyResult result;
            if (dnsChoice == cloudflareChoice) result = DnsSettingsManager.Apply(adapter, DnsSettingsManager.Cloudflare);
            else if (dnsChoice == googleChoice) result = DnsSettingsManager.Apply(adapter, DnsSettingsManager.Google);
            else if (dnsChoice == customChoice) result = DnsSettingsManager.Apply(adapter, PromptCustomDns(adapter));
            else if (dnsChoice == dhcpChoice) result = DnsSettingsManager.ResetToDhcp(adapter);
            else continue;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(result.Success
                ? $"[green]{Markup.Escape(result.Message)}[/]"
                : $"[red]Hata: {Markup.Escape(result.Message)}[/]");
            foreach (var warning in result.Warnings)
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
            }

            if (result.Success)
            {
                VerifyDns(adapter, offerRevert: dnsChoice != dhcpChoice);
            }

            AnsiConsole.MarkupLine("[grey]Devam etmek için bir tuşa basın...[/]");
            System.Console.ReadKey(true);
        }
    }

    private static string PresetLabel(DnsPreset preset, NetworkAdapterInfo adapter)
    {
        var addresses = string.Join(" / ", preset.Ipv4);
        if (adapter.SupportsIPv6) addresses += " + IPv6";
        var encrypted = DnsSettingsManager.SupportsEncryptedDns ? ", şifreli" : "";
        return $"{preset.Name} ({addresses}{encrypted})";
    }

    private static void WriteDnsTable(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Adaptör");
        table.AddColumn("DNS Kaynağı");
        table.AddColumn("IPv4 DNS");
        table.AddColumn("IPv6 DNS");
        foreach (var adapter in adapters)
        {
            table.AddRow(
                Markup.Escape(adapter.Name),
                adapter.IsDhcp ? "[grey]Otomatik (DHCP)[/]" : "[yellow]El ile[/]",
                AddressCell(adapter.Ipv4Dns),
                adapter.SupportsIPv6 ? AddressCell(adapter.Ipv6Dns) : "[grey]IPv6 kapalı[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string AddressCell(IReadOnlyList<string> addresses) =>
        addresses.Count > 0 ? Markup.Escape(string.Join("\n", addresses)) : "[grey]-[/]";

    private static void WriteDnsWarnings(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var dnsProtection = AppStateStore.Load().DnsOverHttps;
        if (!dnsProtection && adapters.Any(a => a.Ipv6Dns.Count > 0))
        {
            AnsiConsole.MarkupLine("[yellow]IPv6 DNS sunucusu olan bir bağdaştırıcı var ve DNS koruması (DoH) kapalı: DNS sorguları bu yoldan İSS'ye gidip değiştirilebilir. Ayarlar'dan DNS korumasını açmanız önerilir.[/]");
        }

        if (!DnsSettingsManager.SupportsEncryptedDns)
        {
            AnsiConsole.MarkupLine("[yellow]Windows 10'da şifreli DNS yok: burada ayarlanan sunuculara sorgular düz metin gider. Düz DNS'in engellendiği hatlarda ZeroDivert'in DNS korumasını (DoH) açık tutun.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Not: Windows 11'de Ayarlar > Ağ ve İnternet > Wi-Fi altındaki \"Tüm Wi-Fi ağlarının DNS ayarları\" tanımlıysa, burada görünen ağ başına ayardan önce o kullanılır. Ayarı uyguladıktan sonra yapılan test, Windows'un gerçekte ne kullandığını gösterir.[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// After a change: show what the adapter now reports, then resolve a name the
    /// way apps do. If that fails, offer to go back to DHCP so the user isn't left
    /// without DNS.
    /// </summary>
    private static void VerifyDns(NetworkAdapterInfo adapter, bool offerRevert)
    {
        var updated = DnsSettingsManager.ListAdapters().FirstOrDefault(a => a.Name == adapter.Name) ?? adapter;
        AnsiConsole.MarkupLine($"Bağdaştırıcının bildirdiği DNS: IPv4 [bold]{Markup.Escape(string.Join(", ", updated.Ipv4Dns.DefaultIfEmpty("-")))}[/], IPv6 [bold]{Markup.Escape(string.Join(", ", updated.Ipv6Dns.DefaultIfEmpty("-")))}[/]");

        var check = AnsiConsole.Status().Start(
            $"{DnsSettingsManager.TestName} çözümleniyor...",
            _ => DnsSettingsManager.CheckResolutionAsync().GetAwaiter().GetResult());

        if (!check.Succeeded)
        {
            AnsiConsole.MarkupLine($"[red]{DnsSettingsManager.TestName} çözümlenemedi: yeni DNS sunucusuna ulaşılamıyor olabilir. Bu haliyle internet çalışmayabilir.[/]");
            if (!offerRevert) return;

            const string revert = "Otomatik (DHCP)'ye geri dön (önerilen)";
            const string keep = "Böyle kalsın";
            if (AnsiConsole.Prompt(CreateMenu("Ne yapalım?").AddChoices(revert, keep)) == revert)
            {
                var reverted = DnsSettingsManager.ResetToDhcp(adapter);
                AnsiConsole.MarkupLine(reverted.Success
                    ? $"[green]{Markup.Escape(reverted.Message)}[/]"
                    : $"[red]Hata: {Markup.Escape(reverted.Message)}[/]");
            }

            return;
        }

        var answer = string.Join(", ", check.SystemAddresses.Take(3));
        if (check.MatchesDoH == false)
        {
            AnsiConsole.MarkupLine($"[yellow]Windows {DnsSettingsManager.TestName} için {Markup.Escape(answer)} döndürdü, ama bu DoH ile alınan cevaptan farklı: DNS hâlâ İSS tarafından değiştiriliyor olabilir (ör. IPv6 DNS veya \"Tüm Wi-Fi ağlarının DNS ayarları\"). ZeroDivert'in DNS koruması (DoH) bunu izleme sırasında düzeltir.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Test başarılı: {DnsSettingsManager.TestName} → {Markup.Escape(answer)}[/]");
        }
    }

    private static DnsPreset PromptCustomDns(NetworkAdapterInfo adapter)
    {
        var ipv4 = new List<string> { PromptAddress("Birincil IPv4 DNS adresi:", AddressFamily.InterNetwork, optional: false)! };
        if (PromptAddress("İkincil IPv4 DNS adresi (opsiyonel):", AddressFamily.InterNetwork, optional: true) is { } v4b)
            ipv4.Add(v4b);

        var ipv6 = new List<string>();
        if (adapter.SupportsIPv6 &&
            PromptAddress("Birincil IPv6 DNS adresi (opsiyonel):", AddressFamily.InterNetworkV6, optional: true) is { } v6a)
        {
            ipv6.Add(v6a);
            if (PromptAddress("İkincil IPv6 DNS adresi (opsiyonel):", AddressFamily.InterNetworkV6, optional: true) is { } v6b)
                ipv6.Add(v6b);
        }

        return new DnsPreset("Özel", [.. ipv4], [.. ipv6], DohTemplate: null);
    }

    private static string? PromptAddress(string prompt, AddressFamily family, bool optional)
    {
        var familyName = family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
        var value = AnsiConsole.Prompt(
            new TextPrompt<string>(prompt)
                .AllowEmpty()
                .Validate(v =>
                    (optional && string.IsNullOrWhiteSpace(v)) ||
                    (System.Net.IPAddress.TryParse(v, out var ip) && ip.AddressFamily == family)
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[red]Geçerli bir {familyName} adresi girin[/]")));

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ShowInfoScreen()
    {
        AnsiConsole.Clear();
        PrintHeader();
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
