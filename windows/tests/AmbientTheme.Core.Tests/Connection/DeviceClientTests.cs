using System.Collections.Concurrent;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Protocol;
using AmbientTheme.SensorSimulator;

namespace AmbientTheme.Core.Tests.Connection;

public sealed class DeviceClientTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private SimulatedSensorHost _host = null!;
    private readonly RecordingEvents _events = new();
    private readonly DeviceClient _client = new();
    private readonly CancellationTokenSource _stop = new();

    public async Task InitializeAsync() => _host = await SimulatedSensorHost.StartAsync();

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        await _host.DisposeAsync();
    }

    private SessionIdentity Identity(string credential = "good", string? pin = null, string? deviceId = null) =>
        new("pc-1", credential, deviceId ?? _host.DeviceId, pin ?? _host.CertificatePin);

    private Task<ConnectionFailure> Run(SessionIdentity identity) =>
        _client.RunSessionAsync(_host.Endpoint, identity, _events, _stop.Token);

    private sealed class RecordingEvents : ISessionEvents
    {
        public BlockingCollection<object> Items { get; } = [];

        public void OnAuthenticating() => Items.Add("authenticating");

        public void OnAuthenticated() => Items.Add("authenticated");

        public void OnReading(ValidatedReading reading) => Items.Add(reading);

        public void OnSensorStatus(SensorState state) => Items.Add(state);

        public void OnRejectedReading(RejectReason reason) => Items.Add(reason);

        public T Next<T>()
        {
            Assert.True(Items.TryTake(out var item, Wait), "No event arrived in time.");
            return Assert.IsType<T>(item);
        }
    }

    [Fact]
    public async Task Authenticated_session_delivers_validated_readings_and_acks_them()
    {
        _host.Authorize("pc-1", "good");
        var session = Run(Identity());
        Assert.Equal("authenticating", _events.Next<string>());
        Assert.Equal("authenticated", _events.Next<string>());

        var sent = await _host.SendReadingAsync(42, source: LightSource.LightSensor);

        Assert.Equal(42, _events.Next<ValidatedReading>().Lux);
        var ack = await _host.WaitForMessageAsync<Ack>(Wait);
        Assert.Equal(sent.MessageId, ack.CorrelationId);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task Sensor_status_is_reported_separately_from_readings()
    {
        _host.Authorize("pc-1", "good");
        _ = Run(Identity());
        _events.Next<string>();
        _events.Next<string>();

        await _host.SendStatusAsync(SensorState.Uncertain);

        Assert.Equal(SensorState.Uncertain, _events.Next<SensorState>());
    }

    [Fact]
    public async Task Simulated_or_stale_readings_never_reach_the_engine()
    {
        _host.Authorize("pc-1", "good");
        _ = Run(Identity());
        _events.Next<string>();
        _events.Next<string>();

        await _host.SendReadingAsync(42, source: LightSource.Simulated);
        await _host.SendReadingAsync(42, ageMs: 60_000, source: LightSource.LightSensor);

        Assert.Equal(RejectReason.UnsupportedSource, _events.Next<RejectReason>());
        Assert.Equal(RejectReason.StaleEvent, _events.Next<RejectReason>());
    }

    [Fact]
    public async Task Unknown_certificate_is_refused_before_any_credential_is_sent()
    {
        _host.Authorize("pc-1", "good");

        var failure = await Run(Identity(pin: new string('0', 64))).WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.TlsIdentityMismatch, failure);
        Assert.Empty(_host.Received);
    }

    [Fact]
    public async Task Revoked_or_wrong_credential_fails_authentication()
    {
        _host.Authorize("pc-1", "good");
        _host.Revoke("pc-1");

        var failure = await Run(Identity()).WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.AuthFailed, failure);
        Assert.DoesNotContain("authenticated", _events.Items);
    }

    [Fact]
    public async Task Another_device_behind_the_same_address_is_refused()
    {
        _host.Authorize("pc-1", "good");

        var failure = await Run(Identity(deviceId: "some-other-device")).WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.DeviceMismatch, failure);
        Assert.DoesNotContain(_host.Received, message => message is Auth);
    }

    [Fact]
    public async Task Dropped_connection_ends_the_session_as_closed()
    {
        _host.Authorize("pc-1", "good");
        var session = Run(Identity());
        _events.Next<string>();
        _events.Next<string>();

        await _host.DropConnectionsAsync();

        Assert.Equal(ConnectionFailure.Closed, await session.WaitAsync(Wait));
    }

    [Fact]
    public async Task Oversized_or_malformed_message_ends_the_session_as_protocol_violation()
    {
        _host.Authorize("pc-1", "good");
        var session = Run(Identity());
        _events.Next<string>();
        _events.Next<string>();

        await _host.SendRawAsync(new byte[ProtocolConstants.MaxMessageBytes + 100]);

        Assert.Equal(ConnectionFailure.ProtocolViolation, await session.WaitAsync(Wait));
    }

    [Fact]
    public async Task Nothing_listening_is_reported_as_refused()
    {
        var endpoint = _host.Endpoint;
        await _host.DisposeAsync();

        var failure = await _client.RunSessionAsync(endpoint, Identity(), _events, _stop.Token).WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.Refused, failure);
    }

    [Fact]
    public async Task Pairing_with_a_scanned_invite_and_confirmed_fingerprint_yields_a_working_credential()
    {
        var invite = PairingInvite.Create("pc-1", "Desktop", DateTimeOffset.UtcNow);
        _host.ArmInvite(invite);
        string? shown = null;

        var result = await _client.PairAsync(_host.Endpoint, invite, code =>
        {
            shown = code;
            return Task.FromResult(true);
        }, _stop.Token).WaitAsync(Wait);

        Assert.Null(result.Failure);
        Assert.Equal(CertificatePin.DisplayCode(_host.CertificatePin), shown);
        Assert.Equal(_host.DeviceId, result.DeviceId);
        Assert.Equal(_host.CertificatePin, result.CertificatePin);

        _ = Run(new SessionIdentity("pc-1", result.Credential!, result.DeviceId!, result.CertificatePin!));
        _events.Next<string>();
        Assert.Equal("authenticated", _events.Next<string>());
    }

    [Fact]
    public async Task Invite_token_is_never_sent_when_the_user_rejects_the_fingerprint()
    {
        var invite = PairingInvite.Create("pc-1", "Desktop", DateTimeOffset.UtcNow);
        _host.ArmInvite(invite);

        var result = await _client.PairAsync(_host.Endpoint, invite, _ => Task.FromResult(false), _stop.Token)
            .WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.TlsIdentityMismatch, result.Failure);
        Assert.Null(result.Credential);
        Assert.Empty(_host.Received);
    }

    [Fact]
    public async Task Invite_that_the_phone_never_scanned_is_rejected()
    {
        var invite = PairingInvite.Create("pc-1", "Desktop", DateTimeOffset.UtcNow);

        var result = await _client.PairAsync(_host.Endpoint, invite, _ => Task.FromResult(true), _stop.Token)
            .WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.AuthFailed, result.Failure);
    }

    [Fact]
    public async Task Invite_cannot_be_used_twice()
    {
        var invite = PairingInvite.Create("pc-1", "Desktop", DateTimeOffset.UtcNow);
        _host.ArmInvite(invite);
        await _client.PairAsync(_host.Endpoint, invite, _ => Task.FromResult(true), _stop.Token).WaitAsync(Wait);

        var second = await _client.PairAsync(_host.Endpoint, invite, _ => Task.FromResult(true), _stop.Token)
            .WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.AuthFailed, second.Failure);
    }

    [Fact]
    public async Task Expired_invite_is_rejected()
    {
        var invite = PairingInvite.Create("pc-1", "Desktop", DateTimeOffset.UtcNow.AddMinutes(-5));
        _host.ArmInvite(invite);

        var result = await _client.PairAsync(_host.Endpoint, invite, _ => Task.FromResult(true), _stop.Token)
            .WaitAsync(Wait);

        Assert.Equal(ConnectionFailure.AuthFailed, result.Failure);
    }
}
