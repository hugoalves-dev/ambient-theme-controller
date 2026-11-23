namespace AmbientTheme.Core.Presentation;

/// <summary>Maps lux onto a 0..1 gauge. Logarithmic, because the eye perceives light that way.</summary>
public static class LuxScale
{
    /// <summary>Upper end of the gauge; brighter readings sit at the right edge.</summary>
    public const double Max = 2000;

    public static double Position(double lux) =>
        Math.Clamp(Math.Log10(Math.Max(lux, 0) + 1) / Math.Log10(Max + 1), 0, 1);
}
