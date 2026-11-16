using AmbientTheme.Core.Protocol;

namespace AmbientTheme.Core.Tests.Protocol;

public class MeasurementValidatorTests
{
    private static readonly Hello Hello = new("dev-1", "s1", "0.1.0", []);

    private static LightReading Reading(long sequence = 1, double lux = 50, string session = "s1",
        long ageMs = 100, LightSource source = LightSource.LightSensor, ReadingQuality quality = ReadingQuality.Good)
        => new(session, sequence, DateTimeOffset.UnixEpoch, lux, source, quality, ageMs);

    private static MeasurementValidator StartedValidator(bool allowSimulated = false)
    {
        var validator = new MeasurementValidator("dev-1", allowSimulated);
        Assert.Equal(RejectReason.None, validator.AcceptHello(Hello));
        return validator;
    }

    [Fact]
    public void Valid_reading_is_accepted()
    {
        var result = StartedValidator().Validate(Reading(lux: 42));

        Assert.Equal(RejectReason.None, result.Reason);
        Assert.Equal(42, result.Reading!.Lux);
    }

    [Fact]
    public void Reading_before_hello_is_rejected()
    {
        Assert.Equal(RejectReason.NoSession, new MeasurementValidator("dev-1").Validate(Reading()).Reason);
    }

    [Fact]
    public void Hello_from_another_device_is_rejected()
    {
        var validator = new MeasurementValidator("dev-1");

        Assert.Equal(RejectReason.DeviceMismatch, validator.AcceptHello(Hello with { DeviceId = "dev-2" }));
        Assert.Equal(RejectReason.NoSession, validator.Validate(Reading()).Reason);
    }

    [Fact]
    public void Reading_from_previous_session_is_rejected()
    {
        Assert.Equal(RejectReason.SessionMismatch, StartedValidator().Validate(Reading(session: "old")).Reason);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, 4)]
    public void Repeated_or_older_sequence_is_rejected(long first, long second)
    {
        var validator = StartedValidator();
        validator.Validate(Reading(first));

        Assert.Equal(RejectReason.SequenceNotIncreasing, validator.Validate(Reading(second)).Reason);
    }

    [Fact]
    public void New_hello_starts_a_new_sequence()
    {
        var validator = StartedValidator();
        validator.Validate(Reading(9));
        validator.AcceptHello(Hello with { SessionId = "s2" });

        Assert.Equal(RejectReason.None, validator.Validate(Reading(1, session: "s2")).Reason);
    }

    [Theory]
    [InlineData(double.NaN, RejectReason.InvalidLux)]
    [InlineData(double.PositiveInfinity, RejectReason.InvalidLux)]
    [InlineData(-1, RejectReason.InvalidLux)]
    [InlineData(200_001, RejectReason.LuxOutOfRange)]
    public void Invalid_lux_is_rejected(double lux, RejectReason expected)
    {
        Assert.Equal(expected, StartedValidator().Validate(Reading(lux: lux)).Reason);
    }

    [Theory]
    [InlineData(-1, RejectReason.InvalidAge)]
    [InlineData(10_001, RejectReason.StaleEvent)]
    public void Incoherent_or_stale_event_age_is_rejected(long ageMs, RejectReason expected)
    {
        Assert.Equal(expected, StartedValidator().Validate(Reading(ageMs: ageMs)).Reason);
    }

    [Fact]
    public void Rejected_reading_does_not_consume_the_sequence()
    {
        var validator = StartedValidator();
        validator.Validate(Reading(3, ageMs: 60_000));

        Assert.Equal(RejectReason.None, validator.Validate(Reading(3)).Reason);
    }

    [Fact]
    public void Simulated_source_is_rejected_by_default()
    {
        Assert.Equal(RejectReason.UnsupportedSource,
            StartedValidator().Validate(Reading(source: LightSource.Simulated)).Reason);
    }

    [Fact]
    public void Simulated_source_is_accepted_only_when_explicitly_allowed()
    {
        Assert.Equal(RejectReason.None,
            StartedValidator(allowSimulated: true).Validate(Reading(source: LightSource.Simulated)).Reason);
    }

    [Fact]
    public void Camera_estimate_is_not_supported_in_the_mvp()
    {
        Assert.Equal(RejectReason.UnsupportedSource,
            StartedValidator().Validate(Reading(source: LightSource.CameraEstimate)).Reason);
    }

    [Fact]
    public void Non_good_quality_is_rejected()
    {
        Assert.Equal(RejectReason.PoorQuality,
            StartedValidator().Validate(Reading(quality: ReadingQuality.Uncertain)).Reason);
    }

    [Fact]
    public void Status_from_another_session_is_not_current()
    {
        var validator = StartedValidator();

        Assert.True(validator.IsCurrentSession(new SensorStatus("s1", SensorState.Active, null, null)));
        Assert.False(validator.IsCurrentSession(new SensorStatus("old", SensorState.Active, null, null)));
    }
}
