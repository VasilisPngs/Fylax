using System.Windows;

namespace Fylax;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument => string.Equals(argument, "--restore-dns", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var dns = new Services.WindowsDnsService();
                if (dns.HasSnapshot)
                {
                    Task.Run(async () => await dns.RestoreDnsAsync()).GetAwaiter().GetResult();
                }
            }
            catch
            {
            }

            Shutdown();
            return;
        }

        var hidden = e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(hidden);
        MainWindow = window;

        if (!hidden)
        {
            window.Show();
        }
    }
}
