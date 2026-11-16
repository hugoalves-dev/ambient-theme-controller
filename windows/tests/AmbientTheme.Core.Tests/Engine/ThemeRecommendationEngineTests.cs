using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Engine;

public class ThemeRecommendationEngineTests
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private static ThemeRecommendationEngine Engine() =>
        new(ThresholdPresets.For(ThresholdPreset.Balanced), Dwell, Cooldown);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData(5, ThemeKind.Dark)]
    [InlineData(25, ThemeKind.Dark)]
    [InlineData(100, ThemeKind.Light)]
    [InlineData(200, ThemeKind.Light)]
    public void Poc_samples_outside_the_band_produce_a_candidate(double lux, ThemeKind expected)
    {
        var observed = expected == ThemeKind.Dark ? ThemeKind.Light : ThemeKind.Dark;

        Assert.Equal(expected, Engine().Evaluate(lux, observed, At(0)).Target);
    }

    [Theory]
    [InlineData(50, ThemeKind.Light)]
    [InlineData(50, ThemeKind.Dark)]
    [InlineData(30, ThemeKind.Light)]
    [InlineData(80, ThemeKind.Dark)]
    public void Intermediate_band_and_exact_limits_hold_the_theme(double lux, ThemeKind observed)
    {
        Assert.Null(Engine().Evaluate(lux, observed, At(0)).Target);
    }

    [Fact]
    public void Candidate_equal_to_observed_theme_requests_nothing()
    {
        Assert.Null(Engine().Evaluate(5, ThemeKind.Dark, At(0)).Target);
    }

    [Fact]
    public void Unknown_observed_theme_still_gets_a_candidate()
    {
        Assert.Equal(ThemeKind.Dark, Engine().Evaluate(5, null, At(0)).Target);
    }

    [Fact]
    public void Candidate_is_not_ready_before_the_dwell_elapses()
    {
        var engine = Engine();
        engine.Evaluate(5, ThemeKind.Light, At(0));

        Assert.False(engine.Evaluate(5, ThemeKind.Light, At(29)).MayApply);
    }

    [Fact]
    public void Candidate_is_ready_after_sustained_dwell()
    {
        var engine = Engine();
        engine.Evaluate(5, ThemeKind.Light, At(0));

        Assert.True(engine.Evaluate(6, ThemeKind.Light, At(30)).MayApply);
    }

    [Fact]
    public void Hold_sample_restarts_the_dwell()
    {
        var engine = Engine();
        engine.Evaluate(5, ThemeKind.Light, At(0));
        engine.Evaluate(50, ThemeKind.Light, At(20));
        engine.Evaluate(5, ThemeKind.Light, At(25));

        Assert.False(engine.Evaluate(5, ThemeKind.Light, At(40)).MayApply);
        Assert.True(engine.Evaluate(5, ThemeKind.Light, At(55)).MayApply);
    }

    [Fact]
    public void Opposite_candidate_restarts_the_dwell()
    {
        var engine = Engine();
        engine.Evaluate(5, null, At(0));
        engine.Evaluate(100, null, At(20));

        Assert.False(engine.Evaluate(100, null, At(40)).MayApply);
    }

    [Fact]
    public void Reset_discards_the_pending_candidate()
    {
        var engine = Engine();
        engine.Evaluate(5, ThemeKind.Light, At(0));
        engine.Reset();

        Assert.False(engine.Evaluate(5, ThemeKind.Light, At(31)).MayApply);
    }

    [Fact]
    public void Cooldown_blocks_a_quick_flip_back()
    {
        var engine = Engine();
        engine.NotifyApplied(At(100));
        engine.Evaluate(100, ThemeKind.Dark, At(110));

        var recommendation = engine.Evaluate(100, ThemeKind.Dark, At(200));

        Assert.True(recommendation.Stable);
        Assert.True(recommendation.InCooldown);
        Assert.False(recommendation.MayApply);
    }

    [Fact]
    public void Change_is_allowed_once_the_cooldown_ends()
    {
        var engine = Engine();
        engine.NotifyApplied(At(100));
        engine.Evaluate(100, ThemeKind.Dark, At(110));

        Assert.True(engine.Evaluate(100, ThemeKind.Dark, At(700)).MayApply);
    }

    [Fact]
    public void Flapping_around_a_threshold_never_becomes_ready()
    {
        var engine = Engine();
        for (var second = 0; second < 300; second += 10)
        {
            var lux = second % 20 == 0 ? 29 : 31;
            Assert.False(engine.Evaluate(lux, ThemeKind.Light, At(second)).MayApply);
        }
    }

    [Theory]
    [InlineData(ThresholdPreset.DarkEarlier, 60, 150)]
    [InlineData(ThresholdPreset.Balanced, 30, 80)]
    [InlineData(ThresholdPreset.LightLonger, 15, 50)]
    public void Presets_match_the_proposed_values(ThresholdPreset preset, double dark, double light)
    {
        Assert.Equal(new Thresholds(dark, light), ThresholdPresets.For(preset));
    }

    [Theory]
    [InlineData(80, 30)]
    [InlineData(30, 30)]
    [InlineData(-1, 30)]
    [InlineData(double.NaN, 30)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(10, 200_001)]
    public void Invalid_thresholds_are_refused(double dark, double light)
    {
        Assert.False(Thresholds.TryCreate(dark, light, out _));
    }

    [Fact]
    public void Valid_custom_thresholds_are_accepted()
    {
        Assert.True(Thresholds.TryCreate(10, 40, out var thresholds));
        Assert.Equal(new Thresholds(10, 40), thresholds);
    }
}
