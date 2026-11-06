using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ZeroDivert.Core.Dns;

namespace ZeroDivert.Console;

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    bool IsDhcp,
    bool SupportsIPv6,
    IReadOnlyList<string> Ipv4Dns,
    IReadOnlyList<string> Ipv6Dns);

/// <summary>A public resolver: its IPv4/IPv6 addresses and DoH template (null if unknown).</summary>
public sealed record DnsPreset(string Name, string[] Ipv4, string[] Ipv6, string? DohTemplate);

public sealed record DnsApplyResult(bool Success, string Message, IReadOnlyList<string> Warnings);

/// <param name="Succeeded">Windows resolved the test name at all.</param>
/// <param name="MatchesDoH">
/// null when either side had no answer; false when Windows' answer shares no address
/// with the DoH answer (typical of an ISP block page).
/// </param>
public sealed record DnsCheckResult(bool Succeeded, IReadOnlyList<IPAddress> SystemAddresses, bool? MatchesDoH);

/// <summary>
/// Reads and changes the DNS servers of local network adapters via `netsh`, IPv4
/// and IPv6, and turns on Windows 11's encrypted DNS for known resolvers.
/// Requires administrator privileges (already enforced by the app as a whole).
///
/// Why both families and encryption: on a Türk Telekom line, setting only IPv4
/// 1.1.1.1 left lookups going to the router's IPv6 DNS (block-page answers), and
/// plain UDP to 1.1.1.1:53 is blocked there - without encryption DNS stops working.
/// </summary>
public static class DnsSettingsManager
{
    public const string TestName = "discord.com";

    public static readonly DnsPreset Cloudflare = new(
        "Cloudflare",
        ["1.1.1.1", "1.0.0.1"],
        ["2606:4700:4700::1111", "2606:4700:4700::1001"],
        "https://cloudflare-dns.com/dns-query");

    public static readonly DnsPreset Google = new(
        "Google",
        ["8.8.8.8", "8.8.4.4"],
        ["2001:4860:4860::8888", "2001:4860:4860::8844"],
        "https://dns.google/dns-query");

    /// <summary>Windows 11 (build 22000+) has a DoH client; Windows 10 doesn't.</summary>
    public static bool SupportsEncryptedDns => Environment.OSVersion.Version.Build >= 22000;

    public static IReadOnlyList<NetworkAdapterInfo> ListAdapters()
    {
        var result = new List<NetworkAdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;

            var dns = nic.GetIPProperties().DnsAddresses;

            // GetIPv4Properties() throws (rather than returning null) when IPv4 isn't
            // actually configured on the adapter, even if Supports(IPv4) said yes.
            bool isDhcp;
            try
            {
                isDhcp = nic.GetIPProperties().GetIPv4Properties()?.IsDhcpEnabled ?? true;
            }
            catch (NetworkInformationException)
            {
                isDhcp = true;
            }

            result.Add(new NetworkAdapterInfo(
                nic.Name,
                nic.Description,
                isDhcp,
                nic.Supports(NetworkInterfaceComponent.IPv6),
                FormatAddresses(dns, AddressFamily.InterNetwork),
                FormatAddresses(dns, AddressFamily.InterNetworkV6)));
        }

        return result;
    }

    /// <summary>
    /// Windows can report the same server twice and link-local ones with a scope
    /// suffix ("fe80::1%13"); show each address once, without the suffix.
    /// </summary>
    internal static IReadOnlyList<string> FormatAddresses(IEnumerable<IPAddress> addresses, AddressFamily family) =>
        addresses
            .Where(a => a.AddressFamily == family)
            // ScopeId throws for IPv4 addresses, so only touch it for IPv6.
            .Select(a => (a.AddressFamily == AddressFamily.InterNetworkV6 && a.ScopeId != 0
                ? new IPAddress(a.GetAddressBytes())
                : a).ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static DnsApplyResult Apply(NetworkAdapterInfo adapter, DnsPreset preset)
    {
        var warnings = new List<string>();

        var error = SetServers("ip", adapter.Name, preset.Ipv4);
        if (error != null) return new DnsApplyResult(false, error, warnings);

        if (adapter.SupportsIPv6 && preset.Ipv6.Length > 0)
        {
            error = SetServers("ipv6", adapter.Name, preset.Ipv6);
            if (error != null) return new DnsApplyResult(false, $"IPv4 ayarlandı ama IPv6 ayarlanamadı: {error}", warnings);
        }
        else if (adapter.SupportsIPv6)
        {
            warnings.Add("Bu sunucu için IPv6 adresi girilmedi; IPv6 DNS sorguları eskisi gibi gidebilir.");
        }

        if (preset.DohTemplate == null)
        {
            if (SupportsEncryptedDns)
                warnings.Add("Bu sunucunun DoH adresi bilinmediği için şifreleme açılmadı.");
        }
        else if (SupportsEncryptedDns)
        {
            foreach (var server in preset.Ipv4.Concat(adapter.SupportsIPv6 ? preset.Ipv6 : []))
            {
                var encryptionError = EnableEncryption(server, preset.DohTemplate);
                if (encryptionError != null)
                    warnings.Add($"{server} için şifreleme açılamadı: {encryptionError}");
            }
        }
        else
        {
            warnings.Add("Windows 10'da şifreli DNS yok: sorgular düz metin gider. Düz DNS'in engellendiği hatlarda (ör. Türk Telekom'da 1.1.1.1:53) DNS çalışmayabilir; ZeroDivert'in DNS koruması (DoH) açıkken bu sorun olmaz.");
        }

        FlushDnsCache();
        return new DnsApplyResult(true, $"DNS {preset.Name} olarak ayarlandı.", warnings);
    }

    public static DnsApplyResult ResetToDhcp(NetworkAdapterInfo adapter)
    {
        var (code, _, error) = RunNetsh("interface", "ip", "set", "dnsservers", $"name={adapter.Name}", "source=dhcp");
        if (code != 0) return new DnsApplyResult(false, ErrorText(code, error), []);

        if (adapter.SupportsIPv6)
        {
            (code, _, error) = RunNetsh("interface", "ipv6", "set", "dnsservers", $"name={adapter.Name}", "source=dhcp");
            if (code != 0) return new DnsApplyResult(false, $"IPv4 geri alındı ama IPv6 alınamadı: {ErrorText(code, error)}", []);
        }

        FlushDnsCache();
        return new DnsApplyResult(true, "DNS otomatik (DHCP) olarak ayarlandı (IPv4 ve IPv6).", []);
    }

    /// <summary>
    /// Resolves <see cref="TestName"/> the way every app does (through Windows' DNS
    /// client, so whatever Windows really uses - including a "DNS for all Wi-Fi
    /// networks" setting that overrides per-network ones) and compares it with a DoH
    /// answer, which an ISP can't tamper with.
    /// </summary>
    public static async Task<DnsCheckResult> CheckResolutionAsync()
    {
        IPAddress[] system;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            system = await Dns.GetHostAddressesAsync(TestName, AddressFamily.InterNetwork, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new DnsCheckResult(false, [], null);
        }

        using var doh = new DoHClient();
        var response = await doh.QueryWireAsync(DnsMessage.BuildQuery(TestName, DnsMessage.TypeA));
        var reference = response == null ? [] : DnsMessage.ReadAddresses(response, DnsMessage.TypeA).Addresses;

        bool? matches = system.Length == 0 || reference.Length == 0 ? null : system.Intersect(reference).Any();
        return new DnsCheckResult(system.Length > 0, system, matches);
    }

    private static string? SetServers(string family, string adapterName, string[] servers)
    {
        var (code, _, error) = RunNetsh("interface", family, "set", "dnsservers",
            $"name={adapterName}", "source=static", $"address={servers[0]}", "register=primary", "validate=no");
        if (code != 0) return ErrorText(code, error);

        for (var i = 1; i < servers.Length; i++)
        {
            (code, _, error) = RunNetsh("interface", family, "add", "dnsservers",
                $"name={adapterName}", $"address={servers[i]}", $"index={i + 1}", "validate=no");
            if (code != 0) return ErrorText(code, error);
        }

        return null;
    }

    /// <summary>
    /// Same as Set-DnsClientDohServerAddress -AutoUpgrade $true -AllowFallbackToUdp $false:
    /// queries to this server always go over DoH. No UDP fallback on purpose - where
    /// plain DNS is blocked or poisoned, falling back would silently undo the point;
    /// the resolution check afterwards catches a server DoH can't reach.
    /// Cloudflare/Google are in Windows' built-in list already, so "set" usually works.
    /// </summary>
    private static string? EnableEncryption(string server, string dohTemplate)
    {
        string[] options = [$"server={server}", $"dohtemplate={dohTemplate}", "autoupgrade=yes", "udpfallback=no"];

        var (code, _, _) = RunNetsh(["dns", "set", "encryption", .. options]);
        if (code == 0) return null;

        var (addCode, _, addError) = RunNetsh(["dns", "add", "encryption", .. options]);
        return addCode == 0 ? null : ErrorText(addCode, addError);
    }

    private static void FlushDnsCache() => RunProcess("ipconfig", "/flushdns");

    private static string ErrorText(int code, string error) =>
        string.IsNullOrWhiteSpace(error) ? $"netsh çıkış kodu {code}" : error.Trim();

    private static (int ExitCode, string Output, string Error) RunNetsh(params string[] args) => RunProcess("netsh", args);

    private static (int ExitCode, string Output, string Error) RunProcess(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
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
            return (-1, "", $"{fileName} başlatılamadı");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        // netsh reports many errors on stdout with exit code 1.
        return (process.ExitCode, stdout, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }
}
