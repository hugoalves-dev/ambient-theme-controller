namespace AmbientTheme.Core.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int MaxMessageBytes = 4096;
    public const string ServiceType = "_ambienttheme._tcp";
}

public enum LightSource { LightSensor, CameraEstimate, Simulated }

public enum ReadingQuality { Good, Uncertain, Invalid }

public enum SensorState { Active, Unavailable, Uncertain, Error }

/// <summary>Base of every wire message. <see cref="MessageId"/> lets the peer correlate ACKs and errors.</summary>
public abstract record ProtocolMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
}

/// <summary>Android → Windows, first message of a session.</summary>
public sealed record Hello(string DeviceId, string SessionId, string AppVersion, IReadOnlyList<string> Capabilities)
    : ProtocolMessage;

/// <summary>Windows → Android, presents the per-computer credential obtained at pairing.</summary>
public sealed record Auth(string ClientId, string Credential) : ProtocolMessage;

public sealed record AuthResult(bool Authorized, string? Reason) : ProtocolMessage;

/// <summary>A physical sensor event. <see cref="SensorEventAgeMs"/> is the age measured at the source, never reset on resend.</summary>
public sealed record LightReading(
    string SessionId,
    long Sequence,
    DateTimeOffset MeasuredAt,
    double Lux,
    LightSource Source,
    ReadingQuality Quality,
    long SensorEventAgeMs) : ProtocolMessage;

/// <summary>Service heartbeat and sensor health. It is never a new physical reading.</summary>
public sealed record SensorStatus(string SessionId, SensorState State, long? LastEventAgeMs, string? Reason)
    : ProtocolMessage;

public sealed record Ack(string CorrelationId) : ProtocolMessage;

public sealed record ProtocolError(string? CorrelationId, string Code, string Message) : ProtocolMessage;

public sealed record PairRequest(string ClientId, string ClientName, string InviteToken) : ProtocolMessage;

public sealed record PairApprove(string DeviceId, string Credential) : ProtocolMessage;

public sealed record PairRevoke(string ClientId) : ProtocolMessage;
