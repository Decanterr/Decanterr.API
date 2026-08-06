using Decanterr.Api.Services;
using AudibleUtilities;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoginController : ControllerBase
{
    // Active login sessions keyed by session ID
    private static readonly ConcurrentDictionary<string, LoginSession> _sessions = new();

    /// <summary>
    /// Start an interactive Audible login flow. Returns an Amazon login URL
    /// that the client must open in a browser.
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult> StartLogin([FromBody] LoginStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId))
            return BadRequest(new { error = "AccountId is required" });

        if (string.IsNullOrWhiteSpace(request.Locale))
            return BadRequest(new { error = "Locale is required" });

        var sessionId = Guid.NewGuid().ToString("N");
        var loginChoice = new ServerLoginChoiceEager();

        // Create or get the account
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        var account = persister.AccountsSettings.Upsert(request.AccountId, request.Locale);

        if (!string.IsNullOrWhiteSpace(request.AccountName))
            account.AccountName = request.AccountName;

        // Set up the login factory for this specific account
        var session = new LoginSession
        {
            SessionId = sessionId,
            LoginChoice = loginChoice,
            Account = account,
            CreatedAt = DateTime.UtcNow
        };
        _sessions[sessionId] = session;

        // Start the login flow in the background
        session.LoginTask = Task.Run(async () =>
        {
            // Temporarily set the factory to return our login choice
            var originalFactory = ApiExtended.LoginChoiceFactory;
            try
            {
                ApiExtended.LoginChoiceFactory = _ => loginChoice;
                await ApiExtended.CreateAsync(account);
            }
            finally
            {
                ApiExtended.LoginChoiceFactory = originalFactory;
            }
        });

        try
        {
            // Wait for the login URL to be generated (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var loginUrl = await loginChoice.GetLoginUrlAsync(cts.Token);

            return Ok(new LoginStartResponse
            {
                SessionId = sessionId,
                LoginUrl = loginUrl
            });
        }
        catch (OperationCanceledException)
        {
            _sessions.TryRemove(sessionId, out _);

            // If the background task already completed, tokens might already be valid
            if (session.LoginTask.IsCompletedSuccessfully)
            {
                return Ok(new { message = "Account already has valid tokens", alreadyAuthenticated = true });
            }

            return StatusCode(504, new { error = "Timed out waiting for login URL generation" });
        }
        catch (Exception ex)
        {
            _sessions.TryRemove(sessionId, out _);

            // If tokens are already valid, CreateAsync succeeds without calling LoginChoiceFactory
            if (session.LoginTask.IsCompletedSuccessfully)
            {
                return Ok(new { message = "Account already has valid tokens", alreadyAuthenticated = true });
            }

            return StatusCode(500, new { error = $"Failed to start login: {ex.Message}" });
        }
    }

    /// <summary>
    /// Complete the login by providing the redirect response URL from Amazon.
    /// The URL should be the full URL from the /ap/maplanding redirect.
    /// </summary>
    [HttpPost("complete")]
    public async Task<ActionResult> CompleteLogin([FromBody] LoginCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(new { error = "SessionId is required" });

        if (string.IsNullOrWhiteSpace(request.ResponseUrl))
            return BadRequest(new { error = "ResponseUrl is required" });

        if (!_sessions.TryRemove(request.SessionId, out var session))
            return NotFound(new { error = "Login session not found or expired" });

        // Provide the response URL to the waiting login flow
        session.LoginChoice.SetResponseUrl(request.ResponseUrl);

        try
        {
            // Wait for the login to complete (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await session.LoginTask.WaitAsync(cts.Token);

            return Ok(new { message = "Login successful", accountId = session.Account.AccountId });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new { error = "Login completion timed out" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Login failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Cancel an active login session.
    /// </summary>
    [HttpPost("cancel")]
    public ActionResult CancelLogin([FromBody] LoginCancelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(new { error = "SessionId is required" });

        if (_sessions.TryRemove(request.SessionId, out var session))
        {
            session.LoginChoice.Cancel();
            return Ok(new { message = "Login cancelled" });
        }

        return NotFound(new { error = "Login session not found or already completed" });
    }

    /// <summary>
    /// Get available Audible locales.
    /// </summary>
    [HttpGet("locales")]
    public ActionResult GetLocales()
    {
        var locales = new[]
        {
            new { name = "us", label = "United States" },
            new { name = "uk", label = "United Kingdom" },
            new { name = "australia", label = "Australia" },
            new { name = "brazil", label = "Brazil" },
            new { name = "canada", label = "Canada" },
            new { name = "france", label = "France" },
            new { name = "germany", label = "Germany" },
            new { name = "india", label = "India" },
            new { name = "italy", label = "Italy" },
            new { name = "japan", label = "Japan" },
            new { name = "spain", label = "Spain" },
        };

        return Ok(locales);
    }

    private class LoginSession
    {
        public required string SessionId { get; init; }
        public required ServerLoginChoiceEager LoginChoice { get; init; }
        public required Account Account { get; init; }
        public required DateTime CreatedAt { get; init; }
        public Task LoginTask { get; set; } = Task.CompletedTask;
    }
}

public record LoginStartRequest
{
    public string AccountId { get; init; } = "";
    public string Locale { get; init; } = "";
    public string? AccountName { get; init; }
}

public record LoginStartResponse
{
    public string SessionId { get; init; } = "";
    public string LoginUrl { get; init; } = "";
}

public record LoginCompleteRequest
{
    public string SessionId { get; init; } = "";
    public string ResponseUrl { get; init; } = "";
}

public record LoginCancelRequest
{
    public string SessionId { get; init; } = "";
}

