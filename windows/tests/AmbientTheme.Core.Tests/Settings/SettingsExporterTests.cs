using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Settings;

namespace AmbientTheme.Core.Tests.Settings;

public class SettingsExporterTests
{
    private static readonly AppSettings Source = new()
    {
        DesiredMode = DesiredMode.Auto,
        Preset = ThresholdPreset.Personalized,
        DarkBelowLux = 12,
        LightAboveLux = 44,
        DwellSeconds = 45,
        CooldownSeconds = 300,
        NotificationsEnabled = false,
        ManualEndpoint = "10.0.0.2:8766",
        PairedDevice = new PairedDevice("dev-1", "Pixel", "PIN-VALUE", ["10.0.0.2:8766"]),
    };

    [Fact]
    public void Export_then_import_restores_preferences()
    {
        var imported = SettingsExporter.Import(SettingsExporter.Export(Source), new AppSettings());

        Assert.NotNull(imported);
        Assert.Equal(ThresholdPreset.Personalized, imported.Preset);
        Assert.Equal(12, imported.DarkBelowLux);
        Assert.Equal(44, imported.LightAboveLux);
        Assert.Equal(45, imported.DwellSeconds);
        Assert.Equal(300, imported.CooldownSeconds);
        Assert.False(imported.NotificationsEnabled);
        Assert.Equal("10.0.0.2:8766", imported.ManualEndpoint);
    }

    [Fact]
    public void Export_contains_no_identity_or_pairing_material()
    {
        var json = SettingsExporter.Export(Source);

        Assert.DoesNotContain("PIN-VALUE", json);
        Assert.DoesNotContain(Source.ClientId, json);
        Assert.DoesNotContain("dev-1", json);
    }

    [Fact]
    public void Import_keeps_local_identity_pairing_and_mode()
    {
        var current = new AppSettings { DesiredMode = DesiredMode.ManualPause, PauseReason = PauseReason.UserPaused };

        var imported = SettingsExporter.Import(SettingsExporter.Export(Source), current)!;

        Assert.Equal(current.ClientId, imported.ClientId);
        Assert.Equal(DesiredMode.ManualPause, imported.DesiredMode);
        Assert.Equal(PauseReason.UserPaused, imported.PauseReason);
        Assert.Null(imported.PairedDevice);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("{\"exportVersion\":99,\"darkBelowLux\":1,\"lightAboveLux\":2}")]
    [InlineData("{\"exportVersion\":1,\"darkBelowLux\":50,\"lightAboveLux\":20,\"dwellSeconds\":30,\"cooldownSeconds\":600}")]
    public void Unreadable_newer_or_invalid_export_is_refused(string json)
    {
        Assert.Null(SettingsExporter.Import(json, new AppSettings()));
    }
}
