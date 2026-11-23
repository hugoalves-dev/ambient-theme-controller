using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AmbientTheme.App;

/// <summary>Animated vector splash shown while the app starts; skipped when launched with --tray.</summary>
public partial class SplashWindow : Window
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(1.8);

    public SplashWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        Caption.RenderTransform = new TranslateTransform();
    }

    /// <summary>Shows the splash, then runs <paramref name="next"/> and closes once the animation has played.</summary>
    public static void ShowThen(Action next)
    {
        var splash = new SplashWindow();
        splash.Show();
        DispatcherTimer.RunOnce(() =>
        {
            next();
            splash.Close();
        }, Duration);
    }
}
