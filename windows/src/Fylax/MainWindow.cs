using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Fylax.Models;
using Fylax.Services;
using AppTheme = Fylax.Models.ThemeMode;

namespace Fylax;

public sealed class MainWindow : Window
{
    private static readonly string AppVersion = ResolveVersion();

    private static string ResolveVersion()
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            if (version is not null)
            {
                return version.ToString(3);
            }
        }
        catch
        {
        }

        return "0.4.0";
    }

    private readonly SettingsService _settingsService = new();
    private readonly BlocklistLoader _blocklistLoader = new();
    private readonly WindowsDnsService _windowsDnsService = new();
    private readonly DnsProxyService _dnsProxyService = new();

    private readonly AppSettings _settings;
    private readonly Grid _root;

    private Theme _theme;
    private int _tab;
    private bool _active;
    private bool _busy;
    private IReadOnlyList<string> _remoteRules = Array.Empty<string>();
    private List<IPAddress> _systemServers = new();

    private TextBlock? _homeTitle;
    private TextBlock? _homeSubtitle;
    private Border? _toggleThumb;
    private SolidColorBrush? _toggleTrackBrush;

    private IReadOnlyList<DnsQueryLogEntry> _activityEntries = Array.Empty<DnsQueryLogEntry>();
    private string _activitySearch = "";
    private bool _activityOnlyBlocked;
    private StackPanel? _activityListHost;
    private TextBlock? _reqText;
    private TextBlock? _blkText;

    private Border? _snack;
    private DispatcherTimer? _snackTimer;
    private DispatcherTimer? _updateTimer;
    private bool _refreshing;

    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _trayReady;
    private bool _reallyExit;
    private readonly StartupService _startupService = new();

    private static FontFamily? _appFont;

    private static FontFamily AppFont
    {
        get
        {
            return _appFont ??= new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Google Sans Flex Rounded");
        }
    }

    public MainWindow(bool autoStartProtection = false)
    {
        _settings = _settingsService.Load();
        _theme = ResolveTheme();

        Title = "Fylax";
        Width = 420;
        Height = 820;
        MinWidth = 380;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = AppFont;

        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/app.ico"));
        }
        catch
        {
        }

        _root = new Grid();
        Content = _root;
        _active = _dnsProxyService.IsRunning;

        RebuildLayout();
        SetupTray();
        _ = StartupAsync(autoStartProtection);
    }

    private async Task StartupAsync(bool autoStartProtection)
    {
        await RecoverDnsAsync();

        if (autoStartProtection)
        {
            await AutoStartProtectionAsync();
        }
    }

    private async Task AutoStartProtectionAsync()
    {
        await WaitForNetworkAsync();

        for (var attempt = 0; attempt < 5 && !_dnsProxyService.IsRunning; attempt++)
        {
            if (_busy)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            _busy = true;

            try
            {
                await StartProtectionAsync();
                SetActiveVisual(true);
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            finally
            {
                _busy = false;
            }
        }
    }

    private static async Task WaitForNetworkAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            if (IsNetworkReady())
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static bool IsNetworkReady()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var gateway in adapter.GetIPProperties().GatewayAddresses)
            {
                if (gateway.Address is null)
                {
                    continue;
                }

                if (!gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Theme ResolveTheme()
    {
        var dark = _settings.Theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsSystemDark()
        };

        return dark ? Theme.Dark : Theme.Light;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch
        {
        }

        return true;
    }

    private async Task RecoverDnsAsync()
    {
        try
        {
            if (!_dnsProxyService.IsRunning && _windowsDnsService.HasSnapshot)
            {
                await _windowsDnsService.RestoreDnsAsync();
            }
        }
        catch
        {
        }
    }

    private void SetupTray()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon { Text = "Fylax", Visible = true };

            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));

                if (info is not null)
                {
                    _tray.Icon = new System.Drawing.Icon(info.Stream);
                }
            }
            catch
            {
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Open");
            open.Click += (_, _) => Dispatcher.Invoke(RestoreFromTray);
            var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exit.Click += (_, _) => Dispatcher.Invoke(ExitApp);
            menu.Items.Add(open);
            menu.Items.Add(exit);
            _tray.ContextMenuStrip = menu;

            _tray.MouseClick += (_, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    Dispatcher.Invoke(RestoreFromTray);
                }
            };

            _trayReady = true;
        }
        catch
        {
            _trayReady = false;
        }
    }

    private void HideToTray()
    {
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Cleanup();
        _tray?.Dispose();
        _tray = null;
        System.Windows.Application.Current.Shutdown();
    }

    private void Cleanup()
    {
        StopAutoUpdate();

        try
        {
            if (_dnsProxyService.IsRunning)
            {
                Task.Run(async () =>
                {
                    await _dnsProxyService.StopAsync();
                    await _windowsDnsService.RestoreDnsAsync();
                }).GetAwaiter().GetResult();
            }
            else if (_windowsDnsService.HasSnapshot)
            {
                Task.Run(async () => await _windowsDnsService.RestoreDnsAsync()).GetAwaiter().GetResult();
            }
        }
        catch
        {
        }
    }

    private async void OnAutostartToggled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                await _startupService.EnableAsync();
                ShowSnack("Fylax will start with Windows");
            }
            else
            {
                await _startupService.DisableAsync();
                ShowSnack("Startup disabled");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyExit)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_trayReady)
        {
            HideToTray();
        }
        else
        {
            ExitApp();
        }
    }

    private void RebuildLayout()
    {
        Background = _theme.Background;
        _root.Background = _theme.Background;
        _root.Children.Clear();

        FrameworkElement screen = _tab switch
        {
            0 => BuildHome(),
            1 => BuildActivity(),
            2 => BuildShield(),
            _ => BuildSettings()
        };

        _root.Children.Add(screen);
        _root.Children.Add(BuildNavPill());
    }

    private void SetTab(int index)
    {
        if (_tab == index)
        {
            return;
        }

        _tab = index;
        RebuildLayout();
    }

    private FrameworkElement BuildNavPill()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(NavItem(Icons.Home, 0));
        row.Children.Add(NavItem(Icons.SearchActivity, 1));
        row.Children.Add(NavItem(Icons.Shield, 2));
        row.Children.Add(NavItem(Icons.Settings, 3));

        return new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(50),
            Background = _theme.SurfaceContainerHigh,
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black }
        };
    }

    private FrameworkElement NavItem(string path, int index)
    {
        var selected = _tab == index;
        var icon = Icons.Make(path, 24, selected ? _theme.OnSecondaryContainer : _theme.OnSurfaceVariant);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        var item = new Border
        {
            Child = icon,
            CornerRadius = new CornerRadius(50),
            Background = selected ? _theme.SecondaryContainer : Brushes.Transparent,
            Padding = new Thickness(22, 10, 22, 10),
            Margin = new Thickness(3, 0, 3, 0),
            Cursor = Cursors.Hand
        };

        item.MouseLeftButtonUp += (_, _) => SetTab(index);
        return item;
    }

    private FrameworkElement BuildHome()
    {
        var column = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _homeTitle = Label(_active ? "Protection is on" : "Protection is off", 28, _theme.OnSurface);
        _homeTitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(_homeTitle);

        column.Children.Add(Spacer(8));

        _homeSubtitle = Label(_active ? "DNS filtering is active" : "Tap to start filtering", 15, _theme.OnSurfaceVariant);
        _homeSubtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(_homeSubtitle);

        column.Children.Add(Spacer(24));

        var toggle = BuildHomeToggle();
        toggle.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(toggle);

        var container = new Grid { Margin = new Thickness(24) };
        container.Children.Add(column);

        var refresh = IconButton(Icons.Cached, async () => await UpdateFiltersAsync(), _theme.OnSurface, 24);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        refresh.VerticalAlignment = VerticalAlignment.Top;
        container.Children.Add(refresh);

        return container;
    }

    private FrameworkElement BuildHomeToggle()
    {
        const double w = 118;
        const double h = 56;
        const double thumb = 46;
        const double pad = 5;

        _toggleTrackBrush = new SolidColorBrush(_active ? _theme.ToggleOn : _theme.ToggleOff);

        var track = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = _toggleTrackBrush
        };

        var thumbBorder = new Border
        {
            Width = thumb,
            Height = thumb,
            CornerRadius = new CornerRadius(thumb / 2),
            Background = _theme.Background
        };

        _toggleThumb = thumbBorder;

        var canvas = new Canvas { Width = w, Height = h };
        Canvas.SetTop(thumbBorder, pad);
        Canvas.SetLeft(thumbBorder, _active ? w - thumb - pad : pad);
        canvas.Children.Add(track);
        canvas.Children.Add(thumbBorder);

        var host = new Border
        {
            Child = canvas,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        host.MouseLeftButtonUp += async (_, _) => await OnToggleAsync();
        return host;
    }

    private void SetActiveVisual(bool on)
    {
        _active = on;

        const double w = 150;
        const double thumb = 62;
        const double pad = 6;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        _toggleThumb?.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(on ? w - thumb - pad : pad, duration) { EasingFunction = ease });
        _toggleTrackBrush?.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? _theme.ToggleOn : _theme.ToggleOff, duration));


        if (_homeTitle is not null)
        {
            _homeTitle.Text = on ? "Protection is on" : "Protection is off";
        }

        if (_homeSubtitle is not null)
        {
            _homeSubtitle.Text = on ? "DNS filtering is active" : "Tap to start filtering";
        }
    }

    private async Task OnToggleAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            if (_dnsProxyService.IsRunning)
            {
                await StopProtectionAsync();
            }
            else
            {
                await StartProtectionAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            SetActiveVisual(_dnsProxyService.IsRunning);
        }
    }

    private async Task StartProtectionAsync()
    {
        if (!_windowsDnsService.IsAdministrator())
        {
            throw new InvalidOperationException("Run Fylax as Administrator to change Windows DNS.");
        }

        var upstream = await BuildUpstreamForStartAsync();
        var urls = _settings.EnabledUrls();
        _remoteRules = urls.Count > 0 ? await _blocklistLoader.LoadCachedFirstAsync(urls, CancellationToken.None) : Array.Empty<string>();
        var matcher = DomainMatcher.Create(_settings.Allowed, _settings.BlockedManual, _remoteRules);

        await _dnsProxyService.StartAsync(upstream, matcher);

        try
        {
            await _dnsProxyService.RunSelfTestAsync();
        }
        catch
        {
            await _dnsProxyService.StopAsync();
            throw;
        }

        try
        {
            await _windowsDnsService.ApplyLocalDnsAsync();
        }
        catch
        {
            await _dnsProxyService.StopAsync();
            throw;
        }

        StartAutoUpdate();
        _ = RefreshRulesAsync(false);
    }

    private void StartAutoUpdate()
    {
        _updateTimer?.Stop();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateTimer.Tick += async (_, _) => await RefreshRulesAsync(false);
        _updateTimer.Start();
    }

    private void StopAutoUpdate()
    {
        _updateTimer?.Stop();
        _updateTimer = null;
    }

    private async Task RefreshRulesAsync(bool announce)
    {
        if (_refreshing || !_dnsProxyService.IsRunning)
        {
            return;
        }

        _refreshing = true;

        try
        {
            var urls = _settings.EnabledUrls();
            _remoteRules = urls.Count > 0 ? await _blocklistLoader.LoadAsync(urls, CancellationToken.None) : Array.Empty<string>();
            var matcher = DomainMatcher.Create(_settings.Allowed, _settings.BlockedManual, _remoteRules);
            _dnsProxyService.UpdateRules(matcher);

            if (announce)
            {
                ShowSnack($"Updated · {_remoteRules.Count} rules");
            }
        }
        catch (Exception ex)
        {
            if (announce)
            {
                ShowError(ex.Message);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task StopProtectionAsync()
    {
        StopAutoUpdate();
        await _dnsProxyService.StopAsync();
        await _windowsDnsService.RestoreDnsAsync();
    }

    private async Task<IUpstreamResolver> BuildUpstreamForStartAsync()
    {
        if (_settings.DnsMode == DnsMode.Custom)
        {
            return BuildCustomUpstream();
        }

        var servers = await _windowsDnsService.GetActiveDnsServersAsync();
        _systemServers = servers
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address is not null)
            .Select(address => address!)
            .ToList();

        if (_systemServers.Count == 0)
        {
            throw new InvalidOperationException("No system DNS server found. Switch to Custom and set a resolver.");
        }

        return new PlainUpstreamResolver(_systemServers[0]);
    }

    private IUpstreamResolver BuildUpstreamForLive()
    {
        if (_settings.DnsMode == DnsMode.Custom)
        {
            return BuildCustomUpstream();
        }

        if (_systemServers.Count == 0)
        {
            throw new InvalidOperationException("No captured system DNS. Restart protection to apply.");
        }

        return new PlainUpstreamResolver(_systemServers[0]);
    }

    private IUpstreamResolver BuildCustomUpstream()
    {
        var primary = _settings.DnsPrimary.Trim();

        if (!IPAddress.TryParse(primary, out var ip))
        {
            throw new InvalidOperationException("Set a Primary DNS or switch to System default.");
        }

        if (_settings.DohEnabled)
        {
            var host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{primary}]" : primary;
            return new DohUpstreamResolver($"https://{host}/dns-query");
        }

        return new PlainUpstreamResolver(ip);
    }

    private async Task UpdateFiltersAsync()
    {
        if (!_dnsProxyService.IsRunning)
        {
            ShowSnack("Start protection first");
            return;
        }

        await RefreshRulesAsync(true);
    }

    private FrameworkElement BuildActivity()
    {
        _activityEntries = _dnsProxyService.Snapshot();
        _activitySearch = "";
        _activityOnlyBlocked = false;
        var counts = _dnsProxyService.Counts();

        var top = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Label("Activity", 20, _theme.OnSurface);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);

        var clear = TextOnlyButton("Clear", () =>
        {
            _dnsProxyService.ClearLog();
            _activityEntries = _dnsProxyService.Snapshot();
            RefreshCounters();
            RefreshActivityList();
        });
        clear.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(clear, 1);

        var refresh = IconButton(Icons.Refresh, () =>
        {
            _activityEntries = _dnsProxyService.Snapshot();
            RefreshCounters();
            RefreshActivityList();
        }, _theme.OnSurface, 22);
        Grid.SetColumn(refresh, 2);

        header.Children.Add(title);
        header.Children.Add(clear);
        header.Children.Add(refresh);
        top.Children.Add(header);
        top.Children.Add(Spacer(8));

        var countersRow = new Grid();
        countersRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        countersRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var reqColumn = new StackPanel();
        _reqText = Label(counts.Total.ToString(), 26, _theme.OnSurface);
        reqColumn.Children.Add(_reqText);
        reqColumn.Children.Add(Label("Requests", 13, _theme.OnSurfaceVariant));
        Grid.SetColumn(reqColumn, 0);

        var blkColumn = new StackPanel();
        _blkText = Label(counts.Blocked.ToString(), 26, new SolidColorBrush(_theme.ToggleOff));
        blkColumn.Children.Add(_blkText);
        blkColumn.Children.Add(Label("Blocked", 13, _theme.OnSurfaceVariant));
        Grid.SetColumn(blkColumn, 1);

        countersRow.Children.Add(reqColumn);
        countersRow.Children.Add(blkColumn);
        top.Children.Add(countersRow);
        top.Children.Add(Spacer(12));

        var searchField = MakeField("Search", "", out var searchBox);
        searchBox.TextChanged += (_, _) =>
        {
            _activitySearch = searchBox.Text;
            RefreshActivityList();
        };
        top.Children.Add(searchField);
        top.Children.Add(Spacer(8));

        var onlyRow = new Grid();
        onlyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        onlyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var onlyLabel = Label("Blocked only", 14, _theme.OnSurface);
        onlyLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(onlyLabel, 0);

        var onlySwitch = MakeSwitch(false, value =>
        {
            _activityOnlyBlocked = value;
            RefreshActivityList();
        });
        onlySwitch.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(onlySwitch, 1);

        onlyRow.Children.Add(onlyLabel);
        onlyRow.Children.Add(onlySwitch);
        top.Children.Add(onlyRow);
        top.Children.Add(Spacer(8));

        _activityListHost = new StackPanel { Margin = new Thickness(0, 0, 0, 110) };
        var scroll = new ScrollViewer
        {
            Content = _activityListHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dock = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(scroll);

        RefreshActivityList();
        return dock;
    }

    private void RefreshCounters()
    {
        var counts = _dnsProxyService.Counts();

        if (_reqText is not null)
        {
            _reqText.Text = counts.Total.ToString();
        }

        if (_blkText is not null)
        {
            _blkText.Text = counts.Blocked.ToString();
        }
    }

    private void RefreshActivityList()
    {
        if (_activityListHost is null)
        {
            return;
        }

        _activityListHost.Children.Clear();
        var search = _activitySearch.Trim();

        var filtered = _activityEntries
            .Where(entry => (!_activityOnlyBlocked || entry.Blocked) && (search.Length == 0 || entry.Host.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (filtered.Count == 0)
        {
            var empty = Label("No activity yet", 13, _theme.OnSurfaceVariant);
            empty.Margin = new Thickness(8);
            _activityListHost.Children.Add(empty);
            return;
        }

        foreach (var entry in filtered)
        {
            _activityListHost.Children.Add(ActivityRow(entry));
        }
    }

    private FrameworkElement ActivityRow(DnsQueryLogEntry entry)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var host = new TextBlock
        {
            Text = entry.Host,
            FontSize = 14,
            FontFamily = AppFont,
            Foreground = entry.Blocked ? new SolidColorBrush(_theme.ToggleOff) : _theme.OnSurface,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

        var meta = Label($"{entry.Type}  ·  {entry.Time:HH:mm:ss}", 12, _theme.OnSurfaceVariant);

        texts.Children.Add(host);
        texts.Children.Add(meta);
        Grid.SetColumn(texts, 0);

        var action = entry.Blocked
            ? SmallButton("Allow", () => AllowHost(entry.Host))
            : SmallButton("Block", () => BlockHost(entry.Host));
        action.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(action, 1);

        row.Children.Add(texts);
        row.Children.Add(action);
        return row;
    }

    private void AllowHost(string host)
    {
        if (_settings.Allowed.Any(domain => domain == host))
        {
            ShowSnack("Already allowed");
            return;
        }

        _settings.Allowed.Add(host);
        _settingsService.Save(_settings);
        ApplyMatcherLive();
        ShowSnack($"Allowed · {host}");
    }

    private void BlockHost(string host)
    {
        if (_settings.BlockedManual.Any(domain => domain == host))
        {
            ShowSnack("Already blocked");
            return;
        }

        _settings.BlockedManual.Add(host);
        _settingsService.Save(_settings);
        ApplyMatcherLive();
        ShowSnack($"Blocked · {host}");
    }

    private void ApplyMatcherLive()
    {
        if (!_dnsProxyService.IsRunning)
        {
            return;
        }

        var matcher = DomainMatcher.Create(_settings.Allowed, _settings.BlockedManual, _remoteRules);
        _dnsProxyService.UpdateRules(matcher);
    }

    private FrameworkElement BuildShield()
    {
        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 140) };

        content.Children.Add(Label("Blocklists", 20, _theme.OnSurface));
        content.Children.Add(Spacer(12));

        var listInner = new StackPanel();

        if (_settings.Lists.Count == 0)
        {
            listInner.Children.Add(EmptyText("No lists"));
        }
        else
        {
            foreach (var item in _settings.Lists.ToList())
            {
                listInner.Children.Add(BlocklistRow(item));
            }
        }

        content.Children.Add(Card(listInner));
        content.Children.Add(Spacer(12));

        var addUrlRow = new Grid();
        addUrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addUrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var urlField = MakeField("Add blocklist URL", "", out var urlBox);
        Grid.SetColumn(urlField, 0);

        var addUrlButton = IconButton(Icons.Add, () =>
        {
            var url = urlBox.Text.Trim();

            if (url.Length > 0 && !_settings.Lists.Any(item => item.Url == url))
            {
                _settings.Lists.Add(new BlockListItem { Url = url, Enabled = true });
                RebuildLayout();
            }
        }, _theme.OnSurface, 26);
        addUrlButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(addUrlButton, 1);

        addUrlRow.Children.Add(urlField);
        addUrlRow.Children.Add(addUrlButton);
        content.Children.Add(addUrlRow);
        content.Children.Add(Spacer(12));

        content.Children.Add(PillButton("Apply", async () => await ApplyListsAsync()));
        content.Children.Add(Spacer(28));

        content.Children.Add(Label("Allowed", 20, _theme.OnSurface));
        content.Children.Add(Spacer(4));
        content.Children.Add(Label("Domains here are never blocked, even if a list blocks them.", 13, _theme.OnSurfaceVariant));
        content.Children.Add(Spacer(12));

        var allowedInner = new StackPanel();

        if (_settings.Allowed.Count == 0)
        {
            allowedInner.Children.Add(EmptyText("No exceptions"));
        }
        else
        {
            foreach (var domain in _settings.Allowed.ToList())
            {
                allowedInner.Children.Add(DomainRow(domain, true));
            }
        }

        content.Children.Add(Card(allowedInner));
        content.Children.Add(Spacer(12));
        content.Children.Add(AddDomainRow("Add allowed domain", true));
        content.Children.Add(Spacer(28));

        content.Children.Add(Label("Blocked", 20, _theme.OnSurface));
        content.Children.Add(Spacer(4));
        content.Children.Add(Label("Domains here are always blocked, on top of your lists.", 13, _theme.OnSurfaceVariant));
        content.Children.Add(Spacer(12));

        var blockedInner = new StackPanel();

        if (_settings.BlockedManual.Count == 0)
        {
            blockedInner.Children.Add(EmptyText("Nothing added"));
        }
        else
        {
            foreach (var domain in _settings.BlockedManual.ToList())
            {
                blockedInner.Children.Add(DomainRow(domain, false));
            }
        }

        content.Children.Add(Card(blockedInner));
        content.Children.Add(Spacer(12));
        content.Children.Add(AddDomainRow("Add blocked domain", false));
        content.Children.Add(Spacer(28));

        var dnsHeader = new StackPanel { Orientation = Orientation.Horizontal };
        var dnsIcon = Icons.Make(Icons.Dns, 22, _theme.OnSurface);
        dnsIcon.VerticalAlignment = VerticalAlignment.Center;
        dnsHeader.Children.Add(dnsIcon);
        dnsHeader.Children.Add(new TextBlock
        {
            Text = "DNS",
            FontSize = 20,
            FontFamily = AppFont,
            Foreground = _theme.OnSurface,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(dnsHeader);
        content.Children.Add(Spacer(8));

        content.Children.Add(RadioRow(_settings.DnsMode == DnsMode.System, "System default", "Provided by your network / ISP", () =>
        {
            _settings.DnsMode = DnsMode.System;
            RebuildLayout();
        }));

        content.Children.Add(RadioRow(_settings.DnsMode == DnsMode.Custom, "Custom", "Choose your own resolver", () =>
        {
            _settings.DnsMode = DnsMode.Custom;
            RebuildLayout();
        }));

        if (_settings.DnsMode == DnsMode.Custom)
        {
            content.Children.Add(Spacer(8));

            var primaryField = MakeField("Primary", _settings.DnsPrimary, out var primaryBox);
            primaryBox.TextChanged += (_, _) => _settings.DnsPrimary = primaryBox.Text;
            content.Children.Add(primaryField);
            content.Children.Add(Spacer(8));

            var secondaryField = MakeField("Secondary", _settings.DnsSecondary, out var secondaryBox);
            secondaryBox.TextChanged += (_, _) => _settings.DnsSecondary = secondaryBox.Text;
            content.Children.Add(secondaryField);
            content.Children.Add(Spacer(8));

            var dohRow = new Grid();
            dohRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dohRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dohLabel = Label("DNS over HTTPS (DoH)", 15, _theme.OnSurface);
            dohLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dohLabel, 0);

            var dohSwitch = MakeSwitch(_settings.DohEnabled, value => _settings.DohEnabled = value);
            dohSwitch.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dohSwitch, 1);

            dohRow.Children.Add(dohLabel);
            dohRow.Children.Add(dohSwitch);
            content.Children.Add(dohRow);
        }

        content.Children.Add(Spacer(12));
        content.Children.Add(PillButton("Save DNS", async () => await SaveDnsAsync()));

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private FrameworkElement AddDomainRow(string placeholder, bool allowed)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var field = MakeField(placeholder, "", out var box);
        Grid.SetColumn(field, 0);

        var addButton = IconButton(Icons.Add, () =>
        {
            var domain = box.Text.Trim().ToLowerInvariant();

            if (domain.Length == 0)
            {
                return;
            }

            if (allowed)
            {
                if (_settings.Allowed.Any(value => value == domain))
                {
                    return;
                }

                _settings.Allowed.Add(domain);
            }
            else
            {
                if (_settings.BlockedManual.Any(value => value == domain))
                {
                    return;
                }

                _settings.BlockedManual.Add(domain);
            }

            _settingsService.Save(_settings);
            ApplyMatcherLive();
            RebuildLayout();
        }, _theme.OnSurface, 26);
        addButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(addButton, 1);

        row.Children.Add(field);
        row.Children.Add(addButton);
        return row;
    }

    private FrameworkElement BlocklistRow(BlockListItem item)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var url = new TextBlock
        {
            Text = item.Url,
            FontSize = 13,
            FontFamily = AppFont,
            Foreground = _theme.OnSurface,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(url, 0);

        var toggle = MakeSwitch(item.Enabled, value => item.Enabled = value);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(toggle, 1);

        var delete = IconButton(Icons.Delete, () =>
        {
            _settings.Lists.Remove(item);
            RebuildLayout();
        }, _theme.OnSurfaceVariant, 22);
        Grid.SetColumn(delete, 2);

        row.Children.Add(url);
        row.Children.Add(toggle);
        row.Children.Add(delete);
        return row;
    }

    private FrameworkElement DomainRow(string domain, bool allowed)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = domain,
            FontSize = 13,
            FontFamily = AppFont,
            Foreground = _theme.OnSurface,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);

        var delete = IconButton(Icons.Delete, () =>
        {
            if (allowed)
            {
                _settings.Allowed.Remove(domain);
            }
            else
            {
                _settings.BlockedManual.Remove(domain);
            }

            _settingsService.Save(_settings);
            ApplyMatcherLive();
            RebuildLayout();
        }, _theme.OnSurfaceVariant, 22);
        Grid.SetColumn(delete, 1);

        row.Children.Add(label);
        row.Children.Add(delete);
        return row;
    }

    private async Task ApplyListsAsync()
    {
        _settingsService.Save(_settings);

        try
        {
            if (_dnsProxyService.IsRunning)
            {
                var urls = _settings.EnabledUrls();
                _remoteRules = urls.Count > 0 ? await _blocklistLoader.LoadAsync(urls, CancellationToken.None) : Array.Empty<string>();
                var matcher = DomainMatcher.Create(_settings.Allowed, _settings.BlockedManual, _remoteRules);
                _dnsProxyService.UpdateRules(matcher);
                ShowSnack($"Applied · {_remoteRules.Count} rules");
            }
            else
            {
                ShowSnack("Saved · start protection to apply");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task SaveDnsAsync()
    {
        _settingsService.Save(_settings);

        try
        {
            if (_dnsProxyService.IsRunning)
            {
                var upstream = BuildUpstreamForLive();
                _dnsProxyService.UpdateUpstream(upstream);
            }

            ShowSnack("DNS saved");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private FrameworkElement BuildSettings()
    {
        var top = new StackPanel();
        top.Children.Add(Label("Theme", 20, _theme.OnSurface));
        top.Children.Add(Spacer(8));
        top.Children.Add(RadioRow(_settings.Theme == AppTheme.System, "System", null, () => SetThemeMode(AppTheme.System)));
        top.Children.Add(RadioRow(_settings.Theme == AppTheme.Light, "Light", null, () => SetThemeMode(AppTheme.Light)));
        top.Children.Add(RadioRow(_settings.Theme == AppTheme.Dark, "Dark", null, () => SetThemeMode(AppTheme.Dark)));

        top.Children.Add(Spacer(24));
        top.Children.Add(Label("Startup", 20, _theme.OnSurface));
        top.Children.Add(Spacer(8));

        var startupRow = new Grid();
        startupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        startupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var startupLabel = Label("Start with Windows", 15, _theme.OnSurface);
        startupLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(startupLabel, 0);

        var startupSwitch = MakeSwitch(_startupService.IsEnabled(), OnAutostartToggled);
        startupSwitch.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(startupSwitch, 1);

        startupRow.Children.Add(startupLabel);
        startupRow.Children.Add(startupSwitch);
        top.Children.Add(startupRow);

        DockPanel.SetDock(top, Dock.Top);

        var bottom = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 120)
        };

        var shield = Icons.Make(Icons.Shield, 48, _theme.Primary);
        shield.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Children.Add(shield);
        bottom.Children.Add(Spacer(12));

        var name = Label("Fylax", 20, _theme.OnSurface);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Children.Add(name);
        bottom.Children.Add(Spacer(4));

        var version = Label($"Version {AppVersion}", 14, _theme.OnSurfaceVariant);
        version.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Children.Add(version);

        var dock = new DockPanel { Margin = new Thickness(16) };
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        return dock;
    }

    private void SetThemeMode(AppTheme mode)
    {
        _settings.Theme = mode;
        _settingsService.Save(_settings);
        _theme = ResolveTheme();
        RebuildLayout();
    }

    private void ShowSnack(string message)
    {
        if (_snack is not null)
        {
            _root.Children.Remove(_snack);
            _snack = null;
        }

        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontFamily = AppFont,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        };

        var snack = new Border
        {
            Child = text,
            Background = new SolidColorBrush(Color.FromRgb(0x32, 0x30, 0x36)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 92),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black }
        };

        _snack = snack;
        _root.Children.Add(snack);

        _snackTimer?.Stop();
        _snackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _snackTimer.Tick += (_, _) =>
        {
            _snackTimer?.Stop();

            if (_snack is not null)
            {
                _root.Children.Remove(_snack);
                _snack = null;
            }
        };
        _snackTimer.Start();
    }

    private void ShowError(string message)
    {
        var clean = message.Replace("\r", " ").Replace("\n", " ").Trim();

        if (clean.Length > 180)
        {
            clean = clean.Substring(0, 180) + "…";
        }

        ShowSnack(clean);
    }

    private TextBlock Label(string text, double size, Brush color)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = color,
            FontFamily = AppFont,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private FrameworkElement EmptyText(string text)
    {
        var label = Label(text, 13, _theme.OnSurfaceVariant);
        label.Margin = new Thickness(8);
        return label;
    }

    private Border Card(UIElement child)
    {
        return new Border
        {
            Child = child,
            Background = _theme.SurfaceContainerHighest,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8)
        };
    }

    private FrameworkElement PillButton(string label, Action onClick)
    {
        var text = Label(label, 15, _theme.OnPrimary);
        text.HorizontalAlignment = HorizontalAlignment.Center;

        var button = new Border
        {
            Child = text,
            Background = _theme.Primary,
            CornerRadius = new CornerRadius(50),
            Padding = new Thickness(0, 12, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand
        };

        button.MouseLeftButtonUp += (_, _) => onClick();
        return button;
    }

    private FrameworkElement SmallButton(string label, Action onClick)
    {
        var text = Label(label, 13, _theme.OnPrimary);

        var button = new Border
        {
            Child = text,
            Background = _theme.Primary,
            CornerRadius = new CornerRadius(50),
            Padding = new Thickness(16, 7, 16, 7),
            Cursor = Cursors.Hand
        };

        button.MouseLeftButtonUp += (_, _) => onClick();
        return button;
    }

    private FrameworkElement TextOnlyButton(string label, Action onClick)
    {
        var text = Label(label, 13, _theme.Primary);

        var button = new Border
        {
            Child = text,
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand
        };

        button.MouseLeftButtonUp += (_, _) => onClick();
        return button;
    }

    private FrameworkElement IconButton(string path, Action onClick, Brush tint, double size)
    {
        var icon = Icons.Make(path, size, tint);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        var button = new Border
        {
            Child = icon,
            Background = Brushes.Transparent,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(50),
            Cursor = Cursors.Hand
        };

        button.MouseLeftButtonUp += (_, _) => onClick();
        return button;
    }

    private FrameworkElement RadioRow(bool selected, string title, string? subtitle, Action onClick)
    {
        var indicator = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        indicator.Children.Add(new Ellipse
        {
            Width = 20,
            Height = 20,
            Stroke = selected ? _theme.Primary : _theme.OnSurfaceVariant,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        });

        if (selected)
        {
            indicator.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = _theme.Primary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Label(title, 15, _theme.OnSurface));

        if (subtitle is not null)
        {
            texts.Children.Add(Label(subtitle, 12, _theme.OnSurfaceVariant));
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(indicator);
        row.Children.Add(texts);

        var host = new Border
        {
            Child = row,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            Cursor = Cursors.Hand
        };

        host.MouseLeftButtonUp += (_, _) => onClick();
        return host;
    }

    private Border MakeField(string placeholder, string initial, out TextBox box)
    {
        var textBox = new TextBox
        {
            Text = initial,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = _theme.OnSurface,
            CaretBrush = _theme.OnSurface,
            FontFamily = AppFont,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = _theme.OnSurfaceVariant,
            FontFamily = AppFont,
            FontSize = 14,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            Visibility = string.IsNullOrEmpty(initial) ? Visibility.Visible : Visibility.Collapsed
        };

        textBox.TextChanged += (_, _) => hint.Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(textBox);

        box = textBox;

        return new Border
        {
            Child = grid,
            BorderBrush = _theme.Outline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Background = Brushes.Transparent
        };
    }

    private FrameworkElement MakeSwitch(bool initial, Action<bool> onChange)
    {
        const double w = 44;
        const double h = 24;
        const double thumb = 18;
        const double pad = 3;

        var onColor = ((SolidColorBrush)_theme.Primary).Color;
        var offColor = ((SolidColorBrush)_theme.OutlineVariant).Color;

        var state = initial;
        var trackBrush = new SolidColorBrush(state ? onColor : offColor);

        var track = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = trackBrush
        };

        var thumbShape = new Ellipse { Width = thumb, Height = thumb, Fill = Brushes.White };

        var canvas = new Canvas { Width = w, Height = h };
        Canvas.SetTop(thumbShape, pad);
        Canvas.SetLeft(thumbShape, state ? w - thumb - pad : pad);
        canvas.Children.Add(track);
        canvas.Children.Add(thumbShape);

        var host = new Border
        {
            Child = canvas,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        host.MouseLeftButtonUp += (_, _) =>
        {
            state = !state;
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            thumbShape.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(state ? w - thumb - pad : pad, duration) { EasingFunction = ease });
            trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(state ? onColor : offColor, duration));
            onChange(state);
        };

        return host;
    }

    private static FrameworkElement Spacer(double height)
    {
        return new Border { Height = height };
    }
}
