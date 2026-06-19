using System.Text.Json;

namespace EmuDOS.Core.Infrastructure;

/// <summary>Loads and saves <see cref="UserSettings"/> as settings.json in the data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataRoot, "settings.json");
    }

    public UserSettings Load()
    {
        if (!File.Exists(_path))
            return new UserSettings();

        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path)) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
