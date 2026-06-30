using Microsoft.Win32;

namespace Fylax.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Fylax";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task EnableAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized", RegistryValueKind.String);
        return Task.CompletedTask;
    }

    public Task DisableAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(ValueName, false);
        return Task.CompletedTask;
    }
}
