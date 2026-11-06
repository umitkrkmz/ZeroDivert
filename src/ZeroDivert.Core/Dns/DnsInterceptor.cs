namespace ZeroDivert.Core.Dns;

/// <summary>
/// Sends one packet back into the network stack. <c>asInboundReply</c>: deliver it
/// to this machine as if it had arrived from the network (the DNS reply); otherwise
/// send it on outbound unchanged (the original query, when DoH failed). The capture
/// loop builds this per query, closing over a copy of that packet's WinDivert
/// address, and it may be called from any thread.
/// </summary>
public delegate void PacketSender(byte[] packet, bool asInboundReply);

/// <summary>
/// Answers this machine's plain DNS queries (UDP 53, IPv4 and IPv6, any app) over
/// DNS-over-HTTPS instead of letting them reach the network, where ISPs poison them
/// (e.g. discord.com -> a block-page IP) - typically through a router's IPv6 DNS
/// that an encrypted IPv4 DNS setting in Windows never covered. System DNS settings
/// are not touched; stopping the engine restores normal behavior.
///
/// A query is taken out of the capture loop and answered asynchronously, so a DoH
/// round trip never stalls other traffic. If DoH fails, the original query is sent
/// on unchanged: DNS keeps working (unprotected) instead of breaking. Answers still
/// in flight when the interceptor is disposed are dropped.
/// </summary>
public sealed class DnsInterceptor : IDisposable
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

    // A UDP length field can't describe more; a real DNS response never gets close.
    private const int MaxDnsPayload = ushort.MaxValue - 48;

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]?>> _query;
    private readonly string? _passThroughName;
    private readonly DoHClient? _ownedClient;
    private readonly CancellationTokenSource _cts = new();
    private long _answered;
    private long _fallbacks;

    public event Action<string>? OnLog;

    public DnsInterceptor(string dohServerUrl)
        : this(null!, PassThroughNameFor(dohServerUrl))
    {
        _ownedClient = new DoHClient(dohServerUrl);
        _query = _ownedClient.QueryWireAsync;
    }

    internal DnsInterceptor(Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]?>> query, string? passThroughName)
    {
        _query = query;
        _passThroughName = passThroughName;
    }

    public long AnsweredViaDoH => Interlocked.Read(ref _answered);
    public long FellBackToPlainDns => Interlocked.Read(ref _fallbacks);

    /// <summary>
    /// Takes over <paramref name="packet"/> if it's an outbound DNS query. When this
    /// returns true the caller must NOT reinject the packet: it will be answered (or
    /// forwarded on DoH failure) through <paramref name="send"/>.
    /// </summary>
    public bool TryIntercept(ReadOnlySpan<byte> packet, bool isOutbound, PacketSender send)
    {
        if (!isOutbound || _cts.IsCancellationRequested) return false;
        if (!DnsPacket.TryParseQuery(packet, out var dnsOffset, out var queryName)) return false;

        // The DoH server's own hostname has to resolve normally, or the client would
        // wait on itself.
        if (_passThroughName != null && string.Equals(queryName, _passThroughName, StringComparison.OrdinalIgnoreCase))
            return false;

        _ = AnswerAsync(packet.ToArray(), dnsOffset, queryName!, send);
        return true;
    }

    internal async Task AnswerAsync(byte[] queryPacket, int dnsOffset, string queryName, PacketSender send)
    {
        byte[]? response = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(QueryTimeout);
            response = await _query(queryPacket.AsMemory(dnsOffset), timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or ObjectDisposedException)
        {
        }

        // Stopped while the query was in flight: the capture handle is closing, so
        // there is nothing to send through anymore.
        if (_cts.IsCancellationRequested) return;

        if (response != null && response.Length <= MaxDnsPayload)
        {
            send(DnsPacket.BuildResponse(queryPacket, response), asInboundReply: true);
            Interlocked.Increment(ref _answered);
            return;
        }

        send(queryPacket, asInboundReply: false);
        if (Interlocked.Increment(ref _fallbacks) == 1)
        {
            OnLog?.Invoke($"[DNS] DoH unreachable - sending plain DNS for {queryName} (and later failures) unprotected");
        }
    }

    internal static string? PassThroughNameFor(string dohServerUrl) =>
        Uri.TryCreate(dohServerUrl, UriKind.Absolute, out var uri) && uri.HostNameType == UriHostNameType.Dns
            ? uri.Host
            : null;

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _cts.Dispose();
        _ownedClient?.Dispose();
    }
}
