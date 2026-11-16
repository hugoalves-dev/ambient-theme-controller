namespace AmbientTheme.Core.Automation;

/// <summary>Persistent user intent. Never cleared by network or sensor failures.</summary>
public enum DesiredMode { Auto, RecommendOnly, ManualPause }

public enum PauseReason { None, UserPaused, ExternalThemeChange }

/// <summary>Volatile operational state, independent from <see cref="DesiredMode"/>.</summary>
public enum RuntimeState
{
    Unpaired,
    Discovering,
    Connecting,
    Authenticating,
    WaitingForSensor,
    Stabilizing,
    Ready,
    Active,
    Degraded,
    Disconnected,
    Reconnecting,
}
