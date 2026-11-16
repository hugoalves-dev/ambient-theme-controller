using Microsoft.Win32;

namespace ThemeProbe;

public interface IThemeRegistry
{
    int? Read(string name);
    void Write(string name, int value);
}

public record ThemeValues(int? Apps, int? System);
public record ApplyResult(bool Success, ThemeValues Values, string? Error = null);

public sealed class ThemeController(IThemeRegistry registry)
{
    public const string AppsName = "AppsUseLightTheme";
    public const string SystemName = "SystemUsesLightTheme";

    public ThemeValues Read() => new(registry.Read(AppsName), registry.Read(SystemName));

    public ApplyResult Apply(bool light)
    {
        int target = light ? 1 : 0;
        try
        {
            var before = Read();
            if (before.Apps != target) registry.Write(AppsName, target);
            if (before.System != target) registry.Write(SystemName, target);
            var after = Read();
            return new(after.Apps == target && after.System == target, after,
                after.Apps == target && after.System == target ? null : "Readback mismatch");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            try { return new(false, Read(), ex.Message); }
            catch (Exception readEx) when (readEx is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            { return new(false, new(null, null), $"{ex.Message}; readback: {readEx.Message}"); }
        }
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsThemeRegistry : IThemeRegistry
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public int? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) is int value ? value : null;
    }
    public void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true)
            ?? throw new System.IO.IOException("Personalize registry key unavailable");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
