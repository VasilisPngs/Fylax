using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace Fylax.Services;

public sealed class WindowsDnsService
{
    private readonly string _snapshotPath;

    public WindowsDnsService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fylax");
        Directory.CreateDirectory(folder);
        _snapshotPath = Path.Combine(folder, "dns-snapshot.json");
    }

    public bool HasSnapshot => File.Exists(_snapshotPath);

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<IReadOnlyList<string>> GetActiveDnsServersAsync()
    {
        const string command = @"
$ErrorActionPreference = 'SilentlyContinue'
$idx = Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1 -ExpandProperty InterfaceIndex
$servers = @()
if ($idx) {
    $servers += (Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv4).ServerAddresses
    $servers += (Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv6).ServerAddresses
}
$servers | Where-Object { $_ -and $_ -ne '127.0.0.1' -and $_ -ne '::1' } | ForEach-Object { Write-Output $_ }
";
        var output = await RunPowerShellCaptureAsync(command);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct()
            .ToList();
    }

    public async Task ApplyLocalDnsAsync()
    {
        if (!IsAdministrator())
        {
            throw new InvalidOperationException("Administrator privileges are required to change Windows DNS settings.");
        }
        var script = ApplyScript.Replace("__SNAPSHOT_PATH__", EscapeForSingleQuotes(_snapshotPath));
        var output = await RunPowerShellCaptureAsync(script);
        var applied = ParseApplied(output);
        if (applied <= 0)
        {
            var detail = ParseError(output);
            var message = "Fylax could not take over the system DNS on any active adapter.";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += " " + detail;
            }
            throw new InvalidOperationException(message);
        }
    }

    public Task RestoreDnsAsync()
    {
        if (!IsAdministrator())
        {
            throw new InvalidOperationException("Administrator privileges are required to restore Windows DNS settings.");
        }
        if (!File.Exists(_snapshotPath))
        {
            return RunPowerShellAsync(RestoreAutomaticScript);
        }
        var script = RestoreScript.Replace("__SNAPSHOT_PATH__", EscapeForSingleQuotes(_snapshotPath));
        return RunPowerShellAsync(script);
    }

    private const string ApplyScript = @"
$ErrorActionPreference = 'SilentlyContinue'
$path = '__SNAPSHOT_PATH__'
$errs = @()
$targets = @()
$configs = Get-NetIPConfiguration | Where-Object { $_.NetAdapter.Status -eq 'Up' -and ($_.IPv4DefaultGateway -or $_.IPv6DefaultGateway) }
foreach ($c in $configs) {
    $targets += [pscustomobject]@{ Index = [int]$c.InterfaceIndex; Alias = [string]$c.InterfaceAlias }
}
if (@($targets).Count -eq 0) {
    $ri = Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1
    if ($ri) {
        $na = Get-NetAdapter -InterfaceIndex $ri.InterfaceIndex
        $targets += [pscustomobject]@{ Index = [int]$ri.InterfaceIndex; Alias = [string]$na.Name }
    }
}
if (-not (Test-Path $path)) {
    $snapshot = foreach ($t in $targets) {
        $guid = (Get-NetAdapter -InterfaceIndex $t.Index).InterfaceGuid
        $v4Static = ''
        $v6Static = ''
        if ($guid) {
            $v4Static = (Get-ItemProperty -Path ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\' + $guid) -Name NameServer -ErrorAction SilentlyContinue).NameServer
            $v6Static = (Get-ItemProperty -Path ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\' + $guid) -Name NameServer -ErrorAction SilentlyContinue).NameServer
        }
        $dns4 = (Get-DnsClientServerAddress -InterfaceIndex $t.Index -AddressFamily IPv4).ServerAddresses
        $dns6 = (Get-DnsClientServerAddress -InterfaceIndex $t.Index -AddressFamily IPv6).ServerAddresses
        [pscustomobject]@{ Index = $t.Index; V4 = @($dns4); V6 = @($dns6); V4Static = [bool]$v4Static; V6Static = [bool]$v6Static }
    }
    ConvertTo-Json -InputObject @($snapshot) -Depth 5 | Set-Content -Path $path -Encoding UTF8
}
$applied = 0
foreach ($t in $targets) {
    $idx = $t.Index
    $alias = $t.Alias
    try { Set-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv4 -ServerAddresses '127.0.0.1' -ErrorAction Stop } catch { $errs += ('v4 ' + $_.Exception.Message) }
    try { Set-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv6 -ServerAddresses '::1' -ErrorAction Stop } catch { $errs += ('v6 ' + $_.Exception.Message) }
    $cur4 = @()
    $cur6 = @()
    $leak6 = @()
    for ($v = 0; $v -lt 5; $v++) {
        $cur4 = @((Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv4).ServerAddresses)
        $cur6 = @((Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv6).ServerAddresses)
        $leak6 = @($cur6 | Where-Object { $_ -and $_ -ne '::1' })
        if (($cur4 -contains '127.0.0.1') -and (@($leak6).Count -eq 0)) { break }
        Start-Sleep -Milliseconds 250
    }
    if (($cur4 -notcontains '127.0.0.1') -or (@($leak6).Count -gt 0)) {
        $null = (& netsh interface ipv4 set dnsservers $alias static 127.0.0.1 primary 2>&1)
        $null = (& netsh interface ipv6 set dnsservers $alias static ::1 primary 2>&1)
        $cur4 = @((Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv4).ServerAddresses)
        $cur6 = @((Get-DnsClientServerAddress -InterfaceIndex $idx -AddressFamily IPv6).ServerAddresses)
        $leak6 = @($cur6 | Where-Object { $_ -and $_ -ne '::1' })
    }
    if (($cur4 -contains '127.0.0.1') -and (@($leak6).Count -eq 0)) {
        $applied++
    } else {
        $errs += ('idx ' + $idx + ' v4=[' + (@($cur4) -join ',') + '] v6=[' + (@($cur6) -join ',') + ']')
    }
}
Clear-DnsClientCache
ipconfig /flushdns | Out-Null
if (@($errs).Count -gt 0) { Write-Output ('FYLAX_ERR=' + (@($errs) -join ' ; ')) }
Write-Output ('FYLAX_APPLIED=' + $applied)
";

    private const string RestoreScript = @"
$ErrorActionPreference = 'Stop'
$path = '__SNAPSHOT_PATH__'
$entries = Get-Content -Path $path -Raw | ConvertFrom-Json
foreach ($entry in @($entries)) {
    $index = [int]$entry.Index
    if ($entry.V4Static -and @($entry.V4).Count -gt 0) {
        try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 -ServerAddresses @($entry.V4) } catch {}
    } else {
        try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 -ResetServerAddresses } catch {}
    }
    if ($entry.V6Static -and @($entry.V6).Count -gt 0) {
        try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 -ServerAddresses @($entry.V6) } catch {}
    } else {
        try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 -ResetServerAddresses } catch {}
    }
}
Remove-Item -Path $path -Force -ErrorAction SilentlyContinue
Clear-DnsClientCache
ipconfig /flushdns | Out-Null
";

    private const string RestoreAutomaticScript = @"
$ErrorActionPreference = 'Stop'
$v4 = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric | Select-Object -ExpandProperty InterfaceIndex
$v6 = Get-NetRoute -DestinationPrefix '::/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric | Select-Object -ExpandProperty InterfaceIndex
$indexes = @(@($v4) + @($v6)) | Select-Object -Unique
foreach ($index in $indexes) {
    try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 -ResetServerAddresses } catch {}
    try { Set-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 -ResetServerAddresses } catch {}
}
Clear-DnsClientCache
ipconfig /flushdns | Out-Null
";

    private static int ParseApplied(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FYLAX_APPLIED=", StringComparison.Ordinal) && int.TryParse(trimmed.AsSpan(14), out var value))
            {
                return value;
            }
        }
        return 0;
    }

    private static string ParseError(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FYLAX_ERR=", StringComparison.Ordinal))
            {
                return trimmed[10..].Trim();
            }
        }
        return string.Empty;
    }

    private static string EscapeForSingleQuotes(string value)
    {
        return value.Replace("'", "''");
    }

    private static Task RunPowerShellAsync(string command)
    {
        return RunPowerShellCaptureAsync(command);
    }

    private static async Task<string> RunPowerShellCaptureAsync(string command)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(message.Trim());
        }
        return output;
    }
}
