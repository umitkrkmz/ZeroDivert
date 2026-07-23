using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZeroDivert.Console;

public sealed record NetworkAdapterInfo(string Name, string Description, bool IsDhcp, IReadOnlyList<string> DnsServers);

/// <summary>
/// Reads and changes the DNS servers of local network adapters via `netsh`.
/// Requires administrator privileges (already enforced by the app as a whole).
/// </summary>
public static class DnsSettingsManager
{
    public static IReadOnlyList<NetworkAdapterInfo> ListAdapters()
    {
        var result = new List<NetworkAdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;

            var props = nic.GetIPProperties();
            var dnsServers = props.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();

            // GetIPv4Properties() throws (rather than returning null) when IPv4 isn't
            // actually configured on the adapter, even if Supports(IPv4) said yes.
            bool isDhcp;
            try
            {
                isDhcp = props.GetIPv4Properties()?.IsDhcpEnabled ?? true;
            }
            catch (NetworkInformationException)
            {
                isDhcp = true;
            }

            result.Add(new NetworkAdapterInfo(nic.Name, nic.Description, isDhcp, dnsServers));
        }

        return result;
    }

    public static (bool Success, string Message) SetDns(string adapterName, string primary, string? secondary)
    {
        var (code, _, error) = RunNetsh("interface", "ip", "set", "dns",
            $"name={adapterName}", "source=static", $"addr={primary}", "register=primary");

        if (code != 0)
        {
            return (false, string.IsNullOrWhiteSpace(error) ? $"netsh çıkış kodu {code}" : error);
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var (code2, _, error2) = RunNetsh("interface", "ip", "add", "dns",
                $"name={adapterName}", $"addr={secondary}", "index=2");

            if (code2 != 0)
            {
                return (false, string.IsNullOrWhiteSpace(error2) ? $"netsh çıkış kodu {code2}" : error2);
            }
        }

        return (true, "DNS ayarlandı.");
    }

    public static (bool Success, string Message) ResetToDhcp(string adapterName)
    {
        var (code, _, error) = RunNetsh("interface", "ip", "set", "dns", $"name={adapterName}", "source=dhcp");
        return code == 0
            ? (true, "DNS otomatik (DHCP) olarak ayarlandı.")
            : (false, string.IsNullOrWhiteSpace(error) ? $"netsh çıkış kodu {code}" : error);
    }

    private static (int ExitCode, string Output, string Error) RunNetsh(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
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
            return (-1, "", "netsh başlatılamadı");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        return (process.ExitCode, stdout, stderr);
    }
}
