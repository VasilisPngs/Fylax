namespace Fylax.Models;

public enum DnsMode
{
    System,
    Custom
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed class BlockListItem
{
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
}

public sealed class AppSettings
{
    public List<BlockListItem> Lists { get; set; } = new();
    public List<string> Allowed { get; set; } = new();
    public List<string> BlockedManual { get; set; } = new();
    public DnsMode DnsMode { get; set; } = DnsMode.System;
    public string DnsPrimary { get; set; } = "";
    public string DnsSecondary { get; set; } = "";
    public bool DohEnabled { get; set; } = false;
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public IReadOnlyList<string> EnabledUrls()
    {
        return Lists.Where(item => item.Enabled).Select(item => item.Url).ToList();
    }
}
