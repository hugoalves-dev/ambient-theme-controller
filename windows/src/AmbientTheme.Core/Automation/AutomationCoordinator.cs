using AmbientTheme.Core.Engine;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Automation;

/// <summary>Suggested watchdog values (spec §6). Experimental defaults, not SLAs.</summary>
public sealed record CoordinatorTimings
{
    public TimeSpan Suspect { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ConfirmedFailure { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan Stabilization { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Final authority on theme changes. Keeps the persistent user intent (<see cref="DesiredMode"/>) apart from
/// the volatile runtime state, and only writes when every gate agrees. Transport, UI and messages can feed it
/// events but can never switch the theme directly. All times are local monotonic readings.
/// </summary>
public sealed class AutomationCoordinator
{
    private enum Link { None, Discovering, Connecting, Authenticating, Session }

    private enum SensorPhase { Waiting, Stabilizing, Stable }

    private readonly Lock _gate = new();
    private readonly SettingsRepository _settings;
    private readonly IThemeController _theme;
    private readonly INotifier _notifier;
    private readonly CoordinatorTimings _timings;

    private ThemeRecommendationEngine _engine;
    private Link _link = Link.None;
    private SensorPhase _phase = SensorPhase.Waiting;
    private SensorState? _sensor;
    private double? _lux;
    private Recommendation _recommendation = Recommendation.Hold;
    private TimeSpan _lastMessageAt;
    private TimeSpan _stabilizingSince;
    private TimeSpan? _suspendedSince;
    private bool _silent;
    private bool _hadSession;
    private bool _failureConfirmed;
    private bool _applyBlocked;
    private bool _contrastBlocked;
    private FatalFailure? _fatal;

    public AutomationCoordinator(SettingsRepository settings, IThemeController theme, INotifier notifier,
        CoordinatorTimings? timings = null)
    {
        _settings = settings;
        _theme = theme;
        _notifier = notifier;
        _timings = timings ?? new CoordinatorTimings();
        _engine = CreateEngine();
    }

    /// <summary>Raised after any event that may have changed <see cref="Status"/>. May fire on any thread.</summary>
    public event Action? Changed;

    public CoordinatorStatus Status
    {
        get
        {
            lock (_gate)
            {
                var settings = _settings.Current;
                return new CoordinatorStatus(State(settings.DesiredMode, settings.PairedDevice is not null),
                    settings.DesiredMode, settings.PauseReason, _lux, _sensor, _recommendation, _applyBlocked,
                    _contrastBlocked, _fatal);
            }
        }
    }

    /// <summary>Opens the first "sensor not available yet" episode for a paired computer.</summary>
    public void Start(TimeSpan now) => Run(() =>
    {
        if (_settings.Current.PairedDevice is not null)
        {
            _suspendedSince = now;
        }
    });

    public void OnDiscovering() => Run(() => SetLink(Link.Discovering));

    public void OnConnecting(TimeSpan now) => Run(() => SetLink(Link.Connecting));

    public void OnAuthenticating(TimeSpan now) => Run(() => SetLink(Link.Authenticating));

    /// <summary>Identity and credential verified: a new session starts and nothing from the previous one is reused.</summary>
    public void OnAuthenticated(TimeSpan now) => Run(() =>
    {
        _fatal = null;
        _link = Link.Session;
        _hadSession = true;
        _silent = false;
        _sensor = null;
        _lastMessageAt = now;
        ResetSensor(now);
    });

    public void OnTransportLost(TimeSpan now) => Run(() =>
    {
        _link = Link.None;
        _sensor = null;
        ResetSensor(now);
    });

    /// <summary>Revocation or identity failure: stop, require a new pairing and never retry aggressively.</summary>
    public void OnFatalFailure(FatalFailure failure, TimeSpan now) => Run(() =>
    {
        _link = Link.None;
        _fatal = failure;
        _sensor = null;
        ResetSensor(now);
        if (!_failureConfirmed)
        {
            _failureConfirmed = true;
            Notify(NotificationKind.PairingRejected);
        }
    });

    /// <summary>A validated physical reading of the current session.</summary>
    public void OnReading(ValidatedReading reading, TimeSpan now) => Run(() =>
    {
        if (_link != Link.Session)
        {
            return;
        }

        MarkAlive(now);
        if (_sensor is not (null or SensorState.Active))
        {
            return;
        }

        _lux = reading.Lux;
        if (_phase == SensorPhase.Waiting)
        {
            _phase = SensorPhase.Stabilizing;
            _stabilizingSince = now;
        }

        Evaluate(now);
    });

    /// <summary>Service heartbeat and sensor health. It proves the service answers, never that a new sample exists.</summary>
    public void OnSensorStatus(SensorState state, TimeSpan now) => Run(() =>
    {
        if (_link != Link.Session)
        {
            return;
        }

        _sensor = state;
        if (state == SensorState.Active)
        {
            MarkAlive(now);
            Evaluate(now);
            return;
        }

        _lastMessageAt = now;
        ResetSensor(now);
        if (state is SensorState.Unavailable or SensorState.Error)
        {
            ConfirmFailure();
        }
    });

    /// <summary>A detectable theme change that this app did not make. Authorship is not inferred.</summary>
    public void OnExternalThemeChange() => Run(() =>
    {
        var observed = _theme.Current;
        var wasAuto = _settings.Current.DesiredMode == DesiredMode.Auto;
        _settings.Update(settings => wasAuto
            ? settings with
            {
                DesiredMode = DesiredMode.ManualPause,
                PauseReason = PauseReason.ExternalThemeChange,
                LastObservedTheme = observed,
            }
            : settings with { LastObservedTheme = observed });

        _engine.Reset();
        if (wasAuto)
        {
            _recommendation = Recommendation.Hold;
            Notify(NotificationKind.ExternalThemeChange);
        }
    });

    public void UserPause() => Run(() =>
    {
        _settings.Update(settings => settings with
        {
            DesiredMode = DesiredMode.ManualPause,
            PauseReason = PauseReason.UserPaused,
        });
        _engine.Reset();
        _recommendation = Recommendation.Hold;
    });

    /// <summary>
    /// Explicit user choice of Auto or RecommendOnly. It clears pauses and write blocks, but the next change
    /// still has to earn stabilization, hysteresis, dwell and cooldown.
    /// </summary>
    public void UserSetMode(DesiredMode mode) => Run(() =>
    {
        if (mode == DesiredMode.ManualPause)
        {
            throw new ArgumentException("Use UserPause to pause.", nameof(mode));
        }

        _settings.Update(settings => settings with { DesiredMode = mode, PauseReason = PauseReason.None });
        _applyBlocked = false;
        _engine.Reset();
    });

    public void UserSetPreset(ThresholdPreset preset) => Run(() =>
    {
        var thresholds = ThresholdPresets.For(preset);
        _settings.Update(settings => settings with
        {
            Preset = preset,
            DarkBelowLux = thresholds.DarkBelowLux,
            LightAboveLux = thresholds.LightAboveLux,
        });
        _engine = CreateEngine();
    });

    /// <returns>False when the values are invalid; nothing is changed or silently corrected.</returns>
    public bool UserSetThresholds(double darkBelowLux, double lightAboveLux)
    {
        if (!Thresholds.TryCreate(darkBelowLux, lightAboveLux, out _))
        {
            return false;
        }

        Run(() =>
        {
            _settings.Update(settings => settings with
            {
                Preset = ThresholdPreset.Personalized,
                DarkBelowLux = darkBelowLux,
                LightAboveLux = lightAboveLux,
            });
            _engine = CreateEngine();
        });
        return true;
    }

    /// <summary>Drives watchdogs, stabilization and dwell. Call about once per second.</summary>
    public void Tick(TimeSpan now) => Run(() =>
    {
        if (_link == Link.Session && !_silent && now - _lastMessageAt >= _timings.Suspect)
        {
            _silent = true;
            _engine.Reset();
            _recommendation = Recommendation.Hold;
            _suspendedSince ??= now;
        }

        if (_suspendedSince is { } since && !_failureConfirmed && now - since >= _timings.ConfirmedFailure)
        {
            ConfirmFailure();
        }

        if (IsStabilizing && now - _stabilizingSince >= _timings.Stabilization)
        {
            _phase = SensorPhase.Stable;
            MarkReliable();
        }

        Evaluate(now);
    });

    private bool IsStabilizing => _link == Link.Session && !_silent && _phase == SensorPhase.Stabilizing;

    private bool IsReliable => _link == Link.Session && !_silent && _phase == SensorPhase.Stable;

    private void Evaluate(TimeSpan now)
    {
        if (!IsReliable || _lux is not { } lux)
        {
            _recommendation = Recommendation.Hold;
            return;
        }

        var settings = _settings.Current;
        if (settings.DesiredMode == DesiredMode.ManualPause)
        {
            _recommendation = Recommendation.Hold;
            return;
        }

        _recommendation = _engine.Evaluate(lux, _theme.Current.Unified, now);
        if (settings.DesiredMode != DesiredMode.Auto || _applyBlocked || !_recommendation.MayApply)
        {
            return;
        }

        var result = _theme.Apply(_recommendation.Target!.Value);
        _contrastBlocked = result.Outcome == ApplyOutcome.Blocked;
        switch (result.Outcome)
        {
            case ApplyOutcome.Applied:
                _engine.NotifyApplied(now);
                _recommendation = Recommendation.Hold;
                _settings.Update(current => current with { LastObservedTheme = result.Observed });
                break;
            case ApplyOutcome.PartialFailure or ApplyOutcome.Failed:
                _applyBlocked = true;
                _settings.Update(current => current with { LastObservedTheme = result.Observed });
                Notify(NotificationKind.ThemeApplyFailed);
                break;
        }
    }

    /// <summary>Any message on a live session ends a silence episode.</summary>
    private void MarkAlive(TimeSpan now)
    {
        _lastMessageAt = now;
        if (!_silent)
        {
            return;
        }

        _silent = false;
        if (_phase == SensorPhase.Stable)
        {
            MarkReliable();
        }
    }

    private void MarkReliable()
    {
        if (_failureConfirmed && _settings.Current.DesiredMode == DesiredMode.Auto)
        {
            Notify(NotificationKind.SensorRecovered);
        }

        _failureConfirmed = false;
        _suspendedSince = null;
    }

    /// <summary>The source stopped being trustworthy: freeze changes and forget everything measured so far.</summary>
    private void ResetSensor(TimeSpan now)
    {
        _phase = SensorPhase.Waiting;
        _lux = null;
        _recommendation = Recommendation.Hold;
        _engine.Reset();
        _suspendedSince ??= now;
    }

    private void ConfirmFailure()
    {
        if (_failureConfirmed)
        {
            return;
        }

        _failureConfirmed = true;
        Notify(NotificationKind.SensorUnavailable);
    }

    private void SetLink(Link link)
    {
        if (_fatal is null || link == Link.Session)
        {
            _link = link;
        }
    }

    private RuntimeState State(DesiredMode mode, bool paired)
    {
        if (!paired)
        {
            return RuntimeState.Unpaired;
        }

        if (_link == Link.Session)
        {
            if (_silent)
            {
                return RuntimeState.Degraded;
            }

            return _phase switch
            {
                SensorPhase.Waiting => RuntimeState.WaitingForSensor,
                SensorPhase.Stabilizing => RuntimeState.Stabilizing,
                _ => mode == DesiredMode.Auto && !_applyBlocked ? RuntimeState.Active : RuntimeState.Ready,
            };
        }

        if (_fatal is not null)
        {
            return RuntimeState.Disconnected;
        }

        if (_hadSession && !_failureConfirmed)
        {
            return RuntimeState.Degraded;
        }

        return _link switch
        {
            Link.Discovering => RuntimeState.Discovering,
            Link.Connecting => _hadSession ? RuntimeState.Reconnecting : RuntimeState.Connecting,
            Link.Authenticating => RuntimeState.Authenticating,
            _ => RuntimeState.Disconnected,
        };
    }

    private ThemeRecommendationEngine CreateEngine()
    {
        var settings = _settings.Current;
        return new ThemeRecommendationEngine(
            new Thresholds(settings.DarkBelowLux, settings.LightAboveLux),
            TimeSpan.FromSeconds(settings.DwellSeconds),
            TimeSpan.FromSeconds(settings.CooldownSeconds));
    }

    private void Notify(NotificationKind kind)
    {
        if (_settings.Current.NotificationsEnabled)
        {
            _notifier.Notify(kind);
        }
    }

    private void Run(Action action)
    {
        lock (_gate)
        {
            action();
        }

        Changed?.Invoke();
    }
}
