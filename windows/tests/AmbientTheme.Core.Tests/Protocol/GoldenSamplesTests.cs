using System.Text;
using System.Text.Json;
using AmbientTheme.Core.Protocol;

namespace AmbientTheme.Core.Tests.Protocol;

/// <summary>
/// docs/protocol/golden-v1.json is the contract shared with the Android implementation, whose tests read
/// the same file. A change that breaks either side fails here or there.
/// </summary>
public class GoldenSamplesTests
{
    public static TheoryData<string, string> Samples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "protocol", "golden-v1.json")))
        {
            directory = directory.Parent;
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory!.FullName, "docs", "protocol", "golden-v1.json")));
        var data = new TheoryData<string, string>();
        foreach (var sample in document.RootElement.EnumerateObject())
        {
            data.Add(sample.Name, sample.Value.GetRawText());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Golden_sample_decodes_and_survives_a_roundtrip(string name, string json)
    {
        var decoded = MessageCodec.Decode(Encoding.UTF8.GetBytes(json));
        Assert.True(decoded.Error is null, $"{name}: {decoded.Error}");

        var again = MessageCodec.Decode(MessageCodec.Encode(decoded.Message!)).Message!;

        Assert.Equal(decoded.Message!.GetType(), again.GetType());
        Assert.Equal(decoded.Message.MessageId, again.MessageId);
        Assert.Equal(Canonical(decoded.Message), Canonical(again));
    }

    [Fact]
    public void Golden_file_covers_every_message_type()
    {
        var types = Samples().Select(row => JsonDocument.Parse((string)row[1]).RootElement.GetProperty("type").GetString());

        Assert.Equal(
            ["ACK", "AUTH", "AUTH_RESULT", "ERROR", "HELLO", "LIGHT_READING", "PAIR_APPROVE", "PAIR_REQUEST", "PAIR_REVOKE", "SENSOR_STATUS"],
            types.Distinct().Order());
    }

    private static string Canonical(ProtocolMessage message) => Encoding.UTF8.GetString(MessageCodec.Encode(message));
}
