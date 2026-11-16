using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Presentation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AmbientTheme.App;

public partial class App : Application, INotifier
{
    private AppHost? _host;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private DispatcherTimer? _timer;
    private int _refreshQueued;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _host = new AppHost(this);
            _window = new MainWindow(_host);
            CreateTray(desktop);

            _host.Changed += QueueRefresh;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _host.Tick());
            _timer.Start();
            _host.Start();
            desktop.Exit += (_, _) => _host.Dispose();

            if (!desktop.Args?.Contains("--tray") ?? true)
            {
                _window.Show();
            }

            Refresh();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Called by the coordinator under its lock, possibly off the UI thread: only queue the toast.</summary>
    public void Notify(NotificationKind kind) => Dispatcher.UIThread.Post(() =>
    {
        var canResume = kind is NotificationKind.ExternalThemeChange or NotificationKind.ThemeApplyFailed;
        new NotificationWindow(StatusPresenter.Describe(kind),
            canResume ? () => _host!.Coordinator.UserSetMode(DesiredMode.Auto) : null).Show();
    });

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _statusItem = new NativeMenuItem { IsEnabled = false };
        var menu = new NativeMenu
        {
            _statusItem,
            new NativeMenuItemSeparator(),
            Item("Abrir", ShowWindow),
            new NativeMenuItemSeparator(),
            Item("Automático", () => _host!.Coordinator.UserSetMode(DesiredMode.Auto)),
            Item("Somente recomendar", () => _host!.Coordinator.UserSetMode(DesiredMode.RecommendOnly)),
            Item("Pausar", () => _host!.Coordinator.UserPause()),
            new NativeMenuItemSeparator(),
            Item("Sair", () => desktop.Shutdown()),
        };

        _tray = new TrayIcon { Icon = AppIcon.Create(), Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowWindow()
    {
        _window!.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Status changes arrive often and from any thread; coalesce them into one UI refresh.</summary>
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
        }
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        var view = StatusPresenter.Present(_host!.Coordinator.Status, _host.Settings.Current, _host.LastFailure);
        _tray!.ToolTipText = view.TrayTooltip;
        _statusItem!.Header = view.Automation;
        _window!.Render(view);
    }
}
