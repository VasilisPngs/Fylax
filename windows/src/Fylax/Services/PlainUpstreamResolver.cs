using System.Net;
using System.Net.Sockets;
using System.IO;

namespace Fylax.Services;

public sealed class PlainUpstreamResolver : IUpstreamResolver
{
    private readonly IPEndPoint _endpoint;

    public PlainUpstreamResolver(IPAddress address)
    {
        _endpoint = new IPEndPoint(address, 53);
    }

    public async Task<byte[]> ResolveAsync(byte[] query, int length, bool preferTcp, CancellationToken cancellationToken)
    {
        var request = query.AsMemory(0, length).ToArray();
        if (!preferTcp)
        {
            var udpResponse = await ResolveOverUdpAsync(request, cancellationToken);
            if (udpResponse is not null && !DnsMessage.IsTruncated(udpResponse, udpResponse.Length))
            {
                return udpResponse;
            }
        }
        return await ResolveOverTcpAsync(request, cancellationToken);
    }

    private async Task<byte[]?> ResolveOverUdpAsync(byte[] request, CancellationToken cancellationToken)
    {
        using var client = new UdpClient(_endpoint.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        client.Connect(_endpoint);
        await client.SendAsync(request, timeout.Token);
        try
        {
            var result = await client.ReceiveAsync(timeout.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<byte[]> ResolveOverTcpAsync(byte[] request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(_endpoint.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await client.ConnectAsync(_endpoint.Address, _endpoint.Port, timeout.Token);
        await using var stream = client.GetStream();
        var prefixed = new byte[request.Length + 2];
        prefixed[0] = (byte)(request.Length >> 8);
        prefixed[1] = (byte)(request.Length & 0xFF);
        Buffer.BlockCopy(request, 0, prefixed, 2, request.Length);
        await stream.WriteAsync(prefixed, timeout.Token);
        var lengthPrefix = new byte[2];
        await stream.ReadExactlyAsync(lengthPrefix, timeout.Token);
        var responseLength = (lengthPrefix[0] << 8) | lengthPrefix[1];
        if (responseLength <= 0 || responseLength > 65535)
        {
            throw new IOException("Invalid DNS TCP response length.");
        }
        var response = new byte[responseLength];
        await stream.ReadExactlyAsync(response, timeout.Token);
        return response;
    }
}
