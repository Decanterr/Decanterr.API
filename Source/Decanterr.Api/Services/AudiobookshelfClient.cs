using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Decanterr.Api.Services;

public class AudiobookshelfClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AudiobookshelfClient> _logger;
    private readonly AudiobookshelfOptions _options;

    public AudiobookshelfClient(HttpClient http, ILogger<AudiobookshelfClient> logger, IConfiguration configuration)
    {
        _http = http;
        _logger = logger;
        _options = configuration.GetSection("Audiobookshelf").Get<AudiobookshelfOptions>()
            ?? new AudiobookshelfOptions();

        _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
    }

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Url) && !string.IsNullOrWhiteSpace(_options.ApiToken);

    /// <summary>Get all libraries from Audiobookshelf.</summary>
    public async Task<List<AbsLibrary>> GetLibrariesAsync()
    {
        var response = await _http.GetAsync("api/libraries");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<AbsLibrariesResponse>();
        return json?.Libraries ?? [];
    }

    /// <summary>Upload audiobook files to Audiobookshelf.</summary>
    public async Task<bool> UploadBookAsync(string libraryId, string folderId, string title, string? author, string? series, params string[] filePaths)
    {
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

        var response = await _http.PostAsync("api/upload", content);

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
        _logger.LogInformation("Triggering Audiobookshelf library scan for {LibraryId}", libraryId);
        var response = await _http.PostAsync($"api/libraries/{libraryId}/scan", null);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Audiobookshelf scan failed ({Status}): {Body}", response.StatusCode, body);
            return false;
        }

        return true;
    }

    /// <summary>Verify connectivity to Audiobookshelf.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/authorize");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Audiobookshelf at {Url}", _options.Url);
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

