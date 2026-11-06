using System.Net;
using System.Net.Http.Headers;

namespace ZeroDivert.Core.Dns;

/// <summary>
/// Minimal DNS-over-HTTPS client: RFC 8484 wire format over POST
/// (application/dns-message), which every DoH server supports. The JSON API is
/// deliberately not used - only Cloudflare serves it at /dns-query; Google and
/// Quad9 silently return nothing.
/// </summary>
public sealed class DoHClient : IDisposable
{
    public const string DefaultServerUrl = "https://1.1.1.1/dns-query";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public DoHClient(string serverUrl = DefaultServerUrl)
    {
        ServerUrl = serverUrl;
    }

    public string ServerUrl { get; }

    /// <summary>
    /// Sends a DNS message as-is and returns the wire-format response, or null on any
    /// failure. Nothing is parsed or re-encoded, so every query type works.
    /// </summary>
    public async Task<byte[]?> QueryWireAsync(ReadOnlyMemory<byte> query, CancellationToken ct = default)
    {
        try
        {
            using var content = new ReadOnlyMemoryContent(query);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

            // HTTP/2 when offered: some servers (Quad9) reject HTTP/1.1 outright, and
            // one multiplexed connection suits a stream of small queries.
            using var request = new HttpRequestMessage(HttpMethod.Post, ServerUrl)
            {
                Content = content,
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            return body.Length >= 12 ? body : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
