using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace AmbientTheme.App;

/// <summary>
/// The sun-and-moon logo as vector shapes on a 120x120 canvas (same geometry as docs/brand/logo.svg).
/// With <see cref="IsAnimated"/> the theme's splash animation applies: rays turn, sun breathes, moon glides in.
/// </summary>
internal sealed class LogoMark : UserControl
{
    private const double Extent = 120;
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F5A524"));

    private readonly Canvas _rays;
    private readonly Ellipse _sun;
    private readonly Ellipse _moon;

    public LogoMark()
    {
        _rays = new Canvas
        {
            Width = Extent,
            Height = Extent,
            RenderTransform = new RotateTransform(),
            RenderTransformOrigin = RelativePoint.Center,
            Classes = { "logo-rays" },
        };
        for (var i = 0; i < 8; i++)
        {
            var ray = new Rectangle
            {
                Width = 8,
                Height = 16,
                RadiusX = 4,
                RadiusY = 4,
                Fill = Amber,
                RenderTransform = new RotateTransform(i * 45),
                // Rotate about the logo's centre (60,60), which is 3.5 ray-heights below the ray's top.
                RenderTransformOrigin = new RelativePoint(0.5, 3.5, RelativeUnit.Relative),
            };
            Canvas.SetLeft(ray, 56);
            Canvas.SetTop(ray, 4);
            _rays.Children.Add(ray);
        }

        _sun = new Ellipse
        {
            Width = 60,
            Height = 60,
            Fill = new RadialGradientBrush
            {
                Center = new RelativePoint(0.38, 0.35, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.38, 0.35, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#FFD27A"), 0), new GradientStop(Color.Parse("#F5A524"), 1) },
            },
            RenderTransform = new ScaleTransform(),
            RenderTransformOrigin = RelativePoint.Center,
            Classes = { "logo-sun" },
        };
        Canvas.SetLeft(_sun, 30);
        Canvas.SetTop(_sun, 30);

        _moon = new Ellipse
        {
            Width = 48,
            Height = 48,
            Fill = new SolidColorBrush(Color.Parse("#141821")),
            RenderTransform = new TranslateTransform(),
            Classes = { "logo-moon" },
        };
        Canvas.SetLeft(_moon, 52);
        Canvas.SetTop(_moon, 24);

        Content = new Viewbox
        {
            Child = new Canvas { Width = Extent, Height = Extent, Children = { _rays, _sun, _moon } },
        };
    }

    /// <summary>Moon colour; match it to the surface behind the logo so the moon reads as a cut-out.</summary>
    public IBrush? MoonBrush
    {
        get => _moon.Fill;
        set => _moon.Fill = value;
    }

    public bool IsAnimated
    {
        get => _sun.Classes.Contains("animated");
        set
        {
            foreach (var part in new Control[] { _rays, _sun, _moon })
            {
                part.Classes.Set("animated", value);
            }
        }
    }
}
