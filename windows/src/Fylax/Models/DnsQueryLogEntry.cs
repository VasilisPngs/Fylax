namespace Fylax.Models;

public sealed class DnsQueryLogEntry
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    public string Host { get; init; } = "";
    public string Type { get; init; } = "";
    public bool Blocked { get; init; }
}
