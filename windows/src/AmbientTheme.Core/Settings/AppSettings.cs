using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Settings;

/// <summary>
/// Public facts about the paired Android. The credential itself lives in <see cref="ISecretStore"/>.
/// Endpoints are mutable hints, never identity.
/// </summary>
public sealed record PairedDevice(string DeviceId, string Name, string CertificatePin, IReadOnlyList<string> KnownEndpoints);

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ClientId { get; init; } = Guid.NewGuid().ToString("N");
    public DesiredMode DesiredMode { get; init; } = DesiredMode.RecommendOnly;
    public PauseReason PauseReason { get; init; } = PauseReason.None;
    public ThresholdPreset Preset { get; init; } = ThresholdPreset.Balanced;
    public double DarkBelowLux { get; init; } = 30;
    public double LightAboveLux { get; init; } = 80;
    public int DwellSeconds { get; init; } = 30;
    public int CooldownSeconds { get; init; } = 600;
    public bool NotificationsEnabled { get; init; } = true;
    public PairedDevice? PairedDevice { get; init; }
    public string? ManualEndpoint { get; init; }
    public ThemeSnapshot? LastObservedTheme { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid =>
        Thresholds.TryCreate(DarkBelowLux, LightAboveLux, out _)
        && DwellSeconds >= 0
        && CooldownSeconds >= 0
        && !string.IsNullOrWhiteSpace(ClientId)
        && Enum.IsDefined(DesiredMode)
        && Enum.IsDefined(PauseReason)
        && Enum.IsDefined(Preset);
}

/// <summary>Protected storage for per-computer credentials (DPAPI on Windows).</summary>
public interface ISecretStore
{
    string? Read(string name);
    void Write(string name, string secret);
    void Delete(string name);
}
