using ZeroDivert.Core.Adaptive;
using ZeroDivert.Core.Bypass;
using ZeroDivert.Core.Config;
using ZeroDivert.Core.Detection;
using static ZeroDivert.Tests.TlsTestPackets;

namespace ZeroDivert.Tests;

public class BypassStrategyTests
{
    [Fact]
    public void TestClientHello_IsRecognizedAsDiscord()
    {
        var packet = TcpSegment(ClientHello());
        var context = Context(packet);

        Assert.True(context.IsClientHello);
        Assert.Equal("gateway.discord.gg", PacketInspector.ExtractSni(PayloadOf(packet)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoodbyeDpi_SendsDecoyThenTailThenTwoByteHead(bool ipv6)
    {
        var hello = ClientHello(paddingLength: 1200);
        var packet = TcpSegment(hello, ipv6);

        var results = new GoodbyeDpiStrategy().Process(packet, Context(packet));

        Assert.Equal(3, results.Count);
        var (decoy, tail, head) = (results[0].Data, results[1].Data, results[2].Data);

        // 1. Decoy: GoodbyeDPI's www.w3.org ClientHello, TTL 5, same seq/ack as the real segment.
        Assert.Equal(GoodbyeDpiStrategy.DefaultFakeTtl, Ttl(decoy));
        Assert.Equal(FakeClientHello.Bytes.ToArray(), PayloadOf(decoy));
        Assert.Equal("www.w3.org", PacketInspector.ExtractSni(PayloadOf(decoy)));
        Assert.Equal(Seq, SeqOf(decoy));
        Assert.Equal(Ack, AckOf(decoy));

        // 2. Tail first (reverse order), starting 2 bytes in.
        Assert.Equal(Seq + 2, SeqOf(tail));
        Assert.Equal(hello[2..], PayloadOf(tail));

        // 3. Then the 2-byte head.
        Assert.Equal(Seq, SeqOf(head));
        Assert.Equal(hello[..2], PayloadOf(head));

        // Real segments keep the client's TTL; every IP length field matches its packet.
        Assert.Equal(ClientTtl, Ttl(tail));
        Assert.Equal(ClientTtl, Ttl(head));
        Assert.All(results, r => Assert.Equal(r.Data.Length, IpLengthField(r.Data)));
        Assert.All(results, r => Assert.True(r.RecalcChecksum));
    }

    [Fact]
    public void GoodbyeDpi_UsesConfiguredTtlAndFragmentSize()
    {
        var hello = ClientHello();
        var packet = TcpSegment(hello);

        var results = new GoodbyeDpiStrategy(fakeTtl: 7, fragmentSize: 40).Process(packet, Context(packet));

        Assert.Equal(7, Ttl(results[0].Data));
        Assert.Equal(hello[..40], PayloadOf(results[2].Data));
        Assert.Equal(Seq + 40, SeqOf(results[1].Data));
    }

    [Fact]
    public void GoodbyeDpi_LeavesNonClientHelloAlone()
    {
        var packet = TcpSegment([0x17, 0x03, 0x03, 0x00, 0x10, 1, 2, 3]);
        var results = new GoodbyeDpiStrategy().Process(packet, Context(packet));

        Assert.Equal(packet, Assert.Single(results).Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FakeTtl_DecoyOutlivesTheHomeRouter_ThenRealPacketUnchanged(bool ipv6)
    {
        var packet = TcpSegment(ClientHello(), ipv6);

        var results = new FakeTtlStrategy().Process(packet, Context(packet));

        Assert.Equal(2, results.Count);
        Assert.Equal(5, Ttl(results[0].Data));
        Assert.Equal(FakeClientHello.Bytes.ToArray(), PayloadOf(results[0].Data));
        Assert.Equal(packet, results[1].Data);
    }

    [Fact]
    public void TcpFragmentation_FromProfile_FragmentsOnlyTheStart()
    {
        // A ~1.8 KB post-quantum-sized ClientHello. Split entirely into 2-byte
        // segments it used to become ~900 packets when loaded from a saved profile.
        var hello = ClientHello(paddingLength: 1500);
        var packet = TcpSegment(hello);
        var legacyProfile = new StrategyConfig { TcpFragment = { FragmentSize = 2, FragmentFirstBytes = 0 } };

        var strategy = AdaptiveEngine.CreateStrategy(BypassTechnique.TcpFragmentation, legacyProfile);
        var results = strategy.Process(packet, Context(packet));

        Assert.Equal(26, results.Count); // 25 two-byte pieces of the first 50 bytes + the rest
        Assert.Equal(hello, results.SelectMany(r => PayloadOf(r.Data)).ToArray());
        Assert.Equal(Seq + 50, SeqOf(results[^1].Data));
    }

    [Fact]
    public void LegacyFakeTtlOfOne_IsReplacedByAUsableTtl()
    {
        var packet = TcpSegment(ClientHello());
        var legacyProfile = new StrategyConfig { FakeTtl = { FakeTtl = 1 } };

        var results = AdaptiveEngine.CreateStrategy(BypassTechnique.FakeTtl, legacyProfile).Process(packet, Context(packet));

        Assert.Equal(FakeTtlStrategy.DefaultFakeTtl, Ttl(results[0].Data));
    }

    [Fact]
    public void Desync_Disorder_SendsSecondPartFirst()
    {
        var hello = ClientHello();
        var packet = TcpSegment(hello);

        var results = new DesyncStrategy(DesyncMode.Disorder, splitPosition: 3).Process(packet, Context(packet));

        Assert.Equal(2, results.Count);
        Assert.Equal(Seq + 3, SeqOf(results[0].Data));
        Assert.Equal(hello[..3], PayloadOf(results[1].Data));
    }

    [Fact]
    public void Rotation_EndsWithGoodbyeDpi_AndHasNoCombined()
    {
        Assert.Equal(BypassTechnique.GoodbyeDpi, AdaptiveEngine.RotationOrder[^1]);
        Assert.DoesNotContain(BypassTechnique.Combined, AdaptiveEngine.RotationOrder);
    }
}

public class PartialClientHelloTests
{
    [Fact]
    public void RecordContinuingInNextSegment_IsDetectedAsPartial()
    {
        // A ~1.8 KB ClientHello whose first TCP segment carries only 1400 bytes;
        // the capture loop counts it as unreadable when its SNI isn't in there.
        var whole = TlsTestPackets.ClientHello(paddingLength: 1500);

        Assert.False(PacketInspector.IsPartialClientHello(TlsTestPackets.ClientHello()));
        Assert.False(PacketInspector.IsPartialClientHello(whole));
        Assert.True(PacketInspector.IsPartialClientHello(whole[..1400]));
    }
}
