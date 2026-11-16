namespace AmbientTheme.Core.Protocol;

public enum RejectReason
{
    None,
    NoSession,
    DeviceMismatch,
    SessionMismatch,
    SequenceNotIncreasing,
    InvalidLux,
    LuxOutOfRange,
    InvalidAge,
    StaleEvent,
    UnsupportedSource,
    PoorQuality,
}

/// <summary>A reading that passed every check and may enter the recommendation engine.</summary>
public sealed record ValidatedReading(double Lux, long Sequence, LightSource Source);

public sealed record ValidationResult(RejectReason Reason, ValidatedReading? Reading)
{
    public bool IsValid => Reason == RejectReason.None;

    public static ValidationResult Reject(RejectReason reason) => new(reason, null);
}

/// <summary>
/// Gatekeeper between the transport and the engine: only readings from the paired device,
/// the current session, in order, fresh at the source and physically plausible get through.
/// </summary>
public sealed class MeasurementValidator(string pairedDeviceId, bool allowSimulated = false)
{
    public const double MaxLux = 200_000;
    public const long MaxEventAgeMs = 10_000;

    private string? _sessionId;
    private long _lastSequence;

    public RejectReason AcceptHello(Hello hello)
    {
        if (!string.Equals(hello.DeviceId, pairedDeviceId, StringComparison.Ordinal))
        {
            _sessionId = null;
            return RejectReason.DeviceMismatch;
        }

        _sessionId = hello.SessionId;
        _lastSequence = long.MinValue;
        return RejectReason.None;
    }

    public bool IsCurrentSession(SensorStatus status) =>
        _sessionId is not null && status.SessionId == _sessionId;

    public ValidationResult Validate(LightReading reading)
    {
        var reason = Check(reading);
        if (reason != RejectReason.None)
        {
            return ValidationResult.Reject(reason);
        }

        _lastSequence = reading.Sequence;
        return new ValidationResult(RejectReason.None, new ValidatedReading(reading.Lux, reading.Sequence, reading.Source));
    }

    private RejectReason Check(LightReading reading)
    {
        if (_sessionId is null)
        {
            return RejectReason.NoSession;
        }

        if (reading.SessionId != _sessionId)
        {
            return RejectReason.SessionMismatch;
        }

        if (reading.Sequence <= _lastSequence)
        {
            return RejectReason.SequenceNotIncreasing;
        }

        if (!double.IsFinite(reading.Lux) || reading.Lux < 0)
        {
            return RejectReason.InvalidLux;
        }

        if (reading.Lux > MaxLux)
        {
            return RejectReason.LuxOutOfRange;
        }

        if (reading.SensorEventAgeMs < 0)
        {
            return RejectReason.InvalidAge;
        }

        if (reading.SensorEventAgeMs > MaxEventAgeMs)
        {
            return RejectReason.StaleEvent;
        }

        if (!IsSupported(reading.Source))
        {
            return RejectReason.UnsupportedSource;
        }

        return reading.Quality == ReadingQuality.Good ? RejectReason.None : RejectReason.PoorQuality;
    }

    private bool IsSupported(LightSource source) =>
        source == LightSource.LightSensor || (source == LightSource.Simulated && allowSimulated);
}
