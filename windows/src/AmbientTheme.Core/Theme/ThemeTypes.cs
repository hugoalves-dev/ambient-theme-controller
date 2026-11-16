namespace AmbientTheme.Core.Theme;

public enum ThemeKind { Light, Dark }

/// <summary>
/// Raw values of <c>AppsUseLightTheme</c> and <c>SystemUsesLightTheme</c> (0 = dark, 1 = light, null = missing).
/// </summary>
public sealed record ThemeSnapshot(int? Apps, int? System)
{
    /// <summary>The single theme both values agree on, or null when mixed, missing or invalid.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ThemeKind? Unified => (Apps, System) switch
    {
        (1, 1) => ThemeKind.Light,
        (0, 0) => ThemeKind.Dark,
        _ => null,
    };

    public static ThemeSnapshot Of(ThemeKind theme)
    {
        var value = ToRegistryValue(theme);
        return new ThemeSnapshot(value, value);
    }

    public static int ToRegistryValue(ThemeKind theme) => theme == ThemeKind.Light ? 1 : 0;
}
