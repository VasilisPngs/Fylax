namespace Fylax.Services;

public sealed class DnsCache
{
    private const int MinTtlSeconds = 300;
    private const int MaxTtlSeconds = 86400;
    private const int StaleTtlSeconds = 10;
    private const int MaxEntries = 4096;
    private static readonly TimeSpan StaleGrace = TimeSpan.FromHours(24);

    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<Node>> _map = new();
    private readonly LinkedList<Node> _lru = new();

    public bool TryGet(string key, ushort id, out byte[] response, out bool stale)
    {
        response = Array.Empty<byte>();
        stale = false;
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                return false;
            }
            var now = DateTimeOffset.UtcNow;
            if (now >= node.Value.Expiry + StaleGrace)
            {
                _lru.Remove(node);
                _map.Remove(key);
                return false;
            }
            _lru.Remove(node);
            _lru.AddFirst(node);
            var clone = (byte[])node.Value.Response.Clone();
            stale = now >= node.Value.Expiry;
            if (stale)
            {
                DnsMessage.ClampAnswerTtls(clone, clone.Length, StaleTtlSeconds, StaleTtlSeconds);
            }
            DnsMessage.SetId(clone, id);
            response = clone;
            return true;
        }
    }

    public byte[]? Set(string key, byte[] response, int length)
    {
        if (length < 12)
        {
            return null;
        }
        var rcode = response[3] & 0x0F;
        var ancount = (response[6] << 8) | response[7];
        if (rcode != 0 || ancount <= 0 || DnsMessage.IsTruncated(response, length))
        {
            return null;
        }
        var stored = response.AsMemory(0, length).ToArray();
        var ttl = DnsMessage.ClampAnswerTtls(stored, stored.Length, MinTtlSeconds, MaxTtlSeconds);
        if (ttl is null || ttl <= 0)
        {
            return null;
        }
        var expiry = DateTimeOffset.UtcNow.AddSeconds(ttl.Value);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = new Node(key, stored, expiry);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<Node>(new Node(key, stored, expiry));
                _lru.AddFirst(node);
                _map[key] = node;
                if (_map.Count > MaxEntries && _lru.Last is { } tail)
                {
                    _lru.RemoveLast();
                    _map.Remove(tail.Value.Key);
                }
            }
        }
        return stored;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    private readonly record struct Node(string Key, byte[] Response, DateTimeOffset Expiry);
}
