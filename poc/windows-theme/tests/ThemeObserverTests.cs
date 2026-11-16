using Xunit;

namespace ThemeProbe.Tests;

public class ThemeObserverTests
{
    [Fact]
    public void Unchanged_values_are_not_reported_as_changes()
    {
        var observer = new ThemeObserver(new ThemeValues(0, 0));
        Assert.False(observer.Observe(new ThemeValues(0, 0)));
    }

    [Fact]
    public void Either_theme_change_is_reported_once()
    {
        var observer = new ThemeObserver(new ThemeValues(0, 0));
        Assert.True(observer.Observe(new ThemeValues(1, 0)));
        Assert.False(observer.Observe(new ThemeValues(1, 0)));
        Assert.True(observer.Observe(new ThemeValues(1, 1)));
    }

    [Fact]
    public void Missing_value_is_reported_as_a_change()
    {
        var observer = new ThemeObserver(new ThemeValues(1, 1));
        Assert.True(observer.Observe(new ThemeValues(null, 1)));
    }
}
