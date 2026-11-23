using AmbientTheme.Core.Presentation;

namespace AmbientTheme.Core.Tests.Presentation;

public class LuxScaleTests
{
    [Fact]
    public void Ends_of_the_scale_map_to_zero_and_one()
    {
        Assert.Equal(0, LuxScale.Position(0));
        Assert.Equal(1, LuxScale.Position(LuxScale.Max), 6);
    }

    [Fact]
    public void Out_of_range_readings_are_clamped()
    {
        Assert.Equal(0, LuxScale.Position(-5));
        Assert.Equal(1, LuxScale.Position(1_000_000));
    }

    [Fact]
    public void Scale_is_logarithmic_and_monotonic()
    {
        Assert.True(LuxScale.Position(10) < LuxScale.Position(100));
        Assert.True(LuxScale.Position(100) < LuxScale.Position(1000));
        Assert.True(LuxScale.Position(10) > 0.25, "dim rooms must not be crushed against the left edge");
    }
}
