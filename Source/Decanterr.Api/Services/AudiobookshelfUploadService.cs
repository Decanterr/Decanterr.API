using ApplicationServices;
using DataLayer;

namespace Decanterr.Api.Services;

/// <summary>
/// Uploads liberated audiobook files to Audiobookshelf after liberation completes.
/// </summary>
public class AudiobookshelfUploadService
{
    private readonly AudiobookshelfClient _client;
    private readonly ILogger<AudiobookshelfUploadService> _logger;

    // Cached library/folder IDs (resolved once on first upload)
    private string? _libraryId;
    private string? _folderId;
    private bool _resolved;

    public AudiobookshelfUploadService(AudiobookshelfClient client, ILogger<AudiobookshelfUploadService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Re-upload an already-liberated book to Audiobookshelf, locating its audio file on disk.
    /// </summary>
    public async Task<(bool success, string? error)> UploadExistingBookAsync(string asin)
    {
        if (!_client.IsEnabled)
            return (false, "Audiobookshelf integration is not configured");

        var filePath = LibationFileManager.AudibleFileStorage.Audio.GetPath(asin);
        if (filePath is null)
            return (false, "Book has not been unlocked yet");

        var success = await UploadAsync(asin, filePath);
        return (success, success ? null : "Failed to upload to Audiobookshelf");
    }

    /// <summary>
    /// Upload a liberated book to Audiobookshelf.
    /// Called after FileCreated fires from the liberation process.
    /// </summary>
    public async Task<bool> UploadAsync(string asin, string filePath)
    {
        if (!_client.IsEnabled)
        {
            _logger.LogDebug("Audiobookshelf integration is disabled; skipping upload for {Asin}", asin);
            return false;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Liberated file not found: {FilePath}", filePath);
            return false;
        }

        // Resolve library/folder on first call
        if (!_resolved)
            await ResolveLibraryAsync();

        if (_libraryId is null || _folderId is null)
        {
            _logger.LogError("Could not resolve Audiobookshelf library/folder. Skipping upload for {Asin}", asin);
            return false;
        }

        // Get book metadata from Libation DB
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        var title = lb?.Book?.TitleWithSubtitle ?? Path.GetFileNameWithoutExtension(filePath);
        var author = lb?.Book?.AuthorNames;
        var series = lb?.Book?.SeriesNames();

        // Collect all files in the book's directory (m4b + cover + cue etc.)
        var bookDir = Path.GetDirectoryName(filePath)!;
        var filesToUpload = Directory.GetFiles(bookDir)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var success = await _client.UploadBookAsync(_libraryId, _folderId, title, author, series, filesToUpload);

        if (success)
        {
            _logger.LogInformation("Successfully uploaded {Asin} ('{Title}') to Audiobookshelf", asin, title);
            AudiobookshelfUploadTracker.MarkUploaded(asin);
            await _client.ScanLibraryAsync(_libraryId);
        }
        else
            _logger.LogError("Failed to upload {Asin} ('{Title}') to Audiobookshelf", asin, title);

        return success;
    }

    private async Task ResolveLibraryAsync()
    {
        _resolved = true;

        try
        {
            var libraries = await _client.GetLibrariesAsync();
            // Pick the first "book" type library
            var bookLibrary = libraries.FirstOrDefault(l => l.MediaType == "book")
                ?? libraries.FirstOrDefault();

            if (bookLibrary is null)
            {
                _logger.LogError("No libraries found in Audiobookshelf");
                return;
            }

            _libraryId = bookLibrary.Id;
            _folderId = bookLibrary.Folders.FirstOrDefault()?.Id;

            if (_folderId is null)
            {
                _logger.LogError("No folders found in Audiobookshelf library '{Name}'", bookLibrary.Name);
                return;
            }

            _logger.LogInformation("Using Audiobookshelf library '{Name}' (id={LibraryId}), folder={FolderId}",
                bookLibrary.Name, _libraryId, _folderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve Audiobookshelf libraries");
        }
    }
}

