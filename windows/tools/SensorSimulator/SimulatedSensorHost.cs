using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Protocol;
using Endpoint = AmbientTheme.Core.Connection.Endpoint;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace AmbientTheme.SensorSimulator;

/// <summary>Stable identity of a simulated phone: device id plus its self-signed TLS certificate.</summary>
public sealed record SimulatedIdentity(string DeviceId, X509Certificate2 Certificate)
{
    public static SimulatedIdentity Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ambient-theme-simulator", key, HashAlgorithmName.SHA256);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // Re-import so the private key is usable by SChannel on Windows.
        var certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
        return new SimulatedIdentity("sim-" + Guid.NewGuid().ToString("N")[..12], certificate);
    }
}

/// <summary>
/// Reference implementation of the Android side of protocol v1 (WSS server, pairing, auth, readings).
/// It exists to test the Windows client without a phone. Its readings are simulated: by default they are
/// tagged <see cref="LightSource.Simulated"/>, which the Windows validator refuses for automation.
/// </summary>
public sealed class SimulatedSensorHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, string> _credentials = new();
    private readonly ConcurrentQueue<ProtocolMessage> _received = new();
    private readonly ConcurrentDictionary<WebSocket, byte> _sockets = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Lock _gate = new();
    private PairingInvite? _invite;
    private Session? _session;
    private TaskCompletionSource<Session> _sessionReady = NewSessionSignal();

    private SimulatedSensorHost(WebApplication app, SimulatedIdentity identity)
    {
        _app = app;
        Identity = identity;
    }

    public SimulatedIdentity Identity { get; }
    public string DeviceId => Identity.DeviceId;
    public string CertificatePin => Core.Connection.CertificatePin.Compute(Identity.Certificate);
    public Endpoint Endpoint { get; private set; } = new("127.0.0.1", 0);
    public IReadOnlyCollection<ProtocolMessage> Received => _received;

    public static async Task<SimulatedSensorHost> StartAsync(SimulatedIdentity? identity = null,
        IPAddress? address = null, int port = 0)
    {
        identity ??= SimulatedIdentity.Create();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(address ?? IPAddress.Loopback, port, listen => listen.UseHttps(identity.Certificate)));

        var app = builder.Build();
        var host = new SimulatedSensorHost(app, identity);
        app.UseWebSockets();
        app.Map("/v1", host.HandleAsync);
        await app.StartAsync();

        var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        host.Endpoint = new Endpoint((address ?? IPAddress.Loopback).ToString(), new Uri(bound).Port);
        return host;
    }

    /// <summary>Simulates the phone scanning the QR code shown by Windows.</summary>
    public void ArmInvite(PairingInvite invite)
    {
        lock (_gate)
        {
            _invite = invite;
        }
    }

    public void Authorize(string clientId, string credential) => _credentials[clientId] = credential;

    public void Revoke(string clientId) => _credentials.TryRemove(clientId, out _);

    public async Task<LightReading> SendReadingAsync(double lux, long ageMs = 50,
        LightSource source = LightSource.Simulated, ReadingQuality quality = ReadingQuality.Good)
    {
        var session = await CurrentSessionAsync();
        var reading = new LightReading(session.Id, session.NextSequence(), DateTimeOffset.UtcNow, lux, source, quality, ageMs);
        await SendAsync(session.Socket, MessageCodec.Encode(reading));
        return reading;
    }

    public async Task SendStatusAsync(SensorState state, long? lastEventAgeMs = null, string? reason = null)
    {
        var session = await CurrentSessionAsync();
        await SendAsync(session.Socket, MessageCodec.Encode(new SensorStatus(session.Id, state, lastEventAgeMs, reason)));
    }

    public async Task SendRawAsync(byte[] payload) => await SendAsync((await CurrentSessionAsync()).Socket, payload);

    public Task DropConnectionsAsync()
    {
        foreach (var socket in _sockets.Keys)
        {
            socket.Abort();
        }

        return Task.CompletedTask;
    }

    public async Task<T> WaitForMessageAsync<T>(TimeSpan timeout) where T : ProtocolMessage
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_received.OfType<T>().LastOrDefault() is { } message)
            {
                return message;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"No {typeof(T).Name} arrived within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        await DropConnectionsAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown took too long; disposing below releases the listener anyway.
        }

        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        _sockets[socket] = 0;
        try
        {
            var session = new Session(socket, Guid.NewGuid().ToString("N"));
            await SendAsync(socket, MessageCodec.Encode(new Hello(DeviceId, session.Id, "simulator", ["LIGHT_SENSOR"])));
            switch (await ReceiveAsync(socket))
            {
                case Auth auth:
                    await ServeSessionAsync(session, auth);
                    break;
                case PairRequest request:
                    await SendAsync(socket, MessageCodec.Encode(Pair(request)));
                    break;
            }

            await CloseAsync(socket);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            // The client went away; nothing to clean up beyond the socket itself.
        }
        finally
        {
            _sockets.TryRemove(socket, out _);
        }
    }

    private async Task ServeSessionAsync(Session session, Auth auth)
    {
        var authorized = _credentials.TryGetValue(auth.ClientId, out var expected) && expected == auth.Credential;
        await SendAsync(session.Socket, MessageCodec.Encode(new AuthResult(authorized, authorized ? null : "REVOKED")));
        if (!authorized)
        {
            return;
        }

        lock (_gate)
        {
            _session = session;
            _sessionReady.TrySetResult(session);
        }

        try
        {
            while (await ReceiveAsync(session.Socket) is not null)
            {
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_session == session)
                {
                    _session = null;
                    _sessionReady = NewSessionSignal();
                }
            }
        }
    }

    /// <summary>Approves only the invite that was scanned, once, before it expires.</summary>
    private ProtocolMessage Pair(PairRequest request)
    {
        lock (_gate)
        {
            if (_invite is not { } invite
                || invite.ClientId != request.ClientId
                || invite.IsExpired(DateTimeOffset.UtcNow)
                || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(invite.Token),
                    System.Text.Encoding.UTF8.GetBytes(request.InviteToken)))
            {
                return new ProtocolError(request.MessageId, "PAIRING_REJECTED", "Invite is unknown, used or expired.");
            }

            _invite = null;
        }

        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Authorize(request.ClientId, credential);
        return new PairApprove(DeviceId, credential);
    }

    private async Task<Session> CurrentSessionAsync()
    {
        Task<Session> ready;
        lock (_gate)
        {
            ready = _sessionReady.Task;
        }

        return await ready.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<ProtocolMessage?> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[ProtocolConstants.MaxMessageBytes];
        var frame = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (frame.MessageType == WebSocketMessageType.Close || !frame.EndOfMessage)
        {
            return null;
        }

        var message = MessageCodec.Decode(buffer.AsSpan(0, frame.Count)).Message;
        if (message is not null)
        {
            _received.Enqueue(message);
        }

        return message;
    }

    private async Task SendAsync(WebSocket socket, byte[] payload)
    {
        await _sendGate.WaitAsync();
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
        }
    }

    private static TaskCompletionSource<Session> NewSessionSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Session(WebSocket socket, string id)
    {
        private long _sequence;

        public WebSocket Socket { get; } = socket;
        public string Id { get; } = id;

        public long NextSequence() => Interlocked.Increment(ref _sequence);
    }
}
