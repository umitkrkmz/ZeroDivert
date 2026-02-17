using System.Runtime.InteropServices;
using ZeroDivert.Core.Adaptive;
using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;
using ZeroDivert.Core.Detection;
using ZeroDivert.Core.Filters;
using ZeroDivert.Core.Logging;
using ZeroDivert.Core.UI;
using ZeroDivert.Driver;

namespace ZeroDivert.Console;

internal static class Program
{
    private static StatusLine? _statusLine;
    private static FileLogger? _logger;
    private static bool _verbose;
    private static long _bytesProcessed;
    private static WinDivertHandle? _handle;

    private static async Task<int> Main(string[] args)
    {
        System.Console.Title = "ZeroDivert - Discord DPI Bypass";
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintBanner();

        // Parse arguments
        _verbose = args.Contains("-v") || args.Contains("--verbose");
        var noLog = args.Contains("--no-log");
        var noStatus = args.Contains("--no-status");

        // Check admin
        if (!IsAdministrator())
        {
            WriteError("This application requires Administrator privileges!");
            WriteError("Please run as Administrator.");
            return 1;
        }

        WriteSuccess("Running with Administrator privileges");

        // Initialize logging
        if (!noLog)
        {
            _logger = new FileLogger();
            _logger.Info("ZeroDivert started");
            WriteInfo($"Logs: {_logger.GetLogDirectory()}");
        }

        // Initialize profile manager
        var profileManager = new ProfileManager();
        WriteInfo($"Profile: {profileManager.GetProfilePath()}");

        // Initialize adaptive engine
        var adaptiveEngine = new AdaptiveEngine(profileManager);
        adaptiveEngine.OnLog += msg => WriteInfo($"[Adaptive] {msg}");
        adaptiveEngine.OnTechniqueChanged += t => _statusLine?.SetTechnique(t.ToString());

        await adaptiveEngine.InitializeAsync();

        // Initialize status line
        if (!noStatus && !_verbose)
        {
            _statusLine = new StatusLine();
            _statusLine.SetTechnique(adaptiveEngine.CurrentTechnique.ToString());
        }

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            // Unblock the blocking Recv() call immediately
            _handle?.Shutdown();
        };

        try
        {
            await RunAsync(adaptiveEngine, cts.Token);
        }
        catch (Exception ex)
        {
            WriteError($"Fatal error: {ex.Message}");
            _logger?.Error($"Fatal error: {ex}");
            return 1;
        }
        finally
        {
            _statusLine?.Dispose();

            if (_logger != null)
            {
                _logger.Info("ZeroDivert stopped");
                await _logger.DisposeAsync();
            }
        }

        WriteSuccess("\nShutdown complete.");
        return 0;
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

        if (_statusLine != null)
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
            if (_statusLine != null)
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

                _statusLine?.UpdateStats(packetsTotal, packetsModified, discordPackets, _bytesProcessed);

                // Analyze packet
                var context = PacketInspector.Analyze(packet, addr.Outbound);

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
                        _statusLine?.SetStatus($"TLS -> {sni}");

                        if (_verbose)
                        {
                            _statusLine?.WriteLine($"[Discord] ClientHello -> {sni}");
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
                        _statusLine?.WriteLine($"[Discord] UDP Voice -> :{context.DstPort}");
                    }
                }

                if (shouldProcess && addr.Outbound)
                {
                    // Apply DPI bypass
                    var results = engine.ProcessPacket(packet, context);

                    if (_verbose && results.Count > 0)
                    {
                        _statusLine?.WriteLine($"[Bypass] {results.Count} packets, Orig={packet.Length}B, New={results[0].Data.Length}B");
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

            _logger?.LogStats(packetsTotal, discordPackets, packetsModified, _bytesProcessed);
        }
    }

    private static void PrintBanner()
    {
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine(@"
  _____              ____  _                _
 |__  /___ _ __ ___ |  _ \(_)_   _____ _ __| |_
   / // _ \ '__/ _ \| | | | \ \ / / _ \ '__| __|
  / /|  __/ | | (_) | |_| | |\ V /  __/ |  | |_
 /____\___|_|  \___/|____/|_| \_/ \___|_|   \__|

        Discord DPI Bypass Tool v1.0.0
");
        System.Console.ResetColor();
    }

    private static void WriteInfo(string message)
    {
        if (_statusLine != null)
        {
            _statusLine.WriteLine($"[*] {message}");
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Blue;
            System.Console.Write("[*] ");
            System.Console.ResetColor();
            System.Console.WriteLine(message);
        }
    }

    private static void WriteSuccess(string message)
    {
        if (_statusLine != null)
        {
            _statusLine.WriteLine($"[+] {message}");
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("[+] ");
            System.Console.ResetColor();
            System.Console.WriteLine(message);
        }
    }

    private static void WriteError(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.Write("[-] ");
        System.Console.ResetColor();
        System.Console.WriteLine(message);
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
