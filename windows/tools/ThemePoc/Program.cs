// PoC for issue #3: read, observe and (only when explicitly asked) change the real Windows 11 theme
// through the same adapter the product uses. Run it yourself and watch the screen: the visual result
// is the evidence the automated tests cannot collect.
using AmbientTheme.Core.Theme;
using AmbientTheme.Windows;

var registry = new RegistryThemeRegistry();
var contrast = new HighContrastProbe();
var journalPath = Path.Combine(Path.GetTempPath(), "atc-themepoc-journal.json");
var adapter = new ThemeAdapter(registry, contrast, new FileThemeJournal(journalPath), new ThemeChangeBroadcaster());

switch (args.FirstOrDefault())
{
    case "read":
        Print("Current", registry.Read());
        Console.WriteLine($"Contrast theme active: {contrast.IsContrastThemeActive()}");
        return 0;

    case "watch":
        Console.WriteLine("Watching the theme. Change it in Windows Settings; Ctrl+C to stop.");
        using (new RegistryChangeWatcher(RegistryThemeRegistry.PersonalizePath, () => Report(adapter.Observe()),
                   TimeSpan.FromMilliseconds(300)))
        {
            Thread.Sleep(Timeout.Infinite);
        }

        return 0;

    case "apply" when args.Length == 2 && Enum.TryParse<ThemeKind>(args[1], ignoreCase: true, out var theme):
        return Apply(theme);

    case "roundtrip":
        return Roundtrip();

    default:
        Console.WriteLine("""
            Usage:
              ThemePoc read                 show both values and the contrast flag (read-only)
              ThemePoc watch                report external changes as they happen (read-only)
              ThemePoc apply light|dark     CHANGES your Windows theme (apps + system)
              ThemePoc roundtrip            CHANGES the theme, waits 8 s, then restores the original
            """);
        return 1;
}

int Apply(ThemeKind theme)
{
    using var watcher = new RegistryChangeWatcher(RegistryThemeRegistry.PersonalizePath,
        () => Report(adapter.Observe()), TimeSpan.FromMilliseconds(300));
    var result = adapter.Apply(theme);
    Console.WriteLine($"Apply {theme}: {result.Outcome} {result.Detail}");
    Print("Observed", result.Observed);
    Thread.Sleep(1500);
    return result.Outcome is ApplyOutcome.Applied or ApplyOutcome.NoOp ? 0 : 2;
}

int Roundtrip()
{
    var original = registry.Read();
    if (original.Unified is not { } current)
    {
        Console.WriteLine("Theme is mixed or unreadable; refusing to guess what to restore.");
        Print("Current", original);
        return 2;
    }

    var opposite = current == ThemeKind.Light ? ThemeKind.Dark : ThemeKind.Light;
    var first = Apply(opposite);
    Console.WriteLine("Look at the taskbar, Start, Settings and open apps now. Restoring in 8 s...");
    Thread.Sleep(8000);
    return Math.Max(first, Apply(current));
}

void Report(Observation observation) => Console.WriteLine(
    $"{DateTime.Now:HH:mm:ss.fff} registry changed -> {observation} " +
    $"(apps={adapter.Current.Apps}, system={adapter.Current.System})");

static void Print(string label, ThemeSnapshot snapshot) => Console.WriteLine(
    $"{label}: AppsUseLightTheme={Show(snapshot.Apps)} SystemUsesLightTheme={Show(snapshot.System)} " +
    $"=> {snapshot.Unified?.ToString() ?? "mixed/unknown"}");

static string Show(int? value) => value?.ToString() ?? "missing";
