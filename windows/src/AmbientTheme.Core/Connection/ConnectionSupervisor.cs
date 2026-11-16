using System.Diagnostics;
using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;

namespace AmbientTheme.Core.Connection;

/// <summary>Local monotonic time shared by the supervisor and the coordinator tick.</summary>
public static class MonotonicClock
{
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    public static TimeSpan Now => Stopwatch.Elapsed;
}

/// <summary>
/// Keeps one outbound session to the paired Android alive: resolves candidates again on every attempt,
/// backs off with jitter, never scans networks or opens an inbound port, and stops for good when the
/// phone rejects the credential (revocation needs a new pairing).
/// </summary>
public sealed class ConnectionSupervisor(
    DeviceClient client,
    AutomationCoordinator coordinator,
    SettingsRepository settings,
    ISecretStore secrets,
    Func<IReadOnlyList<Endpoint>>? discovered = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<TimeSpan>? clock = null)
{
    public const string CredentialName = "device-credential";
    private const int MaxKnownEndpoints = 5;

    private readonly BackoffPolicy _backoff = new();
    private readonly AutomationCoordinator _coordinator = coordinator;
    private readonly Func<TimeSpan> _clock = clock ?? (() => MonotonicClock.Now);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private Endpoint? _lastActive;

    /// <summary>Raised after each failed attempt, for diagnostics. May fire on any thread.</summary>
    public event Action<Endpoint, ConnectionFailure>? AttemptFailed;

    public (Endpoint Endpoint, ConnectionFailure Failure)? LastFailure { get; private set; }

    public async Task RunAsync(CancellationToken cancellation)
    {
        var failedRounds = 0;
        while (!cancellation.IsCancellationRequested)
        {
            if (settings.Current.PairedDevice is not { } device)
            {
                return;
            }

            if (secrets.Read(CredentialName) is not { } credential)
            {
                _coordinator.OnFatalFailure(FatalFailure.AuthFailed, _clock());
                return;
            }

            var identity = new SessionIdentity(settings.Current.ClientId, credential, device.DeviceId, device.CertificatePin);
            _coordinator.OnConnecting(_clock());
            var outcome = await TryCandidatesAsync(Candidates(device), identity, cancellation);
            if (cancellation.IsCancellationRequested || outcome == RoundOutcome.Fatal)
            {
                return;
            }

            _coordinator.OnTransportLost(_clock());
            failedRounds = outcome == RoundOutcome.HadSession ? 0 : failedRounds + 1;
            try
            {
                await _delay(_backoff.Delay(failedRounds), cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private enum RoundOutcome { NoSession, HadSession, Fatal }

    private async Task<RoundOutcome> TryCandidatesAsync(IReadOnlyList<Endpoint> candidates, SessionIdentity identity,
        CancellationToken cancellation)
    {
        foreach (var endpoint in candidates)
        {
            var bridge = new Bridge(this, endpoint);
            var failure = await client.RunSessionAsync(endpoint, identity, bridge, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                return RoundOutcome.NoSession;
            }

            if (bridge.Authenticated)
            {
                return RoundOutcome.HadSession;
            }

            LastFailure = (endpoint, failure);
            AttemptFailed?.Invoke(endpoint, failure);

            // The pinned identity answered and refused us: this is a revocation, not a wrong address.
            if (failure == ConnectionFailure.AuthFailed)
            {
                _coordinator.OnFatalFailure(FatalFailure.AuthFailed, _clock());
                return RoundOutcome.Fatal;
            }
        }

        return RoundOutcome.NoSession;
    }

    private IReadOnlyList<Endpoint> Candidates(PairedDevice device)
    {
        Endpoint.TryParse(settings.Current.ManualEndpoint, out var manual);
        return EndpointResolver.Order(
            _lastActive,
            discovered?.Invoke() ?? [],
            device.KnownEndpoints.Select(Parse).OfType<Endpoint>(),
            manual.Port == 0 ? null : manual);
    }

    private static Endpoint? Parse(string text) => Endpoint.TryParse(text, out var endpoint) ? endpoint : null;

    private void Remember(Endpoint endpoint)
    {
        _lastActive = endpoint;
        LastFailure = null;
        var text = endpoint.ToString();
        settings.Update(current => current.PairedDevice is not { } device || device.KnownEndpoints.FirstOrDefault() == text
            ? current
            : current with
            {
                PairedDevice = device with
                {
                    KnownEndpoints = [text, .. device.KnownEndpoints.Where(known => known != text).Take(MaxKnownEndpoints - 1)],
                },
            });
    }

    private sealed class Bridge(ConnectionSupervisor owner, Endpoint endpoint) : ISessionEvents
    {
        public bool Authenticated { get; private set; }

        public void OnAuthenticating() => owner._coordinator.OnAuthenticating(owner._clock());

        public void OnAuthenticated()
        {
            Authenticated = true;
            owner.Remember(endpoint);
            owner._coordinator.OnAuthenticated(owner._clock());
        }

        public void OnReading(ValidatedReading reading) => owner._coordinator.OnReading(reading, owner._clock());

        public void OnSensorStatus(SensorState state) => owner._coordinator.OnSensorStatus(state, owner._clock());

        public void OnRejectedReading(RejectReason reason)
        {
            // A rejected reading is neither a sample nor a heartbeat; the coordinator's watchdogs handle silence.
        }
    }
}
