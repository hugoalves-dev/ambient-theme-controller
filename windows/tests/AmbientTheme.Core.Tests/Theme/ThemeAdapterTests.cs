using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Theme;

public class ThemeAdapterTests
{
    private readonly FakeRegistry _registry = new(1, 1);
    private readonly FakeContrast _contrast = new();
    private readonly MemoryJournal _journal = new();

    private ThemeAdapter Adapter() => new(_registry, _contrast, _journal);

    [Theory]
    [InlineData(1, 1, ThemeKind.Dark, 0)]
    [InlineData(0, 0, ThemeKind.Light, 1)]
    [InlineData(0, 1, ThemeKind.Dark, 0)]
    [InlineData(1, 0, ThemeKind.Light, 1)]
    public void Apply_sets_apps_and_system_together(int apps, int system, ThemeKind target, int expected)
    {
        _registry.Apps = apps;
        _registry.System = system;

        var result = Adapter().Apply(target);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.Equal(new ThemeSnapshot(expected, expected), _registry.Read());
        Assert.Null(_journal.Pending);
    }

    [Fact]
    public void Apply_is_a_noop_when_both_values_already_match()
    {
        var result = Adapter().Apply(ThemeKind.Light);

        Assert.Equal(ApplyOutcome.NoOp, result.Outcome);
        Assert.Equal(0, _registry.Writes);
        Assert.Equal(0, _journal.Begun);
    }

    [Fact]
    public void Apply_writes_only_the_value_that_differs()
    {
        _registry.System = 0;

        Adapter().Apply(ThemeKind.Light);

        Assert.Equal(1, _registry.Writes);
    }

    [Fact]
    public void Mutation_is_journaled_before_the_first_write()
    {
        ThemeMutation? seenDuringWrite = null;
        _registry.AfterAppsWrite = () => seenDuringWrite = _journal.Pending;

        Adapter().Apply(ThemeKind.Dark);

        Assert.NotNull(seenDuringWrite);
        Assert.Equal(new ThemeSnapshot(1, 1), seenDuringWrite.Previous);
        Assert.Equal(new ThemeSnapshot(0, 0), seenDuringWrite.Target);
    }

    [Fact]
    public void Partial_write_is_reported_and_not_rolled_back()
    {
        _registry.FailSystemWrite = true;

        var result = Adapter().Apply(ThemeKind.Dark);

        Assert.Equal(ApplyOutcome.PartialFailure, result.Outcome);
        Assert.Equal(new ThemeSnapshot(0, 1), result.Observed);
        Assert.Equal(new ThemeSnapshot(0, 1), _registry.Read());
    }

    [Fact]
    public void Write_refused_by_policy_is_reported_as_failed()
    {
        _registry.FailAllWrites = true;

        var result = Adapter().Apply(ThemeKind.Dark);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal(new ThemeSnapshot(1, 1), _registry.Read());
    }

    [Fact]
    public void Unreadable_registry_is_reported_as_failed_without_writing()
    {
        var adapter = Adapter();
        _registry.FailRead = true;

        Assert.Equal(ApplyOutcome.Failed, adapter.Apply(ThemeKind.Dark).Outcome);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void Missing_values_are_created_by_apply()
    {
        _registry.Apps = null;
        _registry.System = null;

        Assert.Equal(ApplyOutcome.Applied, Adapter().Apply(ThemeKind.Dark).Outcome);
        Assert.Equal(new ThemeSnapshot(0, 0), _registry.Read());
    }

    [Fact]
    public void Contrast_theme_blocks_writes()
    {
        _contrast.Active = true;

        Assert.Equal(ApplyOutcome.Blocked, Adapter().Apply(ThemeKind.Dark).Outcome);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void Own_write_is_not_observed_as_external()
    {
        var adapter = Adapter();
        adapter.Apply(ThemeKind.Dark);

        Assert.Equal(Observation.Unchanged, adapter.Observe());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Change_to_either_value_is_observed_as_external(int apps, int system)
    {
        var adapter = Adapter();
        _registry.Apps = apps;
        _registry.System = system;

        Assert.Equal(Observation.ExternalChange, adapter.Observe());
    }

    [Fact]
    public void External_change_is_reported_once()
    {
        var adapter = Adapter();
        _registry.Apps = 0;
        adapter.Observe();

        Assert.Equal(Observation.Unchanged, adapter.Observe());
    }

    [Fact]
    public void External_change_right_after_an_own_write_is_still_detected()
    {
        var adapter = Adapter();
        adapter.Apply(ThemeKind.Dark);
        _registry.Apps = 1;

        Assert.Equal(Observation.ExternalChange, adapter.Observe());
    }

    [Fact]
    public void Concurrent_external_write_during_apply_is_not_absorbed_as_success()
    {
        _registry.AfterAppsWrite = () => _registry.Apps = 1;

        var result = Adapter().Apply(ThemeKind.Dark);

        Assert.Equal(ApplyOutcome.PartialFailure, result.Outcome);
    }

    [Fact]
    public void Contrast_theme_toggle_is_observed_as_external()
    {
        var adapter = Adapter();
        _contrast.Active = true;

        Assert.Equal(Observation.ExternalChange, adapter.Observe());
    }

    [Fact]
    public void Reconcile_is_consistent_when_nothing_changed_while_closed()
    {
        Assert.Equal(Observation.Unchanged, Adapter().Reconcile(new ThemeSnapshot(1, 1)));
    }

    [Fact]
    public void Reconcile_without_previous_evidence_claims_nothing()
    {
        Assert.Equal(Observation.Unchanged, Adapter().Reconcile(null));
    }

    [Fact]
    public void Reconcile_detects_divergence_from_the_last_known_theme()
    {
        Assert.Equal(Observation.ExternalChange, Adapter().Reconcile(new ThemeSnapshot(0, 0)));
    }

    [Fact]
    public void Reconcile_accepts_a_crashed_operation_that_reached_its_target()
    {
        _journal.Pending = new ThemeMutation("op", new ThemeSnapshot(0, 0), new ThemeSnapshot(1, 1));

        Assert.Equal(Observation.Unchanged, Adapter().Reconcile(new ThemeSnapshot(0, 0)));
        Assert.Null(_journal.Pending);
    }

    [Fact]
    public void Reconcile_prefers_suspension_when_a_crashed_operation_is_ambiguous()
    {
        _registry.System = 0;
        _journal.Pending = new ThemeMutation("op", new ThemeSnapshot(0, 0), new ThemeSnapshot(1, 1));

        Assert.Equal(Observation.ExternalChange, Adapter().Reconcile(new ThemeSnapshot(0, 0)));
        Assert.Null(_journal.Pending);
    }
}
