using System.Buffers.Binary;
using ZeroDivert.Core.Dns;
using static ZeroDivert.Tests.DnsTestPackets;

namespace ZeroDivert.Tests;

public class DnsInterceptorTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Task<byte[]?> EchoAsResponse(ReadOnlyMemory<byte> query)
    {
        var response = query.ToArray();
        response[2] |= 0x80; // QR
        return Task.FromResult<byte[]?>(response);
    }

    [Fact]
    public async Task AnswersOutboundQueryViaDoH_AsInboundReply()
    {
        byte[]? sentToDoH = null;
        using var interceptor = new DnsInterceptor((query, _) =>
        {
            sentToDoH = query.ToArray();
            return EchoAsResponse(query);
        }, passThroughName: null);
        var sender = new RecordingSender();

        Assert.True(interceptor.TryIntercept(QueryV6("discord.com"), isOutbound: true, sender.Send));
        await sender.FirstSend.Task.WaitAsync(Wait);

        Assert.Equal(DnsQuery("discord.com"), sentToDoH);
        var (packet, asInboundReply) = Assert.Single(sender.Sent);
        Assert.True(asInboundReply);
        Assert.Equal(QueryId, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(48)));
        Assert.Equal(1, interceptor.AnsweredViaDoH);
        Assert.Equal(0, interceptor.FellBackToPlainDns);
    }

    [Fact]
    public async Task DoHFailure_ForwardsOriginalQueryUnchanged()
    {
        using var interceptor = new DnsInterceptor((_, _) => Task.FromResult<byte[]?>(null), passThroughName: null);
        var sender = new RecordingSender();
        var query = QueryV4("discord.com");

        Assert.True(interceptor.TryIntercept(query, isOutbound: true, sender.Send));
        await sender.FirstSend.Task.WaitAsync(Wait);

        var (packet, asInboundReply) = Assert.Single(sender.Sent);
        Assert.False(asInboundReply);
        Assert.Equal(query, packet);
        Assert.Equal(1, interceptor.FellBackToPlainDns);
    }

    [Fact]
    public async Task DoHThrowing_StillForwardsOriginalQuery()
    {
        using var interceptor = new DnsInterceptor((_, _) => throw new HttpRequestException("blocked"), passThroughName: null);
        var sender = new RecordingSender();

        Assert.True(interceptor.TryIntercept(QueryV4("discord.com"), isOutbound: true, sender.Send));
        await sender.FirstSend.Task.WaitAsync(Wait);

        Assert.False(Assert.Single(sender.Sent).AsInboundReply);
    }

    [Fact]
    public async Task StoppingWhileAQueryIsInFlight_SendsNothing()
    {
        var release = new TaskCompletionSource();
        var interceptor = new DnsInterceptor(async (query, _) =>
        {
            await release.Task;
            return (await EchoAsResponse(query))!;
        }, passThroughName: null);
        var sender = new RecordingSender();

        Assert.True(interceptor.TryIntercept(QueryV4("discord.com"), isOutbound: true, sender.Send));
        interceptor.Dispose();          // e.g. Ctrl+C / tray "Durdur" while DoH is pending
        release.SetResult();
        await Task.Delay(200);

        Assert.Empty(sender.Sent);
        Assert.False(interceptor.TryIntercept(QueryV4("discord.com"), isOutbound: true, sender.Send));
    }

    [Fact]
    public void LeavesNonQueriesAndTheDoHServersOwnNameAlone()
    {
        using var interceptor = new DnsInterceptor((_, _) => Task.FromResult<byte[]?>(null), passThroughName: "cloudflare-dns.com");
        var sender = new RecordingSender();

        Assert.False(interceptor.TryIntercept(QueryV4("cloudflare-dns.com"), isOutbound: true, sender.Send));
        Assert.False(interceptor.TryIntercept(QueryV4("discord.com"), isOutbound: false, sender.Send));
        Assert.False(interceptor.TryIntercept(Udp(ClientV4, RouterV4, ClientPort, 443, DnsQuery("discord.com")), isOutbound: true, sender.Send));
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData("https://cloudflare-dns.com/dns-query", "cloudflare-dns.com")]
    [InlineData("https://dns.google/dns-query", "dns.google")]
    [InlineData("https://1.1.1.1/dns-query", null)]
    [InlineData("https://[2606:4700:4700::1111]/dns-query", null)]
    public void PassThroughName_OnlyForHostnameServers(string url, string? expected)
    {
        Assert.Equal(expected, DnsInterceptor.PassThroughNameFor(url));
    }

    // Real DoH servers over the network. Must pass with no network too, so a
    // failed request (null) is accepted; a successful one must be a real answer.
    // Includes Quad9, which rejects HTTP/1.1.
    [Theory]
    [InlineData("https://1.1.1.1/dns-query")]
    [InlineData("https://dns.google/dns-query")]
    [InlineData("https://dns.quad9.net/dns-query")]
    public async Task QueryWireAsync_RealServer_ReturnsAnswerForSameQuestion(string server)
    {
        using var client = new DoHClient(server);
        var response = await client.QueryWireAsync(DnsQuery("discord.com"));
        if (response == null) return;

        Assert.Equal(QueryId, BinaryPrimitives.ReadUInt16BigEndian(response));
        Assert.True((response[2] & 0x80) != 0, "QR bit set");
        Assert.Equal(0, response[3] & 0x0F); // NOERROR
        Assert.NotEmpty(DnsMessage.ReadAddresses(response, DnsMessage.TypeA).Addresses);
    }
}
