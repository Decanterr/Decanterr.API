using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Decanterr.Api.Services;

public class AudiobookshelfClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AudiobookshelfClient> _logger;
    private readonly AudiobookshelfSettingsStore _settingsStore;

    public AudiobookshelfClient(HttpClient http, ILogger<AudiobookshelfClient> logger, AudiobookshelfSettingsStore settingsStore)
    {
        _http = http;
        _logger = logger;
        _settingsStore = settingsStore;
    }

    public bool IsEnabled
    {
        get
        {
            var options = _settingsStore.Get();
            return options.Enabled && !string.IsNullOrWhiteSpace(options.Url) && !string.IsNullOrWhiteSpace(options.ApiToken);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, AudiobookshelfOptions options)
    {
        var request = new HttpRequestMessage(method, options.Url.TrimEnd('/') + "/" + relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        return request;
    }

    /// <summary>Get all libraries from Audiobookshelf.</summary>
    public async Task<List<AbsLibrary>> GetLibrariesAsync()
    {
        var options = _settingsStore.Get();
        using var request = CreateRequest(HttpMethod.Get, "api/libraries", options);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<AbsLibrariesResponse>();
        return json?.Libraries ?? [];
    }

    /// <summary>Upload audiobook files to Audiobookshelf.</summary>
    public async Task<bool> UploadBookAsync(string libraryId, string folderId, string title, string? author, string? series, params string[] filePaths)
    {
        var options = _settingsStore.Get();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent(libraryId), "library");
        content.Add(new StringContent(folderId), "folder");

        if (!string.IsNullOrWhiteSpace(author))
            content.Add(new StringContent(author), "author");
        if (!string.IsNullOrWhiteSpace(series))
            content.Add(new StringContent(series), "series");

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found for upload: {FilePath}", filePath);
                continue;
            }

            var fileStream = File.OpenRead(filePath);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, Path.GetFileName(filePath), Path.GetFileName(filePath));
        }

        _logger.LogInformation("Uploading book '{Title}' to Audiobookshelf library {LibraryId}", title, libraryId);

        using var request = CreateRequest(HttpMethod.Post, "api/upload", options);
        request.Content = content;
        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Audiobookshelf upload failed ({Status}): {Body}", response.StatusCode, body);
            return false;
        }

        _logger.LogInformation("Successfully uploaded '{Title}' to Audiobookshelf", title);
        return true;
    }

    /// <summary>Trigger a library scan in Audiobookshelf.</summary>
    public async Task<bool> ScanLibraryAsync(string libraryId)
    {
        var options = _settingsStore.Get();
        _logger.LogInformation("Triggering Audiobookshelf library scan for {LibraryId}", libraryId);
        using var request = CreateRequest(HttpMethod.Post, $"api/libraries/{libraryId}/scan", options);
        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Audiobookshelf scan failed ({Status}): {Body}", response.StatusCode, body);
            return false;
        }

        return true;
    }

    /// <summary>Get the ASINs of every item in the given Audiobookshelf library (only items matched to an Audible ASIN).</summary>
    public async Task<List<string>> GetLibraryItemAsinsAsync(string libraryId)
    {
        var options = _settingsStore.Get();
        using var request = CreateRequest(HttpMethod.Get, $"api/libraries/{libraryId}/items?minified=1", options);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<AbsLibraryItemsResponse>();
        return json?.Results
            .Select(r => r.Media?.Metadata?.Asin)
            .Where(asin => !string.IsNullOrWhiteSpace(asin))
            .Select(asin => asin!)
            .ToList() ?? [];
    }

    /// <summary>Verify connectivity to Audiobookshelf.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        var options = _settingsStore.Get();
        try
        {
            // api/authorize is POST-only and returns 404 on GET; api/libraries works with a GET + bearer token.
            using var request = CreateRequest(HttpMethod.Get, "api/libraries", options);
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Audiobookshelf at {Url}", options.Url);
            return false;
        }
    }
}

public class AudiobookshelfOptions
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = "";
    public string ApiToken { get; set; } = "";
}

// --- ABS API response models ---

public class AbsLibrariesResponse
{
    [JsonPropertyName("libraries")]
    public List<AbsLibrary> Libraries { get; set; } = [];
}

public class AbsLibrary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("folders")]
    public List<AbsFolder> Folders { get; set; } = [];
}

public class AbsFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("fullPath")]
    public string FullPath { get; set; } = "";
}

public class AbsLibraryItemsResponse
{
    [JsonPropertyName("results")]
    public List<AbsLibraryItem> Results { get; set; } = [];
}

public class AbsLibraryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("media")]
    public AbsMedia? Media { get; set; }
}

public class AbsMedia
{
    [JsonPropertyName("metadata")]
    public AbsMetadata? Metadata { get; set; }
}

public class AbsMetadata
{
    [JsonPropertyName("asin")]
    public string? Asin { get; set; }
}

