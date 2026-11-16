using AmbientTheme.Core.Automation;

namespace AmbientTheme.Core.Theme;

/// <summary>Access to the two personalization values. Writes may throw when refused by policy.</summary>
public interface IThemeRegistry
{
    ThemeSnapshot Read();
    void WriteApps(int value);
    void WriteSystem(int value);
}

public interface IContrastProbe
{
    bool IsContrastThemeActive();
}

/// <summary>Asks running windows to re-read the theme. Best effort: no repaint is guaranteed.</summary>
public interface IThemeChangeBroadcaster
{
    void Broadcast();
}

public sealed record ThemeMutation(string OperationId, ThemeSnapshot Previous, ThemeSnapshot Target);

/// <summary>Durable record of an in-flight write, so a crash between write and confirmation can be reconciled.</summary>
public interface IThemeJournal
{
    ThemeMutation? Pending { get; }
    void Begin(ThemeMutation mutation);
    void Complete();
}

public enum ApplyOutcome { NoOp, Applied, PartialFailure, Failed, Blocked }

public sealed record ApplyResult(ApplyOutcome Outcome, ThemeSnapshot Observed, string? Detail);

public enum Observation { Unchanged, ExternalChange }

/// <summary>
/// Single writer and observer of the Windows theme. Own writes and observations share one lock and one
/// expected snapshot, so an echo of our own write is recognised by its content (not by timing) and
/// anything else is reported as an external change.
/// </summary>
public sealed class ThemeAdapter : IThemeController
{
    private readonly Lock _gate = new();
    private readonly IThemeRegistry _registry;
    private readonly IContrastProbe _contrast;
    private readonly IThemeJournal _journal;
    private readonly IThemeChangeBroadcaster? _broadcaster;
    private ThemeSnapshot _expected;
    private bool _contrastActive;

    public ThemeAdapter(IThemeRegistry registry, IContrastProbe contrast, IThemeJournal journal,
        IThemeChangeBroadcaster? broadcaster = null)
    {
        _registry = registry;
        _contrast = contrast;
        _journal = journal;
        _broadcaster = broadcaster;
        _expected = registry.Read();
        _contrastActive = contrast.IsContrastThemeActive();
    }

    public ThemeSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _expected;
            }
        }
    }

    public ApplyResult Apply(ThemeKind theme)
    {
        lock (_gate)
        {
            if (_contrast.IsContrastThemeActive())
            {
                return new ApplyResult(ApplyOutcome.Blocked, _expected, "Contrast theme is active.");
            }

            ThemeSnapshot previous;
            try
            {
                previous = _registry.Read();
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                return new ApplyResult(ApplyOutcome.Failed, _expected, exception.Message);
            }

            var target = ThemeSnapshot.Of(theme);
            if (previous == target)
            {
                _expected = previous;
                return new ApplyResult(ApplyOutcome.NoOp, previous, null);
            }

            _journal.Begin(new ThemeMutation(Guid.NewGuid().ToString("N"), previous, target));
            var error = Write(previous, target);
            var observed = _registry.Read();
            _journal.Complete();
            _expected = observed;

            if (observed != previous)
            {
                _broadcaster?.Broadcast();
            }

            if (observed == target)
            {
                return new ApplyResult(ApplyOutcome.Applied, observed, null);
            }

            // No blind rollback: the observed state is preserved and the caller must stop writing.
            var outcome = observed == previous ? ApplyOutcome.Failed : ApplyOutcome.PartialFailure;
            return new ApplyResult(outcome, observed, error ?? "Observed theme differs from the requested one.");
        }
    }

    /// <summary>Call after a registry change notification or a lifecycle event (unlock, resume).</summary>
    public Observation Observe()
    {
        lock (_gate)
        {
            var observed = _registry.Read();
            var contrastActive = _contrast.IsContrastThemeActive();
            if (observed == _expected && contrastActive == _contrastActive)
            {
                return Observation.Unchanged;
            }

            _expected = observed;
            _contrastActive = contrastActive;
            return Observation.ExternalChange;
        }
    }

    /// <summary>
    /// Startup reconciliation against the last theme this app knew about. A divergence is an external or
    /// undetermined change (authorship is never inferred); an ambiguous crashed write prefers suspension.
    /// </summary>
    public Observation Reconcile(ThemeSnapshot? lastKnown)
    {
        lock (_gate)
        {
            var observed = _registry.Read();
            _expected = observed;

            if (_journal.Pending is { } pending)
            {
                _journal.Complete();
                return observed == pending.Target ? Observation.Unchanged : Observation.ExternalChange;
            }

            return lastKnown is null || lastKnown == observed ? Observation.Unchanged : Observation.ExternalChange;
        }
    }

    private string? Write(ThemeSnapshot previous, ThemeSnapshot target)
    {
        try
        {
            if (previous.Apps != target.Apps)
            {
                _registry.WriteApps(target.Apps!.Value);
            }

            if (previous.System != target.System)
            {
                _registry.WriteSystem(target.System!.Value);
            }

            return null;
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return exception.Message;
        }
    }

    private static bool IsRegistryFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or System.Security.SecurityException
            or InvalidOperationException;
}
