// Diagnostic tool for the device PoCs (issues #4 and #5): pairs with a phone and prints what the Windows
// client receives, using the same DeviceClient as the product. It never touches the Windows theme.
//
//   LinkProbe <host:port>            pair (prints the invite to scan or paste on the phone), then listen
//   LinkProbe <host:port> --seconds 60
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Protocol;

if (args.Length == 0 || !Endpoint.TryParse(args[0], out var endpoint))
{
    Console.WriteLine("Usage: LinkProbe <host:port> [--seconds N]");
    return 1;
}

var seconds = args.Length == 3 && args[1] == "--seconds" && int.TryParse(args[2], out var parsed) ? parsed : 30;
var client = new DeviceClient();
var invite = PairingInvite.Create("linkprobe-" + Environment.MachineName.ToLowerInvariant(), "LinkProbe", DateTimeOffset.UtcNow);

Console.WriteLine("Invite (scan as QR or paste on the phone), valid for 2 minutes:");
Console.WriteLine(invite.ToUri());
Console.WriteLine("Press Enter after the phone accepted the invite...");
Console.ReadLine();

var pairing = await client.PairAsync(endpoint, invite, code =>
{
    Console.WriteLine($"Certificate code presented by {endpoint}: {code}");
    Console.WriteLine("(the probe accepts it automatically; in the app the user compares it with the phone)");
    return Task.FromResult(true);
}, CancellationToken.None);

if (pairing.Failure is { } failure)
{
    Console.WriteLine($"PAIRING FAILED: {failure}");
    return 2;
}

Console.WriteLine($"PAIRED with device {pairing.DeviceId}, pin {pairing.CertificatePin}");
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
var identity = new SessionIdentity(invite.ClientId, pairing.Credential!, pairing.DeviceId!, pairing.CertificatePin!);
var ended = await client.RunSessionAsync(endpoint, identity, new Printer(), stop.Token);
Console.WriteLine($"SESSION ENDED: {ended}");
return 0;

internal sealed class Printer : ISessionEvents
{
    private static void Log(string text) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {text}");

    public void OnAuthenticating() => Log("authenticating");

    public void OnAuthenticated() => Log("AUTHENTICATED");

    public void OnReading(ValidatedReading reading) => Log($"READING #{reading.Sequence} {reading.Lux} lx ({reading.Source})");

    public void OnSensorStatus(SensorState state) => Log($"status {state}");

    public void OnRejectedReading(RejectReason reason) => Log($"REJECTED reading: {reason}");
}
