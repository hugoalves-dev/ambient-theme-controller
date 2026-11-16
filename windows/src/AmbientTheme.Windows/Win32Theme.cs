using System.Runtime.InteropServices;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Windows;

/// <summary>Detects an active contrast (accessibility) theme, which automation must never override.</summary>
public sealed class HighContrastProbe : IContrastProbe
{
    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x00000001;

    public bool IsContrastThemeActive()
    {
        var info = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        return SystemParametersInfo(GetHighContrast, info.Size, ref info, 0) && (info.Flags & HighContrastOn) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref HighContrast info, uint winIni);
}

/// <summary>
/// Broadcasts <c>WM_SETTINGCHANGE("ImmersiveColorSet")</c> so shell and apps re-read the theme.
/// Best effort only: Windows does not guarantee a universal repaint (spec §18).
/// </summary>
public sealed class ThemeChangeBroadcaster : IThemeChangeBroadcaster
{
    private static readonly IntPtr Broadcast_ = new(0xFFFF);
    private const uint SettingChange = 0x001A;
    private const uint AbortIfHung = 0x0002;

    public void Broadcast() =>
        SendMessageTimeout(Broadcast_, SettingChange, IntPtr.Zero, "ImmersiveColorSet", AbortIfHung, 200, out _);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeoutMs, out IntPtr result);
}
