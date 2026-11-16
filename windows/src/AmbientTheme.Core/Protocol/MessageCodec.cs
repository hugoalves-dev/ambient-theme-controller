using System.Globalization;
using System.Text.Json;

namespace AmbientTheme.Core.Protocol;

public enum DecodeError { TooLarge, Malformed, UnsupportedVersion, UnknownType }

public sealed record DecodeResult(ProtocolMessage? Message, DecodeError? Error)
{
    public static DecodeResult Fail(DecodeError error) => new(null, error);
}

/// <summary>JSON wire codec for protocol v1. Decoding is strict: anything unexpected is rejected, never guessed.</summary>
public static class MessageCodec
{
    private static readonly WireEnum<LightSource> Sources = new(
        (LightSource.LightSensor, "LIGHT_SENSOR"),
        (LightSource.CameraEstimate, "CAMERA_ESTIMATE"),
        (LightSource.Simulated, "SIMULATED"));

    private static readonly WireEnum<ReadingQuality> Qualities = new(
        (ReadingQuality.Good, "GOOD"),
        (ReadingQuality.Uncertain, "UNCERTAIN"),
        (ReadingQuality.Invalid, "INVALID"));

    private static readonly WireEnum<SensorState> States = new(
        (SensorState.Active, "ACTIVE"),
        (SensorState.Unavailable, "UNAVAILABLE"),
        (SensorState.Uncertain, "UNCERTAIN"),
        (SensorState.Error, "ERROR"));

    public static byte[] Encode(ProtocolMessage message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", TypeName(message));
            writer.WriteNumber("protocolVersion", ProtocolConstants.Version);
            writer.WriteString("messageId", message.MessageId);
            WriteBody(writer, message);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static DecodeResult Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ProtocolConstants.MaxMessageBytes)
        {
            return DecodeResult.Fail(DecodeError.TooLarge);
        }

        try
        {
            var reader = new Utf8JsonReader(payload);
            using var document = JsonDocument.ParseValue(ref reader);
            return DecodeObject(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException
                                              or KeyNotFoundException or OverflowException)
        {
            return DecodeResult.Fail(DecodeError.Malformed);
        }
    }

    private static DecodeResult DecodeObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DecodeResult.Fail(DecodeError.Malformed);
        }

        var type = root.GetProperty("type").GetString() ?? throw new FormatException();
        if (root.GetProperty("protocolVersion").GetInt32() != ProtocolConstants.Version)
        {
            return DecodeResult.Fail(DecodeError.UnsupportedVersion);
        }

        var messageId = Text(root, "messageId");
        ProtocolMessage? message = type switch
        {
            "HELLO" => new Hello(Text(root, "deviceId"), Text(root, "sessionId"), Text(root, "appVersion"),
                root.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()!).ToArray()),
            "AUTH" => new Auth(Text(root, "clientId"), Text(root, "credential")),
            "AUTH_RESULT" => new AuthResult(root.GetProperty("authorized").GetBoolean(), OptionalText(root, "reason")),
            "LIGHT_READING" => new LightReading(
                Text(root, "sessionId"),
                root.GetProperty("sequence").GetInt64(),
                DateTimeOffset.Parse(Text(root, "measuredAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                root.GetProperty("lux").GetDouble(),
                Sources.Parse(Text(root, "source")),
                Qualities.Parse(Text(root, "quality")),
                root.GetProperty("sensorEventAgeMs").GetInt64()),
            "SENSOR_STATUS" => new SensorStatus(Text(root, "sessionId"), States.Parse(Text(root, "state")),
                OptionalNumber(root, "lastEventAgeMs"), OptionalText(root, "reason")),
            "ACK" => new Ack(Text(root, "correlationId")),
            "ERROR" => new ProtocolError(OptionalText(root, "correlationId"), Text(root, "code"), Text(root, "message")),
            "PAIR_REQUEST" => new PairRequest(Text(root, "clientId"), Text(root, "clientName"), Text(root, "inviteToken")),
            "PAIR_APPROVE" => new PairApprove(Text(root, "deviceId"), Text(root, "credential")),
            "PAIR_REVOKE" => new PairRevoke(Text(root, "clientId")),
            _ => null,
        };

        return message is null
            ? DecodeResult.Fail(DecodeError.UnknownType)
            : new DecodeResult(message with { MessageId = messageId }, null);
    }

    private static string TypeName(ProtocolMessage message) => message switch
    {
        Hello => "HELLO",
        Auth => "AUTH",
        AuthResult => "AUTH_RESULT",
        LightReading => "LIGHT_READING",
        SensorStatus => "SENSOR_STATUS",
        Ack => "ACK",
        ProtocolError => "ERROR",
        PairRequest => "PAIR_REQUEST",
        PairApprove => "PAIR_APPROVE",
        PairRevoke => "PAIR_REVOKE",
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    private static void WriteBody(Utf8JsonWriter writer, ProtocolMessage message)
    {
        switch (message)
        {
            case Hello hello:
                writer.WriteString("deviceId", hello.DeviceId);
                writer.WriteString("sessionId", hello.SessionId);
                writer.WriteString("appVersion", hello.AppVersion);
                writer.WriteStartArray("capabilities");
                foreach (var capability in hello.Capabilities)
                {
                    writer.WriteStringValue(capability);
                }

                writer.WriteEndArray();
                break;
            case Auth auth:
                writer.WriteString("clientId", auth.ClientId);
                writer.WriteString("credential", auth.Credential);
                break;
            case AuthResult result:
                writer.WriteBoolean("authorized", result.Authorized);
                WriteOptional(writer, "reason", result.Reason);
                break;
            case LightReading reading:
                writer.WriteString("sessionId", reading.SessionId);
                writer.WriteNumber("sequence", reading.Sequence);
                writer.WriteString("measuredAt", reading.MeasuredAt.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteNumber("lux", reading.Lux);
                writer.WriteString("source", Sources.Format(reading.Source));
                writer.WriteString("quality", Qualities.Format(reading.Quality));
                writer.WriteNumber("sensorEventAgeMs", reading.SensorEventAgeMs);
                break;
            case SensorStatus status:
                writer.WriteString("sessionId", status.SessionId);
                writer.WriteString("state", States.Format(status.State));
                if (status.LastEventAgeMs is { } age)
                {
                    writer.WriteNumber("lastEventAgeMs", age);
                }

                WriteOptional(writer, "reason", status.Reason);
                break;
            case Ack ack:
                writer.WriteString("correlationId", ack.CorrelationId);
                break;
            case ProtocolError error:
                WriteOptional(writer, "correlationId", error.CorrelationId);
                writer.WriteString("code", error.Code);
                writer.WriteString("message", error.Message);
                break;
            case PairRequest request:
                writer.WriteString("clientId", request.ClientId);
                writer.WriteString("clientName", request.ClientName);
                writer.WriteString("inviteToken", request.InviteToken);
                break;
            case PairApprove approve:
                writer.WriteString("deviceId", approve.DeviceId);
                writer.WriteString("credential", approve.Credential);
                break;
            case PairRevoke revoke:
                writer.WriteString("clientId", revoke.ClientId);
                break;
        }
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new FormatException(name);

    private static string? OptionalText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static long? OptionalNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt64() : null;

    private sealed class WireEnum<T>(params (T Value, string Name)[] map) where T : struct, Enum
    {
        public string Format(T value) => map.First(entry => entry.Value.Equals(value)).Name;

        public T Parse(string name)
        {
            foreach (var entry in map)
            {
                if (entry.Name == name)
                {
                    return entry.Value;
                }
            }

            throw new FormatException(name);
        }
    }
}
