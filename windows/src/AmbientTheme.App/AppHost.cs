using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Connection;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;
using AmbientTheme.Windows;

namespace AmbientTheme.App;

/// <summary>
/// Composition root: wires settings, theme adapter, coordinator, registry watcher and connection supervisor.
/// Everything stays local to the user profile; nothing here needs elevation, an account or the internet.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly DpapiSecretStore _secrets;
    private readonly ThemeAdapter _theme;
    private readonly RegistryChangeWatcher _watcher;
    private readonly DeviceClient _client;
    private readonly bool _readOnlySettings;
    private CancellationTokenSource? _connectionStop;
    private ConnectionSupervisor? _supervisor;

    public AppHost(INotifier notifier)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmbientThemeController");
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var loaded = store.Load();
        LoadStatus = loaded.Status;
        _readOnlySettings = loaded.Status == SettingsLoadStatus.NewerSchema;
        Settings = new SettingsRepository(loaded.Settings, settings =>
        {
            if (!_readOnlySettings)
            {
                store.Save(settings);
            }
        });

        AllowSimulated = Environment.GetEnvironmentVariable("ATC_ALLOW_SIMULATED") == "1";
        _client = new DeviceClient { AllowSimulatedReadings = AllowSimulated };
        _secrets = new DpapiSecretStore(Path.Combine(directory, "secrets"));
        _theme = new ThemeAdapter(new RegistryThemeRegistry(), new HighContrastProbe(),
            new FileThemeJournal(Path.Combine(directory, "theme-journal.json")), new ThemeChangeBroadcaster());
        Coordinator = new AutomationCoordinator(Settings, _theme, notifier);
        Coordinator.Changed += () => Changed?.Invoke();

        ReconcileTheme();
        _watcher = new RegistryChangeWatcher(RegistryThemeRegistry.PersonalizePath, ObserveTheme,
            TimeSpan.FromMilliseconds(300));
    }

    /// <summary>Raised on any thread when status or diagnostics may have changed.</summary>
    public event Action? Changed;

    public SettingsRepository Settings { get; }
    public AutomationCoordinator Coordinator { get; }
    public SettingsLoadStatus LoadStatus { get; }
    public bool AllowSimulated { get; }
    public (Endpoint Endpoint, ConnectionFailure Failure)? LastFailure => _supervisor?.LastFailure;

    public void Start()
    {
        Coordinator.Start(MonotonicClock.Now);
        RestartConnection();
    }

    public void Tick() => Coordinator.Tick(MonotonicClock.Now);

    /// <summary>Starts a fresh supervisor, e.g. after pairing or a changed manual endpoint.</summary>
    public void RestartConnection()
    {
        _connectionStop?.Cancel();
        _connectionStop = new CancellationTokenSource();
        _supervisor = new ConnectionSupervisor(_client, Coordinator, Settings, _secrets);
        _supervisor.AttemptFailed += (_, _) => Changed?.Invoke();
        var stop = _connectionStop.Token;
        _ = Task.Run(() => _supervisor.RunAsync(stop), stop);
    }

    public Task<PairingResult> PairAsync(Endpoint endpoint, PairingInvite invite, Func<string, Task<bool>> confirm) =>
        _client.PairAsync(endpoint, invite, confirm, CancellationToken.None);

    public void CompletePairing(PairingResult result, Endpoint endpoint)
    {
        _secrets.Write(ConnectionSupervisor.CredentialName, result.Credential!);
        Settings.Update(settings => settings with
        {
            PairedDevice = new PairedDevice(result.DeviceId!, "Celular Android", result.CertificatePin!, [endpoint.ToString()]),
            ManualEndpoint = endpoint.ToString(),
        });
        RestartConnection();
    }

    /// <summary>Forgets the phone on this computer. Revoking this computer on the phone is done in the Android app.</summary>
    public void Unpair()
    {
        _connectionStop?.Cancel();
        _secrets.Delete(ConnectionSupervisor.CredentialName);
        Settings.Update(settings => settings with { PairedDevice = null });
        Coordinator.OnTransportLost(MonotonicClock.Now);
    }

    public void SetManualEndpoint(Endpoint? endpoint)
    {
        Settings.Update(settings => settings with { ManualEndpoint = endpoint?.ToString() });
        RestartConnection();
    }

    public string Export() => SettingsExporter.Export(Settings.Current);

    public bool Import(string json)
    {
        if (SettingsExporter.Import(json, Settings.Current) is not { } imported)
        {
            return false;
        }

        Settings.Update(_ => imported);
        Coordinator.ReloadSettings();
        return true;
    }

    public void Dispose()
    {
        _connectionStop?.Cancel();
        _watcher.Dispose();
    }

    private void ReconcileTheme()
    {
        if (_theme.Reconcile(Settings.Current.LastObservedTheme) == Observation.ExternalChange)
        {
            Coordinator.OnExternalThemeChange();
        }
        else
        {
            Settings.Update(settings => settings with { LastObservedTheme = _theme.Current });
        }
    }

    private void ObserveTheme()
    {
        if (_theme.Observe() == Observation.ExternalChange)
        {
            Coordinator.OnExternalThemeChange();
        }
    }
}
