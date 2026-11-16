using AmbientTheme.Core.Theme;
using AmbientTheme.Windows;
using Microsoft.Win32;

namespace AmbientTheme.Windows.Tests;

/// <summary>
/// Exercises the real registry APIs against a scratch key under HKCU, so the user's actual theme is never touched.
/// </summary>
public sealed class RegistryIntegrationTests : IDisposable
{
    private const string Root = @"Software\AmbientThemeController.Tests";
    private readonly string _path = $@"{Root}\{Guid.NewGuid():N}";

    public RegistryIntegrationTests() => Registry.CurrentUser.CreateSubKey(_path).Dispose();

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    private RegistryThemeRegistry Store() => new(_path);

    private void SetRaw(string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_path, writable: true)!;
        key.SetValue(name, value, kind);
    }

    private sealed class NoContrast : IContrastProbe
    {
        public bool IsContrastThemeActive() => false;
    }

    private sealed class Journal : IThemeJournal
    {
        public ThemeMutation? Pending { get; private set; }

        public void Begin(ThemeMutation mutation) => Pending = mutation;

        public void Complete() => Pending = null;
    }

    [Fact]
    public void Missing_values_read_as_null()
    {
        Assert.Equal(new ThemeSnapshot(null, null), Store().Read());
    }

    [Fact]
    public void Values_are_written_as_dwords_without_elevation()
    {
        var store = Store();

        store.WriteApps(0);
        store.WriteSystem(1);

        Assert.Equal(new ThemeSnapshot(0, 1), store.Read());
        using var key = Registry.CurrentUser.OpenSubKey(_path)!;
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("AppsUseLightTheme"));
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("SystemUsesLightTheme"));
    }

    [Fact]
    public void Corrupt_value_types_read_as_null()
    {
        SetRaw("AppsUseLightTheme", "dark", RegistryValueKind.String);
        SetRaw("SystemUsesLightTheme", 7, RegistryValueKind.DWord);

        Assert.Equal(new ThemeSnapshot(null, null), Store().Read());
    }

    [Fact]
    public void Adapter_applies_both_values_through_the_real_registry()
    {
        var store = Store();
        store.WriteApps(1);
        store.WriteSystem(1);

        var result = new ThemeAdapter(store, new NoContrast(), new Journal()).Apply(ThemeKind.Dark);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.Equal(new ThemeSnapshot(0, 0), Store().Read());
    }

    [Fact]
    public async Task Watcher_coalesces_a_burst_and_rearms_for_the_next_change()
    {
        var notifications = new List<DateTime>();
        using var signal = new SemaphoreSlim(0);
        using var watcher = new RegistryChangeWatcher(_path, () =>
        {
            lock (notifications)
            {
                notifications.Add(DateTime.UtcNow);
            }

            signal.Release();
        }, TimeSpan.FromMilliseconds(150));

        Store().WriteApps(0);
        Store().WriteSystem(0);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(400);
        Assert.Single(notifications);

        Store().WriteApps(1);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Watcher_stops_notifying_after_dispose()
    {
        var count = 0;
        var watcher = new RegistryChangeWatcher(_path, () => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(50));
        watcher.Dispose();

        Store().WriteApps(0);
        await Task.Delay(300);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Own_write_echo_is_unchanged_but_a_later_external_write_is_detected()
    {
        var store = Store();
        store.WriteApps(1);
        store.WriteSystem(1);
        var adapter = new ThemeAdapter(store, new NoContrast(), new Journal());
        var observations = new List<Observation>();
        using var signal = new SemaphoreSlim(0);
        using var watcher = new RegistryChangeWatcher(_path, () =>
        {
            observations.Add(adapter.Observe());
            signal.Release();
        }, TimeSpan.FromMilliseconds(100));

        adapter.Apply(ThemeKind.Dark);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        SetRaw("AppsUseLightTheme", 1, RegistryValueKind.DWord);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal([Observation.Unchanged, Observation.ExternalChange], observations);
    }

    [Fact]
    public void High_contrast_probe_answers_without_throwing()
    {
        _ = new HighContrastProbe().IsContrastThemeActive();
    }
}
