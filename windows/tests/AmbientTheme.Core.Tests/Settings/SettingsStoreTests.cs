using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "atc-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var result = new SettingsStore(FilePath).Load();

        Assert.Equal(SettingsLoadStatus.Created, result.Status);
        Assert.Equal(DesiredMode.RecommendOnly, result.Settings.DesiredMode);
        Assert.Equal(ThresholdPreset.Balanced, result.Settings.Preset);
        Assert.Equal(30, result.Settings.DarkBelowLux);
        Assert.Equal(80, result.Settings.LightAboveLux);
        Assert.Null(result.Settings.PairedDevice);
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        var saved = new AppSettings
        {
            DesiredMode = DesiredMode.ManualPause,
            PauseReason = PauseReason.ExternalThemeChange,
            Preset = ThresholdPreset.Personalized,
            DarkBelowLux = 12,
            LightAboveLux = 44,
            ManualEndpoint = "192.168.137.175:8766",
            LastObservedTheme = new ThemeSnapshot(0, 1),
            PairedDevice = new PairedDevice("dev-1", "Pixel", "PIN", ["10.0.0.2:8766"]),
        };
        new SettingsStore(FilePath).Save(saved);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(DesiredMode.ManualPause, loaded.Settings.DesiredMode);
        Assert.Equal(PauseReason.ExternalThemeChange, loaded.Settings.PauseReason);
        Assert.Equal(ThresholdPreset.Personalized, loaded.Settings.Preset);
        Assert.Equal(12, loaded.Settings.DarkBelowLux);
        Assert.Equal(44, loaded.Settings.LightAboveLux);
        Assert.Equal(new ThemeSnapshot(0, 1), loaded.Settings.LastObservedTheme);
        Assert.Equal("dev-1", loaded.Settings.PairedDevice!.DeviceId);
        Assert.Equal(["10.0.0.2:8766"], loaded.Settings.PairedDevice.KnownEndpoints);
        Assert.Equal(saved.ClientId, loaded.Settings.ClientId);
    }

    [Fact]
    public void Unknown_fields_from_other_versions_are_tolerated()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{\"schemaVersion\":1,\"desiredMode\":\"Auto\",\"futureField\":{\"a\":1}}");

        var result = new SettingsStore(FilePath).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(DesiredMode.Auto, result.Settings.DesiredMode);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"schemaVersion\":1,\"darkBelowLux\":90,\"lightAboveLux\":80}")]
    [InlineData("{\"schemaVersion\":1,\"dwellSeconds\":-5}")]
    public void Corrupt_or_invalid_file_is_backed_up_and_replaced_by_defaults(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content);

        var result = new SettingsStore(FilePath).Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, result.Status);
        Assert.Equal(30, result.Settings.DarkBelowLux);
        var backup = Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
        Assert.Equal(content, File.ReadAllText(backup));
    }

    [Fact]
    public void Corruption_never_resumes_automation_on_its_own()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "garbage");

        Assert.NotEqual(DesiredMode.Auto, new SettingsStore(FilePath).Load().Settings.DesiredMode);
    }

    [Fact]
    public void Newer_schema_is_never_overwritten()
    {
        Directory.CreateDirectory(_directory);
        const string newer = "{\"schemaVersion\":99,\"desiredMode\":\"Auto\"}";
        File.WriteAllText(FilePath, newer);
        var store = new SettingsStore(FilePath);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.NewerSchema, result.Status);
        Assert.Throws<InvalidOperationException>(() => store.Save(result.Settings));
        Assert.Equal(newer, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        new SettingsStore(FilePath).Save(new AppSettings());

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }
}
