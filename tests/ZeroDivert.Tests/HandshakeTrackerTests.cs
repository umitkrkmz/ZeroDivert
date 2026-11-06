using ZeroDivert.Core.Adaptive;

namespace ZeroDivert.Tests;

public class HandshakeTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const ushort Port = 50123;
    private const uint HelloSeq = 1000;
    private const int RecordLength = 1795; // ~1.8 KB post-quantum ClientHello

    [Fact]
    public void ClientDataPastTheClientHello_IsSuccess()
    {
        var tracker = new HandshakeTracker();
        tracker.ClientHelloSent(Port, HelloSeq, RecordLength, T0);

        var afterHello = HelloSeq + 5 + RecordLength;
        Assert.Equal(HandshakeOutcome.Succeeded, tracker.OutboundData(Port, afterHello));
        Assert.Equal(0, tracker.PendingCount);
    }

    [Fact]
    public void SecondSegmentOfASplitClientHello_IsNotSuccess()
    {
        var tracker = new HandshakeTracker();
        tracker.ClientHelloSent(Port, HelloSeq, RecordLength, T0);

        // The OS split the 1.8 KB ClientHello: the continuation starts inside it.
        Assert.Equal(HandshakeOutcome.None, tracker.OutboundData(Port, HelloSeq + 1440));
        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public void SequenceWraparound_StillDetectsProgress()
    {
        var tracker = new HandshakeTracker();
        var seq = uint.MaxValue - 100;
        tracker.ClientHelloSent(Port, seq, RecordLength, T0);

        var afterHello = unchecked(seq + 5 + RecordLength);
        Assert.True(afterHello < seq);
        Assert.Equal(HandshakeOutcome.Succeeded, tracker.OutboundData(Port, afterHello));
    }

    [Fact]
    public void InboundReset_IsFailureOnlyForPendingConnection()
    {
        var tracker = new HandshakeTracker();
        tracker.ClientHelloSent(Port, HelloSeq, RecordLength, T0);

        Assert.Equal(HandshakeOutcome.None, tracker.InboundReset(Port + 1));
        Assert.Equal(HandshakeOutcome.Reset, tracker.InboundReset(Port));
        Assert.Equal(HandshakeOutcome.None, tracker.InboundReset(Port));
    }

    [Fact]
    public void NoProgress_TimesOutOnce()
    {
        var tracker = new HandshakeTracker();
        tracker.ClientHelloSent(Port, HelloSeq, RecordLength, T0);

        Assert.Equal(0, tracker.TakeTimedOut(T0 + TimeSpan.FromSeconds(5)));
        Assert.Equal(1, tracker.TakeTimedOut(T0 + HandshakeTracker.Timeout + TimeSpan.FromSeconds(1)));
        Assert.Equal(0, tracker.TakeTimedOut(T0 + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void UntrackedConnections_AreIgnored()
    {
        var tracker = new HandshakeTracker();
        Assert.Equal(HandshakeOutcome.None, tracker.OutboundData(Port, HelloSeq));
    }
}
