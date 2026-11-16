using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;
using AmbientTheme.SensorSimulator;

namespace AmbientTheme.Core.Tests.Connection;

public sealed class ConnectionSupervisorTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeNotifier _notifier = new();
    private readonly FakeThemeController _theme = new(ThemeKind.Light);
    private readonly Dictionary<string, string> _secrets = [];
    private readonly CancellationTokenSource _stop = new();
    private SimulatedSensorHost _host = null!;
    private SettingsRepository _settings = null!;
    private AutomationCoordinator _coordinator = null!;
    private Endpoint? _discovered;

    public async Task InitializeAsync()
    {
        _host = await SimulatedSensorHost.StartAsync();
        _host.Authorize("pc-1", "good");
        _settings = new SettingsRepository(new AppSettings
        {
            ClientId = "pc-1",
            DesiredMode = DesiredMode.Auto,
            PairedDevice = new PairedDevice(_host.DeviceId, "Sim", _host.CertificatePin, [_host.Endpoint.ToString()]),
        }, _ => { });
        _secrets[ConnectionSupervisor.CredentialName] = "good";
        _coordinator = new AutomationCoordinator(_settings, _theme, _notifier);
    }

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        await _host.DisposeAsync();
    }

    private sealed class Secrets(Dictionary<string, string> values) : ISecretStore
    {
        public string? Read(string name) => values.GetValueOrDefault(name);

        public void Write(string name, string secret) => values[name] = secret;

        public void Delete(string name) => values.Remove(name);
    }

    private ConnectionSupervisor Supervisor() => new(
        new DeviceClient(), _coordinator, _settings, new Secrets(_secrets),
        discovered: () => _discovered is null ? [] : [_discovered],
        delay: (_, token) => Task.Delay(50, token));

    private async Task WaitForState(RuntimeState expected)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (_coordinator.Status.State != expected)
        {
            Assert.True(DateTime.UtcNow < deadline, $"State stayed {_coordinator.Status.State}, expected {expected}.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Supervisor_connects_and_feeds_readings_into_the_coordinator()
    {
        _ = Supervisor().RunAsync(_stop.Token);
        await WaitForState(RuntimeState.WaitingForSensor);

        await _host.SendReadingAsync(12, source: LightSource.LightSensor);

        await WaitForState(RuntimeState.Stabilizing);
        Assert.Equal(12, _coordinator.Status.Lux);
    }

    [Fact]
    public async Task Supervisor_reconnects_after_a_drop()
    {
        _ = Supervisor().RunAsync(_stop.Token);
        await WaitForState(RuntimeState.WaitingForSensor);
        await _host.SendReadingAsync(12, source: LightSource.LightSensor);
        await WaitForState(RuntimeState.Stabilizing);

        await _host.DropConnectionsAsync();

        await WaitForState(RuntimeState.WaitingForSensor);
        Assert.Null(_coordinator.Status.Lux);
    }

    [Fact]
    public async Task Supervisor_follows_the_same_device_to_a_new_address()
    {
        var supervisor = Supervisor();
        _ = supervisor.RunAsync(_stop.Token);
        await WaitForState(RuntimeState.WaitingForSensor);

        await using var moved = await SimulatedSensorHost.StartAsync(_host.Identity);
        moved.Authorize("pc-1", "good");
        _discovered = moved.Endpoint;
        await _host.DisposeAsync();

        await moved.WaitForMessageAsync<Auth>(Wait);
        await WaitForState(RuntimeState.WaitingForSensor);
        Assert.Contains(moved.Endpoint.ToString(), _settings.Current.PairedDevice!.KnownEndpoints);
    }

    [Fact]
    public async Task Revoked_credential_stops_the_retry_loop_and_requires_repairing()
    {
        _host.Revoke("pc-1");

        await Supervisor().RunAsync(_stop.Token).WaitAsync(Wait);

        Assert.Equal(FatalFailure.AuthFailed, _coordinator.Status.Fatal);
        Assert.Equal([NotificationKind.PairingRejected], _notifier.Sent);
    }

    [Fact]
    public async Task Unreachable_phone_keeps_retrying_and_reports_the_last_failure()
    {
        var endpoint = _host.Endpoint;
        await _host.DisposeAsync();
        var supervisor = Supervisor();
        var failures = 0;
        supervisor.AttemptFailed += (_, _) => Interlocked.Increment(ref failures);

        _ = supervisor.RunAsync(_stop.Token);
        var deadline = DateTime.UtcNow + Wait;
        while (Volatile.Read(ref failures) < 3)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(20);
        }

        Assert.Equal((endpoint, ConnectionFailure.Refused), supervisor.LastFailure);
        Assert.Null(_coordinator.Status.Fatal);
    }

    [Fact]
    public async Task Supervisor_does_nothing_while_unpaired()
    {
        _settings.Update(settings => settings with { PairedDevice = null });

        await Supervisor().RunAsync(_stop.Token).WaitAsync(Wait);

        Assert.Equal(RuntimeState.Unpaired, _coordinator.Status.State);
    }
}
