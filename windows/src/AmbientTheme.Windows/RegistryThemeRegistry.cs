using AmbientTheme.Core.Theme;
using Microsoft.Win32;

namespace AmbientTheme.Windows;

/// <summary>
/// Reads and writes <c>AppsUseLightTheme</c> / <c>SystemUsesLightTheme</c> under HKCU. No elevation is used;
/// a write refused by policy surfaces as an exception for the adapter to report.
/// </summary>
public sealed class RegistryThemeRegistry(string subKeyPath = RegistryThemeRegistry.PersonalizePath) : IThemeRegistry
{
    public const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsValue = "AppsUseLightTheme";
    public const string SystemValue = "SystemUsesLightTheme";

    public ThemeSnapshot Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath);
        return new ThemeSnapshot(ReadFlag(key, AppsValue), ReadFlag(key, SystemValue));
    }

    public void WriteApps(int value) => Write(AppsValue, value);

    public void WriteSystem(int value) => Write(SystemValue, value);

    private void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Only a DWORD 0 or 1 is a valid flag; anything else is reported as unknown.</summary>
    private static int? ReadFlag(RegistryKey? key, string name) =>
        key?.GetValue(name) is int value and (0 or 1) ? value : null;
}
