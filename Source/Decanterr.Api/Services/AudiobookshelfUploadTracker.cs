using System.Text.Json;
using LibationFileManager;

namespace Decanterr.Api.Services;

/// <summary>
/// Tracks which ASINs have been uploaded to Audiobookshelf, persisted under LibationFiles so the
/// UI can reflect per-book upload status without re-querying Audiobookshelf.
/// </summary>
public static class AudiobookshelfUploadTracker
{
    private const string FileName = "audiobookshelf-uploads.json";
    private static readonly object Lock = new();
    private static readonly HashSet<string> Uploaded = Load();

    private static string FilePath => Path.Combine(Configuration.Instance.LibationFiles.Location, FileName);

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(Configuration.Instance.LibationFiles.Location, FileName);
            if (File.Exists(path))
            {
                var items = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
                if (items is not null)
                    foreach (var item in items)
                        set.Add(item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Warning(ex, "Failed to load Audiobookshelf upload tracker file");
        }
        return set;
    }

    public static bool IsUploaded(string asin)
    {
        lock (Lock)
            return Uploaded.Contains(asin);
    }

    public static void MarkUploaded(string asin)
    {
        lock (Lock)
        {
            if (Uploaded.Add(asin))
                Save();
        }
    }

    public static void MarkNotUploaded(string asin)
    {
        lock (Lock)
        {
            if (Uploaded.Remove(asin))
                Save();
        }
    }

    private static void Save() =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Uploaded));
}
