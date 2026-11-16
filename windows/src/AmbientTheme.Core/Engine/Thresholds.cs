using AmbientTheme.Core.Protocol;

namespace AmbientTheme.Core.Engine;

/// <summary>Hysteresis band: dark strictly below <see cref="DarkBelowLux"/>, light strictly above <see cref="LightAboveLux"/>.</summary>
public readonly record struct Thresholds(double DarkBelowLux, double LightAboveLux)
{
    /// <summary>Refuses invalid values instead of silently correcting them.</summary>
    public static bool TryCreate(double darkBelowLux, double lightAboveLux, out Thresholds thresholds)
    {
        thresholds = new Thresholds(darkBelowLux, lightAboveLux);
        return double.IsFinite(darkBelowLux)
               && double.IsFinite(lightAboveLux)
               && darkBelowLux >= 0
               && darkBelowLux < lightAboveLux
               && lightAboveLux <= MeasurementValidator.MaxLux;
    }
}

public enum ThresholdPreset { DarkEarlier, Balanced, LightLonger, Personalized }

/// <summary>Proposed, uncalibrated presets (spec §19). Not medical or universal values.</summary>
public static class ThresholdPresets
{
    public static Thresholds For(ThresholdPreset preset) => preset switch
    {
        ThresholdPreset.DarkEarlier => new Thresholds(60, 150),
        ThresholdPreset.Balanced => new Thresholds(30, 80),
        ThresholdPreset.LightLonger => new Thresholds(15, 50),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), "Personalized has no fixed values."),
    };
}
