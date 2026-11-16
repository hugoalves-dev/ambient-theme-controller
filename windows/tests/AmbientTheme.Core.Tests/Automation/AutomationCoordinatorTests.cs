using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Protocol;
using AmbientTheme.Core.Settings;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Automation;

public class AutomationCoordinatorTests
{
    private static readonly PairedDevice Device = new("dev-1", "Pixel", "PIN", []);

    private readonly FakeNotifier _notifier = new();
    private readonly FakeThemeController _theme = new(ThemeKind.Light);
    private readonly List<AppSettings> _saved = [];
    private SettingsRepository _settings = null!;
    private long _sequence;
    private double _now;

    private AutomationCoordinator Create(DesiredMode mode = DesiredMode.Auto, bool paired = true,
        PauseReason reason = PauseReason.None)
    {
        var initial = new AppSettings { DesiredMode = mode, PauseReason = reason, PairedDevice = paired ? Device : null };
        _settings = new SettingsRepository(initial, _saved.Add);
        var coordinator = new AutomationCoordinator(_settings, _theme, _notifier);
        coordinator.Start(Now);
        return coordinator;
    }

    private TimeSpan Now => TimeSpan.FromSeconds(_now);

    /// <summary>Advances time one second at a time, sending a heartbeat every five seconds when connected.</summary>
    private void Advance(AutomationCoordinator coordinator, double seconds, bool heartbeat = true)
    {
        for (var elapsed = 0; elapsed < seconds; elapsed++)
        {
            _now++;
            if (heartbeat && (int)_now % 5 == 0)
            {
                coordinator.OnSensorStatus(SensorState.Active, Now);
            }

            coordinator.Tick(Now);
        }
    }

    private void Connect(AutomationCoordinator coordinator)
    {
        coordinator.OnConnecting(Now);
        coordinator.OnAuthenticating(Now);
        coordinator.OnAuthenticated(Now);
    }

    private void Read(AutomationCoordinator coordinator, double lux) =>
        coordinator.OnReading(new ValidatedReading(lux, ++_sequence, LightSource.LightSensor), Now);

    /// <summary>Connects, receives a reading and waits past stabilization.</summary>
    private AutomationCoordinator Stable(double lux, DesiredMode mode = DesiredMode.Auto)
    {
        var coordinator = Create(mode);
        Connect(coordinator);
        Read(coordinator, lux);
        Advance(coordinator, 20);
        return coordinator;
    }

    [Fact]
    public void Unpaired_app_reports_unpaired_and_never_notifies()
    {
        var coordinator = Create(paired: false);

        Advance(coordinator, 120, heartbeat: false);

        Assert.Equal(RuntimeState.Unpaired, coordinator.Status.State);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void Session_walks_through_waiting_stabilizing_and_active()
    {
        var coordinator = Create();
        coordinator.OnConnecting(Now);
        Assert.Equal(RuntimeState.Connecting, coordinator.Status.State);
        coordinator.OnAuthenticating(Now);
        Assert.Equal(RuntimeState.Authenticating, coordinator.Status.State);
        coordinator.OnAuthenticated(Now);
        Assert.Equal(RuntimeState.WaitingForSensor, coordinator.Status.State);
        Read(coordinator, 50);
        Assert.Equal(RuntimeState.Stabilizing, coordinator.Status.State);
        Advance(coordinator, 19);
        Assert.Equal(RuntimeState.Stabilizing, coordinator.Status.State);
        Advance(coordinator, 1);
        Assert.Equal(RuntimeState.Active, coordinator.Status.State);
    }

    [Fact]
    public void Stable_session_is_ready_but_not_active_outside_auto_mode()
    {
        Assert.Equal(RuntimeState.Ready, Stable(50, DesiredMode.RecommendOnly).Status.State);
    }

    [Fact]
    public void Dark_room_switches_to_dark_only_after_stabilization_and_dwell()
    {
        var coordinator = Stable(5);
        Assert.Empty(_theme.Applied);

        Advance(coordinator, 29);
        Assert.Empty(_theme.Applied);

        Advance(coordinator, 1);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);
    }

    [Fact]
    public void Nothing_is_applied_at_the_handshake_instant()
    {
        var coordinator = Create();
        Connect(coordinator);
        Read(coordinator, 5);
        coordinator.Tick(Now);

        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Intermediate_band_never_changes_the_theme()
    {
        var coordinator = Stable(50);

        Advance(coordinator, 600);

        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Recommend_only_recommends_without_writing()
    {
        var coordinator = Stable(5, DesiredMode.RecommendOnly);

        Advance(coordinator, 60);

        Assert.Empty(_theme.Applied);
        Assert.Equal(ThemeKind.Dark, coordinator.Status.Recommendation.Target);
        Assert.True(coordinator.Status.Recommendation.Stable);
    }

    [Fact]
    public void Cooldown_blocks_a_quick_flip_back()
    {
        var coordinator = Stable(5);
        Advance(coordinator, 30);
        Read(coordinator, 200);

        Advance(coordinator, 300);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);

        Advance(coordinator, 400);
        Assert.Equal([ThemeKind.Dark, ThemeKind.Light], _theme.Applied);
    }

    [Fact]
    public void Heartbeats_alone_never_stabilize_the_session()
    {
        var coordinator = Create();
        Connect(coordinator);

        Advance(coordinator, 300);

        Assert.Equal(RuntimeState.WaitingForSensor, coordinator.Status.State);
        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Short_drop_freezes_changes_without_notifying()
    {
        var coordinator = Stable(5);
        Advance(coordinator, 25);
        coordinator.OnTransportLost(Now);
        Assert.Equal(RuntimeState.Degraded, coordinator.Status.State);

        Advance(coordinator, 8, heartbeat: false);
        Connect(coordinator);
        Read(coordinator, 5);

        Assert.Empty(_theme.Applied);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void Long_drop_notifies_exactly_once_and_keeps_the_theme()
    {
        var coordinator = Stable(5);
        coordinator.OnTransportLost(Now);

        Advance(coordinator, 44, heartbeat: false);
        Assert.Empty(_notifier.Sent);

        coordinator.OnConnecting(Now);
        Advance(coordinator, 300, heartbeat: false);

        Assert.Equal([NotificationKind.SensorUnavailable], _notifier.Sent);
        Assert.Equal(RuntimeState.Reconnecting, coordinator.Status.State);
        Assert.Empty(_theme.Applied);
        Assert.Equal(DesiredMode.Auto, _settings.Current.DesiredMode);
    }

    [Fact]
    public void Confirmed_failure_without_a_retry_in_progress_is_disconnected()
    {
        var coordinator = Stable(50);
        coordinator.OnTransportLost(Now);

        Advance(coordinator, 45, heartbeat: false);

        Assert.Equal(RuntimeState.Disconnected, coordinator.Status.State);
    }

    [Fact]
    public void Automation_resumes_by_itself_after_recovery_and_stabilization()
    {
        var coordinator = Stable(50);
        coordinator.OnTransportLost(Now);
        Advance(coordinator, 60, heartbeat: false);

        Connect(coordinator);
        Read(coordinator, 5);
        Advance(coordinator, 19);
        Assert.DoesNotContain(NotificationKind.SensorRecovered, _notifier.Sent);
        Assert.Empty(_theme.Applied);

        Advance(coordinator, 31);

        Assert.Equal([NotificationKind.SensorUnavailable, NotificationKind.SensorRecovered], _notifier.Sent);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);
    }

    [Fact]
    public void Lux_from_the_previous_session_is_never_reused()
    {
        var coordinator = Stable(5);
        coordinator.OnTransportLost(Now);
        Connect(coordinator);

        Advance(coordinator, 300);

        Assert.Empty(_theme.Applied);
        Assert.Null(coordinator.Status.Lux);
    }

    [Fact]
    public void Pause_made_while_reconnecting_survives_recovery()
    {
        var coordinator = Stable(50);
        coordinator.OnTransportLost(Now);
        Advance(coordinator, 60, heartbeat: false);
        coordinator.UserPause();

        Connect(coordinator);
        Read(coordinator, 5);
        Advance(coordinator, 600);

        Assert.Empty(_theme.Applied);
        Assert.Equal(DesiredMode.ManualPause, _settings.Current.DesiredMode);
        Assert.Equal(PauseReason.UserPaused, _settings.Current.PauseReason);
        Assert.DoesNotContain(NotificationKind.SensorRecovered, _notifier.Sent);
    }

    [Fact]
    public void Persisted_pause_is_respected_after_restart()
    {
        var coordinator = Create(DesiredMode.ManualPause, reason: PauseReason.ExternalThemeChange);
        Connect(coordinator);
        Read(coordinator, 5);

        Advance(coordinator, 600);

        Assert.Empty(_theme.Applied);
        Assert.Equal(RuntimeState.Ready, coordinator.Status.State);
    }

    [Fact]
    public void External_theme_change_pauses_persistently_and_notifies_once()
    {
        var coordinator = Stable(5);

        coordinator.OnExternalThemeChange();
        coordinator.OnExternalThemeChange();
        Advance(coordinator, 600);

        Assert.Empty(_theme.Applied);
        Assert.Equal(DesiredMode.ManualPause, _saved[^1].DesiredMode);
        Assert.Equal(PauseReason.ExternalThemeChange, _saved[^1].PauseReason);
        Assert.Equal([NotificationKind.ExternalThemeChange], _notifier.Sent);
    }

    [Fact]
    public void External_theme_change_during_reconnection_is_not_cleared_by_recovery()
    {
        var coordinator = Stable(50);
        coordinator.OnTransportLost(Now);
        coordinator.OnExternalThemeChange();

        Connect(coordinator);
        Read(coordinator, 5);
        Advance(coordinator, 600);

        Assert.Empty(_theme.Applied);
        Assert.Equal(DesiredMode.ManualPause, _settings.Current.DesiredMode);
    }

    [Fact]
    public void External_theme_change_does_not_pause_recommend_only()
    {
        var coordinator = Stable(50, DesiredMode.RecommendOnly);

        coordinator.OnExternalThemeChange();

        Assert.Equal(DesiredMode.RecommendOnly, _settings.Current.DesiredMode);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void User_pause_reason_is_not_overwritten_by_a_later_external_change()
    {
        var coordinator = Stable(50);
        coordinator.UserPause();

        coordinator.OnExternalThemeChange();

        Assert.Equal(PauseReason.UserPaused, _settings.Current.PauseReason);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void Explicit_resume_returns_to_auto_without_an_immediate_switch()
    {
        var coordinator = Stable(5);
        coordinator.UserPause();
        Advance(coordinator, 120);

        coordinator.UserSetMode(DesiredMode.Auto);
        coordinator.Tick(Now);
        Assert.Empty(_theme.Applied);
        Assert.Equal(PauseReason.None, _settings.Current.PauseReason);

        Advance(coordinator, 30);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);
    }

    [Theory]
    [InlineData(ApplyOutcome.PartialFailure)]
    [InlineData(ApplyOutcome.Failed)]
    public void Failed_write_blocks_automation_and_never_loops(ApplyOutcome outcome)
    {
        _theme.NextOutcome = outcome;
        var coordinator = Stable(5);

        Advance(coordinator, 600);

        Assert.Single(_theme.Applied);
        Assert.Equal([NotificationKind.ThemeApplyFailed], _notifier.Sent);
        Assert.True(coordinator.Status.ApplyBlocked);
        Assert.Equal(RuntimeState.Ready, coordinator.Status.State);
    }

    [Fact]
    public void Explicit_resume_clears_a_write_block()
    {
        _theme.NextOutcome = ApplyOutcome.Failed;
        var coordinator = Stable(5);
        Advance(coordinator, 60);
        _theme.NextOutcome = ApplyOutcome.Applied;

        coordinator.UserSetMode(DesiredMode.Auto);
        Advance(coordinator, 31);

        Assert.Equal(2, _theme.Applied.Count);
        Assert.False(coordinator.Status.ApplyBlocked);
    }

    [Fact]
    public void Contrast_block_is_reported_without_notification_spam()
    {
        _theme.NextOutcome = ApplyOutcome.Blocked;
        var coordinator = Stable(5);

        Advance(coordinator, 120);

        Assert.True(coordinator.Status.ContrastBlocked);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void Silence_degrades_and_freezes_changes()
    {
        var coordinator = Stable(5);
        Advance(coordinator, 10);

        Advance(coordinator, 14, heartbeat: false);
        Assert.Equal(RuntimeState.Active, coordinator.Status.State);

        Advance(coordinator, 30, heartbeat: false);

        Assert.Equal(RuntimeState.Degraded, coordinator.Status.State);
        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Heartbeat_after_brief_silence_restores_the_session_and_restarts_the_dwell()
    {
        var coordinator = Stable(5);
        Advance(coordinator, 25, heartbeat: false);
        Assert.Equal(RuntimeState.Degraded, coordinator.Status.State);

        coordinator.OnSensorStatus(SensorState.Active, Now);
        Assert.Equal(RuntimeState.Active, coordinator.Status.State);

        Advance(coordinator, 29);
        Assert.Empty(_theme.Applied);
        Advance(coordinator, 1);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);
        Assert.Empty(_notifier.Sent);
    }

    [Theory]
    [InlineData(SensorState.Unavailable)]
    [InlineData(SensorState.Error)]
    public void Explicit_sensor_failure_suspends_and_notifies_immediately(SensorState state)
    {
        var coordinator = Stable(5);

        coordinator.OnSensorStatus(state, Now);
        Advance(coordinator, 120, heartbeat: false);

        Assert.Equal([NotificationKind.SensorUnavailable], _notifier.Sent);
        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Uncertain_sensor_suspends_until_a_new_physical_reading()
    {
        var coordinator = Stable(5);
        coordinator.OnSensorStatus(SensorState.Uncertain, Now);
        Assert.Equal(RuntimeState.WaitingForSensor, coordinator.Status.State);

        coordinator.OnSensorStatus(SensorState.Active, Now);
        Advance(coordinator, 120);
        Assert.Empty(_theme.Applied);
        Assert.Equal(RuntimeState.WaitingForSensor, coordinator.Status.State);

        Read(coordinator, 5);
        Advance(coordinator, 50);
        Assert.Equal([ThemeKind.Dark], _theme.Applied);
    }

    [Fact]
    public void Reading_while_the_sensor_reports_itself_unreliable_is_ignored()
    {
        var coordinator = Create();
        Connect(coordinator);
        coordinator.OnSensorStatus(SensorState.Uncertain, Now);

        Read(coordinator, 5);

        Assert.Equal(RuntimeState.WaitingForSensor, coordinator.Status.State);
    }

    [Theory]
    [InlineData(FatalFailure.AuthFailed)]
    [InlineData(FatalFailure.TlsIdentityMismatch)]
    public void Fatal_identity_failure_requires_repairing_and_notifies_once(FatalFailure failure)
    {
        var coordinator = Stable(5);

        coordinator.OnFatalFailure(failure, Now);
        Advance(coordinator, 300, heartbeat: false);

        Assert.Equal(RuntimeState.Disconnected, coordinator.Status.State);
        Assert.Equal(failure, coordinator.Status.Fatal);
        Assert.Equal([NotificationKind.PairingRejected], _notifier.Sent);
        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void Startup_without_the_phone_notifies_once_after_the_confirmation_window()
    {
        var coordinator = Create();
        coordinator.OnConnecting(Now);

        Advance(coordinator, 200, heartbeat: false);

        Assert.Equal([NotificationKind.SensorUnavailable], _notifier.Sent);
    }

    [Fact]
    public void Disabled_notifications_are_respected()
    {
        var coordinator = Create();
        _settings.Update(settings => settings with { NotificationsEnabled = false });

        Advance(coordinator, 200, heartbeat: false);

        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public void Changing_thresholds_takes_effect_and_is_persisted_as_personalized()
    {
        var coordinator = Stable(50);

        Assert.True(coordinator.UserSetThresholds(60, 150));
        Advance(coordinator, 31);

        Assert.Equal([ThemeKind.Dark], _theme.Applied);
        Assert.Equal(AmbientTheme.Core.Engine.ThresholdPreset.Personalized, _settings.Current.Preset);
        Assert.Equal(60, _settings.Current.DarkBelowLux);
    }

    [Fact]
    public void Invalid_thresholds_are_refused_and_nothing_changes()
    {
        var coordinator = Stable(50);

        Assert.False(coordinator.UserSetThresholds(90, 80));

        Assert.Equal(30, _settings.Current.DarkBelowLux);
    }

    [Fact]
    public void Choosing_a_preset_never_forces_a_switch_inside_the_band()
    {
        var coordinator = Stable(40);

        coordinator.UserSetPreset(AmbientTheme.Core.Engine.ThresholdPreset.LightLonger);
        Advance(coordinator, 120);

        Assert.Empty(_theme.Applied);
        Assert.Equal(15, _settings.Current.DarkBelowLux);
        Assert.Equal(50, _settings.Current.LightAboveLux);
    }

    [Fact]
    public void Applied_theme_is_persisted_as_the_last_observed_snapshot()
    {
        var coordinator = Stable(5);

        Advance(coordinator, 30);

        Assert.Equal(new ThemeSnapshot(0, 0), _settings.Current.LastObservedTheme);
    }
}
