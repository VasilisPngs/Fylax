using System.Windows;

namespace Fylax;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var hidden = e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(hidden);
        MainWindow = window;

        if (!hidden)
        {
            window.Show();
        }
    }
}
