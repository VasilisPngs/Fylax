using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Fylax.Services;

public sealed class DohUpstreamResolver : IUpstreamResolver
{
    private static readonly MediaTypeHeaderValue DnsMediaType = new("application/dns-message");
    private static readonly IPEndPoint BootstrapServer = new(IPAddress.Parse("1.1.1.1"), 53);

    private readonly ConcurrentDictionary<string, IPAddress> _hostCache = new();
    private readonly HttpClient _httpClient;
    private readonly string _url;

    public DohUpstreamResolver(string url)
    {
        _url = url;
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectCallback = ConnectAsync
        };
        _httpClient = new HttpClient(handler, true)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
    }

    public async Task<byte[]> ResolveAsync(byte[] query, int length, bool preferTcp, CancellationToken cancellationToken)
    {
        var request = query.AsMemory(0, length).ToArray();
        using var message = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        var content = new ByteArrayContent(request);
        content.Headers.ContentType = DnsMediaType;
        message.Content = content;
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var address = await ResolveHostAsync(context.DnsEndPoint.Host, cancellationToken);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<IPAddress> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal;
        }
        if (_hostCache.TryGetValue(host, out var cached))
        {
            return cached;
        }
        using var client = new UdpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var query = DnsMessage.CreateQuery(host);
        client.Connect(BootstrapServer);
        await client.SendAsync(query, timeout.Token);
        var result = await client.ReceiveAsync(timeout.Token);
        var rdata = DnsMessage.TryGetFirstAddress(result.Buffer, result.Buffer.Length, 1);
        if (rdata is null || rdata.Length != 4)
        {
            throw new IOException($"Bootstrap resolution failed for {host}.");
        }
        var address = new IPAddress(rdata);
        _hostCache[host] = address;
        return address;
    }
}
