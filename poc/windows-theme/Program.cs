using ThemeProbe;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Windows is required.");
    return 1;
}
if (args.Length > 1 || (args.Length == 1 && args[0] is not ("read" or "watch" or "light" or "dark")))
{
    Console.Error.WriteLine("Usage: ThemeProbe [read|watch|light|dark]");
    return 2;
}
var controller = new ThemeController(new WindowsThemeRegistry());
if (args.Length == 0 || args[0] == "read")
{
    Console.WriteLine(controller.Read());
    return 0;
}
if (args[0] == "watch")
{
    var initial = controller.Read();
    var observer = new ThemeObserver(initial);
    Console.WriteLine($"Initial: {initial}. Watching every second; Ctrl+C to stop.");
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    try
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
            var current = controller.Read();
            if (observer.Observe(current)) Console.WriteLine($"{DateTimeOffset.Now:O}: {current}");
        }
    }
    catch (OperationCanceledException) { return 0; }
}
var result = controller.Apply(args[0] == "light");
Console.WriteLine(result);
return result.Success ? 0 : 1;
