using Xunit;

namespace ThemeProbe.Tests;

public class ThemeStoreTests
{
    [Fact]
    public void Matching_values_do_not_trigger_writes()
    {
        var store = new FakeStore(1, 1);
        var result = new ThemeController(store).Apply(true);
        Assert.True(result.Success);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Applying_theme_updates_and_verifies_both_values()
    {
        var store = new FakeStore(0, 0);
        var result = new ThemeController(store).Apply(true);
        Assert.True(result.Success);
        Assert.Equal(new ThemeValues(1, 1), result.Values);
        Assert.Equal(2, store.Writes);
    }

    [Fact]
    public void Partial_failure_is_reported_with_actual_values()
    {
        var store = new FakeStore(0, 0) { FailSystemWrite = true };
        var result = new ThemeController(store).Apply(true);
        Assert.False(result.Success);
        Assert.Equal(new ThemeValues(1, 0), result.Values);
    }

    private sealed class FakeStore(int apps, int system) : IThemeRegistry
    {
        public int Writes { get; private set; }
        public bool FailSystemWrite { get; init; }
        public int? Read(string name) => name == ThemeController.AppsName ? apps : system;
        public void Write(string name, int value)
        {
            Writes++;
            if (name == ThemeController.AppsName) apps = value;
            else if (!FailSystemWrite) system = value;
        }
    }
}
