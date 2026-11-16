using System.Text.Json;

namespace AmbientTheme.Core.Theme;

/// <summary>Journal kept in a small file so a crash between write and confirmation is visible on the next start.</summary>
public sealed class FileThemeJournal(string path) : IThemeJournal
{
    public ThemeMutation? Pending
    {
        get
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ThemeMutation>(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                return null;
            }
        }
    }

    public void Begin(ThemeMutation mutation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(mutation));
    }

    public void Complete() => File.Delete(path);
}
