using AmbientTheme.Core.Automation;
using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests;

internal sealed class FakeRegistry(int? apps, int? system) : IThemeRegistry
{
    public int? Apps { get; set; } = apps;
    public int? System { get; set; } = system;
    public bool FailSystemWrite { get; set; }
    public bool FailAllWrites { get; set; }
    public bool FailRead { get; set; }
    public int Writes { get; private set; }

    /// <summary>Runs right after the apps value is written, to simulate a concurrent external writer.</summary>
    public Action? AfterAppsWrite { get; set; }

    public ThemeSnapshot Read() => FailRead ? throw new IOException("read") : new ThemeSnapshot(Apps, System);

    public void WriteApps(int value)
    {
        if (FailAllWrites)
        {
            throw new UnauthorizedAccessException("policy");
        }

        Writes++;
        Apps = value;
        AfterAppsWrite?.Invoke();
    }

    public void WriteSystem(int value)
    {
        if (FailAllWrites || FailSystemWrite)
        {
            throw new UnauthorizedAccessException("policy");
        }

        Writes++;
        System = value;
    }
}

internal sealed class FakeContrast : IContrastProbe
{
    public bool Active { get; set; }

    public bool IsContrastThemeActive() => Active;
}

internal sealed class MemoryJournal : IThemeJournal
{
    public ThemeMutation? Pending { get; set; }
    public int Begun { get; private set; }

    public void Begin(ThemeMutation mutation)
    {
        Begun++;
        Pending = mutation;
    }

    public void Complete() => Pending = null;
}

internal sealed class FakeNotifier : INotifier
{
    public List<NotificationKind> Sent { get; } = [];

    public void Notify(NotificationKind kind) => Sent.Add(kind);
}

internal sealed class FakeThemeController(ThemeKind? current) : IThemeController
{
    public ThemeSnapshot Current { get; set; } = current is { } theme ? ThemeSnapshot.Of(theme) : new ThemeSnapshot(0, 1);
    public List<ThemeKind> Applied { get; } = [];
    public ApplyOutcome NextOutcome { get; set; } = ApplyOutcome.Applied;

    public ApplyResult Apply(ThemeKind theme)
    {
        Applied.Add(theme);
        if (NextOutcome == ApplyOutcome.Applied)
        {
            Current = ThemeSnapshot.Of(theme);
        }

        return new ApplyResult(NextOutcome, Current, null);
    }
}
