using System.Net.WebSockets;
using AmbientTheme.Core.Protocol;

namespace AmbientTheme.Core.Connection;

/// <summary>What Windows knows about the paired Android and presents to it.</summary>
public sealed record SessionIdentity(string ClientId, string Credential, string DeviceId, string CertificatePin);

/// <summary>Callbacks of a live session. Only validated data crosses this boundary.</summary>
public interface ISessionEvents
{
    void OnAuthenticating();
    void OnAuthenticated();
    void OnReading(ValidatedReading reading);
    void OnSensorStatus(SensorState state);
    void OnRejectedReading(RejectReason reason);
}

public sealed record PairingResult(ConnectionFailure? Failure, string? DeviceId, string? Credential, string? CertificatePin)
{
    public static PairingResult Failed(ConnectionFailure failure) => new(failure, null, null, null);
}

/// <summary>
/// Outbound WSS client. Windows always initiates; the Android certificate must match the pinned identity
/// (no generic TLS exception), and nothing received here can change the theme directly.
/// </summary>
public sealed class DeviceClient
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Runs one session until it ends and reports why it ended.</summary>
    public async Task<ConnectionFailure> RunSessionAsync(Endpoint endpoint, SessionIdentity identity,
        ISessionEvents events, CancellationToken cancellation)
    {
        using var socket = CreateSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null && CertificatePin.Matches(certificate, identity.CertificatePin);

        if (await ConnectAsync(socket, endpoint, cancellation) is { } connectFailure)
        {
            return connectFailure;
        }

        events.OnAuthenticating();
        var validator = new MeasurementValidator(identity.DeviceId, AllowSimulatedReadings);
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            handshake.CancelAfter(HandshakeTimeout);
            if (await AuthenticateAsync(socket, identity, validator, handshake.Token) is { } handshakeFailure)
            {
                return handshakeFailure;
            }
        }

        events.OnAuthenticated();
        return await PumpAsync(socket, validator, events, cancellation);
    }

    /// <summary>Development switch: lets readings marked SIMULATED through. Never enabled by default.</summary>
    public bool AllowSimulatedReadings { get; init; }

    /// <summary>
    /// First pairing. The certificate is not known yet, so its fingerprint is shown to the user, who compares it
    /// with the code on the phone. The invite token leaves this computer only after that confirmation.
    /// </summary>
    public async Task<PairingResult> PairAsync(Endpoint endpoint, PairingInvite invite,
        Func<string, Task<bool>> confirmFingerprint, CancellationToken cancellation)
    {
        string? pin = null;
        using var socket = CreateSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            pin = certificate is null ? null : CertificatePin.Compute(certificate);
            return pin is not null;
        };

        if (await ConnectAsync(socket, endpoint, cancellation) is { } connectFailure)
        {
            return PairingResult.Failed(connectFailure);
        }

        try
        {
            if (await ReceiveAsync(socket, HandshakeTimeout, cancellation) is not Hello hello)
            {
                return PairingResult.Failed(ConnectionFailure.ProtocolViolation);
            }

            if (!await confirmFingerprint(CertificatePin.DisplayCode(pin!)))
            {
                return PairingResult.Failed(ConnectionFailure.TlsIdentityMismatch);
            }

            await SendAsync(socket, new PairRequest(invite.ClientId, invite.ClientName, invite.Token), cancellation);
            return await ReceiveAsync(socket, HandshakeTimeout, cancellation) switch
            {
                PairApprove approve when approve.DeviceId == hello.DeviceId =>
                    new PairingResult(null, approve.DeviceId, approve.Credential, pin),
                ProtocolError => PairingResult.Failed(ConnectionFailure.AuthFailed),
                _ => PairingResult.Failed(ConnectionFailure.ProtocolViolation),
            };
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return PairingResult.Failed(ConnectionFailure.Closed);
        }
        finally
        {
            await CloseQuietlyAsync(socket);
        }
    }

    private ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = KeepAlive;
        socket.Options.KeepAliveTimeout = KeepAlive;
        return socket;
    }

    private async Task<ConnectionFailure?> ConnectAsync(ClientWebSocket socket, Endpoint endpoint,
        CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            var uri = new UriBuilder("wss", endpoint.Host, endpoint.Port, "/v1").Uri;
            await socket.ConnectAsync(uri, timeout.Token);
            return null;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                              or HttpRequestException or UriFormatException)
        {
            return ConnectionErrors.Classify(exception);
        }
    }

    private static async Task<ConnectionFailure?> AuthenticateAsync(ClientWebSocket socket, SessionIdentity identity,
        MeasurementValidator validator, CancellationToken cancellation)
    {
        try
        {
            if (await ReceiveAsync(socket, cancellation) is not Hello hello)
            {
                return ConnectionFailure.ProtocolViolation;
            }

            if (validator.AcceptHello(hello) != RejectReason.None)
            {
                await CloseQuietlyAsync(socket);
                return ConnectionFailure.DeviceMismatch;
            }

            await SendAsync(socket, new Auth(identity.ClientId, identity.Credential), cancellation);
            return await ReceiveAsync(socket, cancellation) switch
            {
                AuthResult { Authorized: true } => null,
                AuthResult => ConnectionFailure.AuthFailed,
                _ => ConnectionFailure.ProtocolViolation,
            };
        }
        catch (OperationCanceledException)
        {
            return ConnectionFailure.Timeout;
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return ConnectionFailure.Closed;
        }
    }

    private static async Task<ConnectionFailure> PumpAsync(ClientWebSocket socket, MeasurementValidator validator,
        ISessionEvents events, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                switch (await ReceiveAsync(socket, cancellation))
                {
                    case null:
                        return socket.State == WebSocketState.Open
                            ? await ViolationAsync(socket)
                            : ConnectionFailure.Closed;
                    case LightReading reading:
                        var result = validator.Validate(reading);
                        if (result.IsValid)
                        {
                            events.OnReading(result.Reading!);
                            await SendAsync(socket, new Ack(reading.MessageId), cancellation);
                        }
                        else
                        {
                            events.OnRejectedReading(result.Reason);
                            await SendAsync(socket,
                                new ProtocolError(reading.MessageId, "READING_REJECTED", result.Reason.ToString()),
                                cancellation);
                        }

                        break;
                    case SensorStatus status when validator.IsCurrentSession(status):
                        events.OnSensorStatus(status.State);
                        break;
                }
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception) || exception is OperationCanceledException)
        {
            return ConnectionFailure.Closed;
        }
    }

    private static async Task<ConnectionFailure> ViolationAsync(ClientWebSocket socket)
    {
        await CloseQuietlyAsync(socket);
        return ConnectionFailure.ProtocolViolation;
    }

    private static async Task<ProtocolMessage?> ReceiveAsync(ClientWebSocket socket, TimeSpan timeout,
        CancellationToken cancellation)
    {
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limited.CancelAfter(timeout);
        return await ReceiveAsync(socket, limited.Token);
    }

    /// <returns>The decoded message, or null when the peer closed or sent something unacceptable.</returns>
    private static async Task<ProtocolMessage?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        var buffer = new byte[ProtocolConstants.MaxMessageBytes + 1];
        var length = 0;
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer.AsMemory(length), cancellation);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                await CloseQuietlyAsync(socket);
                return null;
            }

            length += frame.Count;
            if (frame.EndOfMessage)
            {
                return MessageCodec.Decode(buffer.AsSpan(0, length)).Message;
            }

            if (length == buffer.Length)
            {
                return null;
            }
        }
    }

    private static Task SendAsync(ClientWebSocket socket, ProtocolMessage message, CancellationToken cancellation) =>
        socket.SendAsync(MessageCodec.Encode(message), WebSocketMessageType.Text, true, cancellation);

    private static async Task CloseQuietlyAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception) || exception is OperationCanceledException)
        {
            // The connection is already gone; there is nothing left to close.
        }
    }

    private static bool IsTransportFailure(Exception exception) =>
        exception is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException;
}
