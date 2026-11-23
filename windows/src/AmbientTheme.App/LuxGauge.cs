using AmbientTheme.Core.Presentation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AmbientTheme.App;

/// <summary>
/// Night-to-day track on a logarithmic lux scale, with the dark/light thresholds marked and the live reading as a
/// knob. No reading means no knob: the gauge never invents a value.
/// </summary>
internal sealed class LuxGauge : Control
{
    public static readonly StyledProperty<double?> LuxProperty = AvaloniaProperty.Register<LuxGauge, double?>(nameof(Lux));
    public static readonly StyledProperty<double> DarkBelowProperty = AvaloniaProperty.Register<LuxGauge, double>(nameof(DarkBelow));
    public static readonly StyledProperty<double> LightAboveProperty = AvaloniaProperty.Register<LuxGauge, double>(nameof(LightAbove));

    private const double TrackHeight = 10;
    private const double KnobRadius = 9;
    private static readonly IBrush Track = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#2C3550"), 0),
            new GradientStop(Color.Parse("#5B6B9A"), 0.45),
            new GradientStop(Color.Parse("#F5A524"), 0.8),
            new GradientStop(Color.Parse("#FFE3A8"), 1),
        },
    };
    private static readonly IPen ThresholdPen = new Pen(new SolidColorBrush(Color.Parse("#9AA3B2")), 2, lineCap: PenLineCap.Round);
    private static readonly IPen KnobPen = new Pen(new SolidColorBrush(Color.Parse("#141821")), 3);
    private static readonly IBrush KnobFill = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9AA3B2"));

    static LuxGauge() => AffectsRender<LuxGauge>(LuxProperty, DarkBelowProperty, LightAboveProperty);

    public double? Lux
    {
        get => GetValue(LuxProperty);
        set => SetValue(LuxProperty, value);
    }

    public double DarkBelow
    {
        get => GetValue(DarkBelowProperty);
        set => SetValue(DarkBelowProperty, value);
    }

    public double LightAbove
    {
        get => GetValue(LightAboveProperty);
        set => SetValue(LightAboveProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 44);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width - 2 * KnobRadius;
        if (width <= 0)
        {
            return;
        }

        const double trackTop = KnobRadius - TrackHeight / 2 + 2;
        double X(double lux) => KnobRadius + LuxScale.Position(lux) * width;

        context.DrawRectangle(Track, null, new Rect(KnobRadius, trackTop, width, TrackHeight), TrackHeight / 2, TrackHeight / 2);

        foreach (var (lux, label) in new[] { (DarkBelow, "escuro"), (LightAbove, "claro") })
        {
            var x = X(lux);
            context.DrawLine(ThresholdPen, new Point(x, trackTop - 4), new Point(x, trackTop + TrackHeight + 4));
            var text = new FormattedText($"{label} {lux:0} lx", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
            var left = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, Bounds.Width - text.Width));
            context.DrawText(text, new Point(left, trackTop + TrackHeight + 8));
        }

        if (Lux is { } reading)
        {
            context.DrawEllipse(KnobFill, KnobPen, new Point(X(reading), trackTop + TrackHeight / 2), KnobRadius, KnobRadius);
        }
    }
}
