using System.Text.Json;
using LibationFileManager;

namespace Decanterr.Api.Services;

/// <summary>
/// Persists Audiobookshelf integration settings to a JSON file under LibationFiles so they can be
/// changed at runtime from the UI, instead of requiring an appsettings.json/env var change + restart.
/// </summary>
public class AudiobookshelfSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();
    private AudiobookshelfOptions _current;

    public AudiobookshelfSettingsStore(IConfiguration configuration)
    {
        _filePath = Path.Combine(Configuration.Instance.LibationFiles.Location, "audiobookshelf.json");
        _current = Load(configuration);
    }

    public AudiobookshelfOptions Get()
    {
        lock (_lock)
            return _current;
    }

    public void Save(AudiobookshelfOptions options)
    {
        lock (_lock)
        {
            _current = options;
            File.WriteAllText(_filePath, JsonSerializer.Serialize(options, SerializerOptions));
        }
    }

    private AudiobookshelfOptions Load(IConfiguration configuration)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AudiobookshelfOptions>(File.ReadAllText(_filePath));
                if (loaded is not null)
                    return loaded;
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Warning(ex, "Failed to load Audiobookshelf settings file at {Path}; falling back to appsettings", _filePath);
            }
        }

        // First run: seed from appsettings.json / env vars, then persist so future edits use the file
        return configuration.GetSection("Audiobookshelf").Get<AudiobookshelfOptions>() ?? new AudiobookshelfOptions();
    }
}
