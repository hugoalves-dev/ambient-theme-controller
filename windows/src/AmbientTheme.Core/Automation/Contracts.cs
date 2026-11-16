using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Automation;

/// <summary>The only door through which the coordinator touches the Windows theme.</summary>
public interface IThemeController
{
    ThemeSnapshot Current { get; }
    ApplyResult Apply(ThemeKind theme);
}

public enum NotificationKind
{
    SensorUnavailable,
    SensorRecovered,
    ExternalThemeChange,
    ThemeApplyFailed,
    PairingRejected,
}

/// <summary>Shows a notification. Implementations must not block: they are called under the coordinator lock.</summary>
public interface INotifier
{
    void Notify(NotificationKind kind);
}

/// <summary>Failures that must not be retried aggressively and require a new, explicit pairing.</summary>
public enum FatalFailure { AuthFailed, TlsIdentityMismatch, DeviceMismatch }

public sealed record CoordinatorStatus(
    RuntimeState State,
    DesiredMode Mode,
    PauseReason PauseReason,
    double? Lux,
    SensorState? Sensor,
    Recommendation Recommendation,
    bool ApplyBlocked,
    bool ContrastBlocked,
    FatalFailure? Fatal);

/// <summary>In-memory current settings plus a persistence callback, shared by the coordinator and the UI.</summary>
public sealed class SettingsRepository(AppSettings initial, Action<AppSettings> persist)
{
    private readonly Lock _gate = new();
    private AppSettings _current = initial;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        lock (_gate)
        {
            var updated = change(_current);
            if (updated == _current)
            {
                return;
            }

            _current = updated;
            persist(updated);
        }
    }
}
