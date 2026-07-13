using System.Diagnostics;
using Microsoft.Win32;

namespace Fylax.Services;

public sealed class StartupService
{
    private const string TaskName = "Fylax";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Fylax";

    public bool IsEnabled()
    {
        try
        {
            using var process = StartSchtasks("/Query", "/TN", TaskName);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnableAsync()
    {
        RemoveLegacyRunKey();

        var action = $"\"{Environment.ProcessPath}\" --minimized";
        using var process = StartSchtasks(
            "/Create", "/TN", TaskName, "/TR", action,
            "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
        if (process is not null)
        {
            await process.WaitForExitAsync();
        }
    }

    public async Task DisableAsync()
    {
        RemoveLegacyRunKey();

        using var process = StartSchtasks("/Delete", "/TN", TaskName, "/F");
        if (process is not null)
        {
            await process.WaitForExitAsync();
        }
    }

    public void RemoveLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key?.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, false);
            }
        }
        catch
        {
        }
    }

    private static Process? StartSchtasks(params string[] arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info);
    }
}
