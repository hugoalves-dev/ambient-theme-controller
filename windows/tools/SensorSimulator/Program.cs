// Development tool: plays the Android role of protocol v1 so the Windows app can be exercised without a phone.
// Readings sent from here are tagged SIMULATED and are refused for automation unless the Windows app is
// started with ATC_ALLOW_SIMULATED=1. It proves nothing about real sensors.
using System.Globalization;
using System.Net;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Protocol;
using AmbientTheme.SensorSimulator;

var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 8766;
await using var host = await SimulatedSensorHost.StartAsync(address: IPAddress.Any, port: port);

Console.WriteLine($"Simulated sensor listening on port {host.Endpoint.Port} (all interfaces), device {host.DeviceId}");
Console.WriteLine($"Fingerprint code to compare on Windows: {CertificatePin.DisplayCode(host.CertificatePin)}");
Console.WriteLine("Commands: invite <ambienttheme://...> | lux <value> | status <ACTIVE|UNAVAILABLE|UNCERTAIN|ERROR> | drop | revoke <clientId> | quit");

while (Console.ReadLine() is { } line)
{
    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    try
    {
        switch (parts)
        {
            case ["invite", var uri] when PairingInvite.TryParse(uri, out var invite):
                host.ArmInvite(invite);
                Console.WriteLine($"Invite from '{invite.ClientName}' armed; pair from Windows now.");
                break;
            case ["lux", var value] when double.TryParse(value, CultureInfo.InvariantCulture, out var lux):
                await host.SendReadingAsync(lux);
                break;
            case ["status", var state] when Enum.TryParse<SensorState>(state, ignoreCase: true, out var sensorState):
                await host.SendStatusAsync(sensorState);
                break;
            case ["drop"]:
                await host.DropConnectionsAsync();
                break;
            case ["revoke", var clientId]:
                host.Revoke(clientId);
                break;
            case ["quit"]:
                return;
            default:
                Console.WriteLine("Unknown command.");
                break;
        }
    }
    catch (TimeoutException)
    {
        Console.WriteLine("No authenticated Windows session yet.");
    }
}
