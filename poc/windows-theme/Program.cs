using ThemeProbe;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Windows is required.");
    return 1;
}
if (args.Length > 1 || (args.Length == 1 && args[0] is not ("read" or "light" or "dark")))
{
    Console.Error.WriteLine("Usage: ThemeProbe [read|light|dark]");
    return 2;
}
var controller = new ThemeController(new WindowsThemeRegistry());
if (args.Length == 0 || args[0] == "read")
{
    Console.WriteLine(controller.Read());
    return 0;
}
var result = controller.Apply(args[0] == "light");
Console.WriteLine(result);
return result.Success ? 0 : 1;
