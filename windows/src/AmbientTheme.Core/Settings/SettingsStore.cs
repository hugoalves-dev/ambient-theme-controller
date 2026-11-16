using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmbientTheme.Core.Settings;

public enum SettingsLoadStatus { Loaded, Created, RecoveredFromCorruption, NewerSchema }

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status);

/// <summary>
/// Versioned local settings file. Unknown fields are tolerated, corrupt files are kept as a backup,
/// and a file written by a newer version is never overwritten.
/// </summary>
public sealed class SettingsStore(string path)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private bool _readOnly;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(path))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Created);
        }

        var content = File.ReadAllText(path);
        var settings = TryParse(content);
        if (settings is null || (settings.SchemaVersion <= AppSettings.CurrentSchemaVersion && !settings.IsValid))
        {
            File.Move(path, $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.RecoveredFromCorruption);
        }

        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            _readOnly = true;
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NewerSchema);
        }

        return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded);
    }

    public void Save(AppSettings settings)
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("Settings were written by a newer version and are read-only.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, path, overwrite: true);
    }

    private static AppSettings? TryParse(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(content, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
