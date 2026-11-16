using System.Text;
using AmbientTheme.Core.Protocol;

namespace AmbientTheme.Core.Tests.Protocol;

public class MessageCodecTests
{
    private const string ReadingPrefix =
        "{\"type\":\"LIGHT_READING\",\"protocolVersion\":1,\"messageId\":\"m\",\"sessionId\":\"s\",\"sequence\":1,\"measuredAt\":\"2026-11-16T10:00:00Z\",";

    private static DecodeResult DecodeJson(string json) => MessageCodec.Decode(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void LightReading_roundtrips_all_fields()
    {
        var reading = new LightReading("s1", 7, DateTimeOffset.Parse("2026-11-16T10:00:00Z"), 42.5,
            LightSource.LightSensor, ReadingQuality.Good, 120);

        var decoded = MessageCodec.Decode(MessageCodec.Encode(reading));

        Assert.Null(decoded.Error);
        Assert.Equal(reading, decoded.Message);
    }

    [Fact]
    public void Hello_roundtrips_identity_and_capabilities()
    {
        var hello = new Hello("dev-1", "s1", "0.1.0", ["LIGHT_SENSOR"]);

        var decoded = Assert.IsType<Hello>(MessageCodec.Decode(MessageCodec.Encode(hello)).Message);

        Assert.Equal("dev-1", decoded.DeviceId);
        Assert.Equal("s1", decoded.SessionId);
        Assert.Equal(["LIGHT_SENSOR"], decoded.Capabilities);
        Assert.Equal(hello.MessageId, decoded.MessageId);
    }

    [Theory]
    [MemberData(nameof(OtherMessages))]
    public void Other_messages_roundtrip(ProtocolMessage message)
    {
        Assert.Equal(message, MessageCodec.Decode(MessageCodec.Encode(message)).Message);
    }

    public static TheoryData<ProtocolMessage> OtherMessages() => new()
    {
        new Auth("pc-1", "secret"),
        new AuthResult(false, "REVOKED"),
        new SensorStatus("s1", SensorState.Uncertain, 90000, "SCREEN_OFF"),
        new SensorStatus("s1", SensorState.Active, null, null),
        new Ack("abc"),
        new ProtocolError("abc", "STALE", "old"),
        new PairRequest("pc-1", "Desktop", "token"),
        new PairApprove("dev-1", "credential"),
        new PairRevoke("pc-1"),
    };

    [Fact]
    public void Encoded_message_carries_type_and_protocol_version()
    {
        var json = Encoding.UTF8.GetString(MessageCodec.Encode(new Ack("abc")));

        Assert.Contains("\"type\":\"ACK\"", json);
        Assert.Contains("\"protocolVersion\":1", json);
    }

    [Fact]
    public void Oversized_payload_is_rejected_without_parsing()
    {
        var payload = new byte[ProtocolConstants.MaxMessageBytes + 1];

        Assert.Equal(DecodeError.TooLarge, MessageCodec.Decode(payload).Error);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"protocolVersion\":1}")]
    [InlineData("{\"type\":\"ACK\",\"protocolVersion\":1}")]
    [InlineData(ReadingPrefix + "\"lux\":\"NaN\",\"source\":\"LIGHT_SENSOR\",\"quality\":\"GOOD\",\"sensorEventAgeMs\":1}")]
    [InlineData(ReadingPrefix + "\"lux\":1,\"source\":\"MAGIC\",\"quality\":\"GOOD\",\"sensorEventAgeMs\":1}")]
    public void Malformed_payload_is_rejected(string json)
    {
        Assert.Equal(DecodeError.Malformed, DecodeJson(json).Error);
    }

    [Fact]
    public void Unsupported_version_is_rejected()
    {
        Assert.Equal(DecodeError.UnsupportedVersion,
            DecodeJson("{\"type\":\"ACK\",\"protocolVersion\":2,\"messageId\":\"m\",\"correlationId\":\"c\"}").Error);
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        Assert.Equal(DecodeError.UnknownType,
            DecodeJson("{\"type\":\"SET_THEME\",\"protocolVersion\":1,\"messageId\":\"m\"}").Error);
    }
}
