using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Fylax.Models;
using System.IO;

namespace Fylax.Services;

public sealed class DnsProxyService
{
    private const int UdpResponseLimit = 512;

    private readonly object _sync = new();
    private readonly List<UdpClient> _udpListeners = new();
    private readonly List<TcpListener> _tcpListeners = new();
    private readonly List<Task> _tasks = new();
    private readonly DnsCache _cache = new();
    private readonly ConcurrentQueue<DnsQueryLogEntry> _log = new();
    private readonly ConcurrentDictionary<string, byte> _refreshing = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private IUpstreamResolver? _upstream;
    private volatile DomainMatcher? _matcher;
    private int _totalCount;
    private int _blockedCount;
    private int _logCount;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _udpListeners.Count > 0 || _tcpListeners.Count > 0;
            }
        }
    }

    public void UpdateRules(DomainMatcher matcher)
    {
        _matcher = matcher;
    }

    public void UpdateUpstream(IUpstreamResolver upstream)
    {
        _upstream = upstream;
    }

    public Task StartAsync(IUpstreamResolver upstream, DomainMatcher matcher)
    {
        lock (_sync)
        {
            if (_udpListeners.Count > 0 || _tcpListeners.Count > 0)
            {
                return Task.CompletedTask;
            }
            _totalCount = 0;
            _blockedCount = 0;
            _logCount = 0;
            _cache.Clear();
            while (_log.TryDequeue(out _)) { }
            _upstream = upstream;
            _matcher = matcher;
            _cancellationTokenSource = new CancellationTokenSource();
            var errors = new List<Exception>();
            var udp = new List<UdpClient>();
            var tcp = new List<TcpListener>();
            BindUdp(AddressFamily.InterNetwork, IPAddress.Loopback, udp, errors);
            BindUdp(AddressFamily.InterNetworkV6, IPAddress.IPv6Loopback, udp, errors);
            BindTcp(IPAddress.Loopback, tcp, errors);
            BindTcp(IPAddress.IPv6Loopback, tcp, errors);
            if (udp.Count == 0 && tcp.Count == 0)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.Message)));
            }
            var token = _cancellationTokenSource.Token;
            foreach (var listener in udp)
            {
                _udpListeners.Add(listener);
                _tasks.Add(Task.Run(() => UdpLoopAsync(listener, token)));
            }
            foreach (var listener in tcp)
            {
                _tcpListeners.Add(listener);
                _tasks.Add(Task.Run(() => TcpLoopAsync(listener, token)));
            }
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        List<UdpClient> udp;
        List<TcpListener> tcp;
        List<Task> tasks;
        CancellationTokenSource? cancellationTokenSource;
        lock (_sync)
        {
            udp = _udpListeners.ToList();
            tcp = _tcpListeners.ToList();
            tasks = _tasks.ToList();
            cancellationTokenSource = _cancellationTokenSource;
            _udpListeners.Clear();
            _tcpListeners.Clear();
            _tasks.Clear();
            _cancellationTokenSource = null;
        }
        if (cancellationTokenSource is not null)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
        foreach (var listener in udp)
        {
            listener.Close();
            listener.Dispose();
        }
        foreach (var listener in tcp)
        {
            listener.Stop();
        }
        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }
        _cache.Clear();
    }

    public async Task RunSelfTestAsync()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("DNS proxy is not running.");
        }
        using var client = new UdpClient(AddressFamily.InterNetwork);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var query = DnsMessage.CreateQuery("example.com");
        await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Loopback, 53));
        await client.ReceiveAsync(timeout.Token);
    }

    private void BindUdp(AddressFamily addressFamily, IPAddress address, List<UdpClient> started, List<Exception> errors)
    {
        try
        {
            var listener = new UdpClient(addressFamily);
            if (addressFamily == AddressFamily.InterNetworkV6)
            {
                listener.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            }
            listener.Client.Bind(new IPEndPoint(address, 53));
            started.Add(listener);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private void BindTcp(IPAddress address, List<TcpListener> started, List<Exception> errors)
    {
        try
        {
            var listener = new TcpListener(address, 53);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            }
            listener.Start();
            started.Add(listener);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private async Task UdpLoopAsync(UdpClient listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var packet = await listener.ReceiveAsync(cancellationToken);
                _ = HandleUdpAsync(listener, packet, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private async Task HandleUdpAsync(UdpClient listener, UdpReceiveResult packet, CancellationToken cancellationToken)
    {
        var buffer = packet.Buffer;
        var question = DnsMessage.ReadQuestion(buffer, buffer.Length);
        try
        {
            var response = await ResolveAsync(buffer, buffer.Length, question, false, cancellationToken);
            if (response.Length > UdpResponseLimit && !DnsMessage.IsTruncated(response, response.Length))
            {
                response = DnsMessage.CreateTruncatedResponse(buffer, buffer.Length);
            }
            await listener.SendAsync(response, response.Length, packet.RemoteEndPoint);
        }
        catch
        {
            try
            {
                var failure = DnsMessage.CreateFailureResponse(buffer, buffer.Length);
                await listener.SendAsync(failure, failure.Length, packet.RemoteEndPoint);
            }
            catch
            {
            }
        }
    }

    private async Task TcpLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleTcpAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    private async Task HandleTcpAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var lengthPrefix = new byte[2];
                    try
                    {
                        await stream.ReadExactlyAsync(lengthPrefix, cancellationToken);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                    var length = (lengthPrefix[0] << 8) | lengthPrefix[1];
                    if (length <= 0)
                    {
                        break;
                    }
                    var buffer = new byte[length];
                    await stream.ReadExactlyAsync(buffer, cancellationToken);
                    var question = DnsMessage.ReadQuestion(buffer, length);
                    byte[] response;
                    try
                    {
                        response = await ResolveAsync(buffer, length, question, true, cancellationToken);
                    }
                    catch
                    {
                        response = DnsMessage.CreateFailureResponse(buffer, length);
                    }
                    var prefixed = new byte[response.Length + 2];
                    prefixed[0] = (byte)(response.Length >> 8);
                    prefixed[1] = (byte)(response.Length & 0xFF);
                    Buffer.BlockCopy(response, 0, prefixed, 2, response.Length);
                    await stream.WriteAsync(prefixed, cancellationToken);
                }
            }
        }
        catch
        {
        }
    }

    private async Task<byte[]> ResolveAsync(byte[] query, int length, DnsMessage.Question question, bool preferTcp, CancellationToken cancellationToken)
    {
        var matcher = _matcher;
        var upstream = _upstream;
        if (matcher is null || upstream is null)
        {
            return DnsMessage.CreateFailureResponse(query, length);
        }
        Interlocked.Increment(ref _totalCount);
        var typeName = DnsMessage.TypeName(question.Type);
        if (matcher.IsBlocked(question.Name))
        {
            Interlocked.Increment(ref _blockedCount);
            RaiseLog(question.Name, typeName, true);
            return DnsMessage.CreateSinkholeResponse(query, length);
        }
        var key = $"{question.Name}|{question.Type}";
        var id = DnsMessage.GetId(query);
        if (_cache.TryGet(key, id, out var cached, out var stale))
        {
            RaiseLog(question.Name, typeName, false);
            if (stale)
            {
                TriggerRefresh(key, query, length, preferTcp);
            }
            return cached;
        }
        try
        {
            var response = await upstream.ResolveAsync(query, length, preferTcp, cancellationToken);
            if (DnsMessage.ContainsBlockedCname(response, response.Length, matcher.IsBlocked))
            {
                Interlocked.Increment(ref _blockedCount);
                RaiseLog(question.Name, typeName, true);
                return DnsMessage.CreateSinkholeResponse(query, length);
            }
            RaiseLog(question.Name, typeName, false);
            if (response.Length >= 12)
            {
                DnsMessage.SetId(response, id);
                var boosted = _cache.Set(key, response, response.Length);
                return boosted ?? response;
            }
            return response;
        }
        catch
        {
            RaiseLog(question.Name, typeName, false);
            return DnsMessage.CreateFailureResponse(query, length);
        }
    }

    private void TriggerRefresh(string key, byte[] query, int length, bool preferTcp)
    {
        if (!_refreshing.TryAdd(key, 0))
        {
            return;
        }
        var snapshot = query.AsMemory(0, length).ToArray();
        _ = Task.Run(async () =>
        {
            try
            {
                var matcher = _matcher;
                var upstream = _upstream;
                if (matcher is null || upstream is null)
                {
                    return;
                }
                var fresh = await upstream.ResolveAsync(snapshot, snapshot.Length, preferTcp, CancellationToken.None);
                if (fresh.Length >= 12 && !DnsMessage.ContainsBlockedCname(fresh, fresh.Length, matcher.IsBlocked))
                {
                    _cache.Set(key, fresh, fresh.Length);
                }
            }
            catch
            {
            }
            finally
            {
                _refreshing.TryRemove(key, out _);
            }
        });
    }

    private void RaiseLog(string host, string type, bool blocked)
    {
        var entry = new DnsQueryLogEntry
        {
            Time = DateTimeOffset.Now,
            Host = string.IsNullOrWhiteSpace(host) ? "unknown" : host,
            Type = type,
            Blocked = blocked
        };
        _log.Enqueue(entry);
        var count = Interlocked.Increment(ref _logCount);
        while (count > 2000 && _log.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _logCount);
        }
    }

    public IReadOnlyList<DnsQueryLogEntry> Snapshot()
    {
        return _log.Reverse().ToList();
    }

    public (int Total, int Blocked) Counts()
    {
        return (_totalCount, _blockedCount);
    }

    public void ClearLog()
    {
        while (_log.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _logCount, 0);
        _totalCount = 0;
        _blockedCount = 0;
    }
}
