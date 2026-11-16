using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Engine;

/// <param name="Target">Theme the light level asks for, or null to keep the current one.</param>
/// <param name="Stable">The candidate has been sustained for the whole dwell period.</param>
/// <param name="InCooldown">An automatic change was applied too recently.</param>
public sealed record Recommendation(ThemeKind? Target, bool Stable, bool InCooldown)
{
    public static readonly Recommendation Hold = new(null, false, false);

    public bool MayApply => Target is not null && Stable && !InCooldown;
}

/// <summary>
/// Deterministic hysteresis + dwell + cooldown. It only recommends; authorising a write is the coordinator's job.
/// All times are local monotonic readings supplied by the caller.
/// </summary>
public sealed class ThemeRecommendationEngine(Thresholds thresholds, TimeSpan dwell, TimeSpan cooldown)
{
    private ThemeKind? _pending;
    private TimeSpan _pendingSince;
    private TimeSpan? _lastApplied;

    public Thresholds Thresholds { get; } = thresholds;

    public Recommendation Evaluate(double lux, ThemeKind? observed, TimeSpan now)
    {
        var candidate = Classify(lux);
        if (candidate is null || candidate == observed)
        {
            _pending = null;
            return Recommendation.Hold;
        }

        if (_pending != candidate)
        {
            _pending = candidate;
            _pendingSince = now;
        }

        var stable = now - _pendingSince >= dwell;
        var inCooldown = _lastApplied is { } applied && now - applied < cooldown;
        return new Recommendation(candidate, stable, inCooldown);
    }

    /// <summary>Starts the cooldown after an automatic change that was actually applied.</summary>
    public void NotifyApplied(TimeSpan now)
    {
        _lastApplied = now;
        _pending = null;
    }

    /// <summary>Drops the pending candidate, e.g. when the source stops being trustworthy.</summary>
    public void Reset() => _pending = null;

    private ThemeKind? Classify(double lux)
    {
        if (lux < Thresholds.DarkBelowLux)
        {
            return ThemeKind.Dark;
        }

        return lux > Thresholds.LightAboveLux ? ThemeKind.Light : null;
    }
}
