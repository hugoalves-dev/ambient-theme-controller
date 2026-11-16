using System.Text.Json;
using AmbientTheme.Core.Engine;

namespace AmbientTheme.Core.Settings;

/// <summary>
/// Versioned export/import of non-sensitive preferences (contingency for reinstall or edition change).
/// Identity, pairing and credentials are never exported; pairing must be redone securely.
/// </summary>
public static class SettingsExporter
{
    private const int ExportVersion = 1;

    public static string Export(AppSettings settings) => JsonSerializer.Serialize(
        new ExportedSettings(ExportVersion, settings.Preset, settings.DarkBelowLux, settings.LightAboveLux,
            settings.DwellSeconds, settings.CooldownSeconds, settings.NotificationsEnabled, settings.ManualEndpoint),
        SettingsStore.Json);

    /// <returns>The merged settings, or null when the export is unreadable, newer or invalid.</returns>
    public static AppSettings? Import(string json, AppSettings current)
    {
        ExportedSettings? exported;
        try
        {
            exported = JsonSerializer.Deserialize<ExportedSettings>(json, SettingsStore.Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (exported is null || exported.ExportVersion != ExportVersion)
        {
            return null;
        }

        var merged = current with
        {
            Preset = exported.Preset,
            DarkBelowLux = exported.DarkBelowLux,
            LightAboveLux = exported.LightAboveLux,
            DwellSeconds = exported.DwellSeconds,
            CooldownSeconds = exported.CooldownSeconds,
            NotificationsEnabled = exported.NotificationsEnabled,
            ManualEndpoint = exported.ManualEndpoint,
        };

        return merged.IsValid ? merged : null;
    }

    private sealed record ExportedSettings(
        int ExportVersion,
        ThresholdPreset Preset,
        double DarkBelowLux,
        double LightAboveLux,
        int DwellSeconds,
        int CooldownSeconds,
        bool NotificationsEnabled,
        string? ManualEndpoint);
}
