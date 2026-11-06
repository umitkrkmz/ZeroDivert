namespace ZeroDivert.Core.Adaptive;

/// <summary>
/// Decides whether a Discord TLS handshake made it past DPI, using only packets the
/// capture filter actually sees: our own outbound traffic plus inbound RSTs.
///
/// After a ClientHello is sent, the connection is pending until one of:
/// - the client sends data beyond the end of the ClientHello (its Finished /
///   ChangeCipherSpec / application data) — it can only do that after the server
///   answered, so the handshake got through: <see cref="HandshakeOutcome.Succeeded"/>;
/// - an inbound RST arrives on that connection (DPI reset it): <see cref="HandshakeOutcome.Reset"/>;
/// - nothing of the above within <see cref="Timeout"/> (DPI silently dropped it):
///   reported by <see cref="TakeTimedOut"/>.
///
/// Progress is judged by TCP sequence number, not by "any later packet", so the
/// second segment of a ClientHello too big for one segment (modern post-quantum
/// key shares push it to ~1.8 KB) isn't mistaken for a server response.
/// Not thread-safe: used from the single capture loop.
/// </summary>
public sealed class HandshakeTracker
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly Dictionary<ushort, (DateTime SentAt, uint EndSeq)> _pending = [];

    public int PendingCount => _pending.Count;

    /// <param name="localPort">Our source port of the connection.</param>
    /// <param name="seq">TCP sequence number of the segment carrying the ClientHello's start.</param>
    /// <param name="tlsRecordLength">Length field of the TLS record header (excludes its 5 bytes).</param>
    public void ClientHelloSent(ushort localPort, uint seq, int tlsRecordLength, DateTime now) =>
        _pending[localPort] = (now, unchecked(seq + 5u + (uint)tlsRecordLength));

    /// <summary>Call for every outbound TCP segment that carries payload.</summary>
    public HandshakeOutcome OutboundData(ushort localPort, uint seq)
    {
        if (!_pending.TryGetValue(localPort, out var hello)) return HandshakeOutcome.None;

        // Signed difference handles sequence-number wraparound.
        if (unchecked((int)(seq - hello.EndSeq)) < 0) return HandshakeOutcome.None;

        _pending.Remove(localPort);
        return HandshakeOutcome.Succeeded;
    }

    /// <summary>Call for an inbound RST; <paramref name="localPort"/> is its destination port.</summary>
    public HandshakeOutcome InboundReset(ushort localPort) =>
        _pending.Remove(localPort) ? HandshakeOutcome.Reset : HandshakeOutcome.None;

    /// <summary>Removes and counts handshakes that saw no progress within <see cref="Timeout"/>.</summary>
    public int TakeTimedOut(DateTime now)
    {
        var cutoff = now - Timeout;
        var expired = _pending.Where(p => p.Value.SentAt < cutoff).Select(p => p.Key).ToList();
        foreach (var port in expired)
        {
            _pending.Remove(port);
        }

        return expired.Count;
    }
}

public enum HandshakeOutcome
{
    None,
    Succeeded,
    Reset
}
