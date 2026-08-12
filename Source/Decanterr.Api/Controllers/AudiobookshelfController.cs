using Decanterr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudiobookshelfController : ControllerBase
{
    private readonly AudiobookshelfClient _client;
    private readonly AudiobookshelfSettingsStore _settingsStore;

    public AudiobookshelfController(AudiobookshelfClient client, AudiobookshelfSettingsStore settingsStore)
    {
        _client = client;
        _settingsStore = settingsStore;
    }

    /// <summary>Get the current Audiobookshelf settings. The API token is never returned; only whether one is set.</summary>
    [HttpGet("settings")]
    public ActionResult GetSettings()
    {
        var options = _settingsStore.Get();
        return Ok(new
        {
            enabled = options.Enabled,
            url = options.Url,
            hasApiToken = !string.IsNullOrWhiteSpace(options.ApiToken)
        });
    }

    /// <summary>
    /// Update Audiobookshelf settings. Leave <paramref name="request"/>'s ApiToken null/empty to keep the
    /// existing token unchanged (e.g. when only toggling Enabled or changing the Url).
    /// </summary>
    [HttpPut("settings")]
    public ActionResult UpdateSettings([FromBody] UpdateAudiobookshelfSettingsRequest request)
    {
        var current = _settingsStore.Get();
        _settingsStore.Save(new AudiobookshelfOptions
        {
            Enabled = request.Enabled,
            Url = request.Url?.Trim() ?? "",
            ApiToken = string.IsNullOrWhiteSpace(request.ApiToken) ? current.ApiToken : request.ApiToken.Trim()
        });

        return Ok(new { message = "Audiobookshelf settings updated" });
    }

    /// <summary>Test connectivity to the configured Audiobookshelf instance.</summary>
    [HttpGet("status")]
    public async Task<ActionResult> GetStatus()
    {
        if (!_client.IsEnabled)
            return Ok(new { enabled = false, message = "Audiobookshelf integration is not configured" });

        var connected = await _client.TestConnectionAsync();
        return Ok(new
        {
            enabled = true,
            connected,
            message = connected ? "Connected to Audiobookshelf" : "Failed to connect to Audiobookshelf"
        });
    }

    /// <summary>List libraries from Audiobookshelf.</summary>
    [HttpGet("libraries")]
    public async Task<ActionResult> GetLibraries()
    {
        if (!_client.IsEnabled)
            return BadRequest(new { error = "Audiobookshelf integration is not configured" });

        var libraries = await _client.GetLibrariesAsync();
        return Ok(libraries.Select(l => new
        {
            l.Id,
            l.Name,
            l.MediaType,
            Folders = l.Folders.Select(f => new { f.Id, f.FullPath })
        }));
    }

    /// <summary>Trigger a library scan in Audiobookshelf.</summary>
    [HttpPost("libraries/{libraryId}/scan")]
    public async Task<ActionResult> ScanLibrary(string libraryId)
    {
        if (!_client.IsEnabled)
            return BadRequest(new { error = "Audiobookshelf integration is not configured" });

        var success = await _client.ScanLibraryAsync(libraryId);
        return success
            ? Ok(new { message = "Library scan triggered" })
            : StatusCode(502, new { error = "Failed to trigger library scan" });
    }
}

public class UpdateAudiobookshelfSettingsRequest
{
    public bool Enabled { get; set; }
    public string? Url { get; set; }
    public string? ApiToken { get; set; }
}

