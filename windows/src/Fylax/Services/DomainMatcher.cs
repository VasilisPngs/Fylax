namespace Fylax.Services;

public sealed class DomainMatcher
{
    private readonly HashSet<string> _allowlist;
    private readonly HashSet<string> _blocklist;

    private DomainMatcher(HashSet<string> allowlist, HashSet<string> blocklist)
    {
        _allowlist = allowlist;
        _blocklist = blocklist;
    }

    public static DomainMatcher Create(IEnumerable<string> allowed, IEnumerable<string> manualBlocked, IEnumerable<string> remoteRules)
    {
        var allowlist = ParseRules(allowed);
        var blocklist = ParseRules(manualBlocked);
        foreach (var rule in remoteRules)
        {
            var domain = NormalizeRule(rule);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                blocklist.Add(domain);
            }
        }
        return new DomainMatcher(allowlist, blocklist);
    }

    public bool IsBlocked(string domain)
    {
        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }
        if (Matches(_allowlist, normalized))
        {
            return false;
        }
        return Matches(_blocklist, normalized);
    }

    private static HashSet<string> ParseRules(IEnumerable<string> rules)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var domain = NormalizeRule(rule);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                domains.Add(domain);
            }
        }
        return domains;
    }

    private static bool Matches(HashSet<string> rules, string domain)
    {
        var current = domain;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (rules.Contains(current))
            {
                return true;
            }
            var dot = current.IndexOf('.');
            if (dot < 0 || dot == current.Length - 1)
            {
                break;
            }
            current = current[(dot + 1)..];
        }
        return false;
    }

    private static string? NormalizeRule(string rule)
    {
        var value = rule.Trim();
        if (value.Length == 0 || value.StartsWith('!') || value.StartsWith('#') || value.StartsWith("@@", StringComparison.Ordinal))
        {
            return null;
        }
        if (value.Contains("##", StringComparison.Ordinal) || value.Contains("#@#", StringComparison.Ordinal) || value.Contains("#?#", StringComparison.Ordinal) || value.Contains("#$#", StringComparison.Ordinal) || value.Contains("#%#", StringComparison.Ordinal))
        {
            return null;
        }
        if (value.StartsWith('/'))
        {
            return null;
        }
        var hashIndex = value.IndexOf('#');
        if (hashIndex >= 0)
        {
            value = value[..hashIndex].Trim();
        }
        if (value.StartsWith("||", StringComparison.Ordinal))
        {
            value = value[2..];
        }
        var parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && IsAddressToken(parts[0]))
        {
            value = parts[1];
        }
        var endIndex = value.IndexOfAny(['^', '/', '$', '|']);
        if (endIndex >= 0)
        {
            value = value[..endIndex];
        }
        value = value.Trim().TrimStart('.').TrimStart('*').TrimStart('.').TrimEnd('.');
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = uri.Host;
        }
        return NormalizeDomain(value);
    }

    private static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.Length == 0 || !value.Contains('.'))
        {
            return "";
        }
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '.' && character != '_')
            {
                return "";
            }
        }
        if (value.StartsWith('-') || value.EndsWith('-'))
        {
            return "";
        }
        return value;
    }

    private static bool IsAddressToken(string value)
    {
        return value == "0.0.0.0" || value == "127.0.0.1" || value == "::" || value == "::1";
    }
}
