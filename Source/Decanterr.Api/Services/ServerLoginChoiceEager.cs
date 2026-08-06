using AudibleApi;
using AudibleUtilities;

namespace Decanterr.Api.Services;

/// <summary>
/// Server-compatible implementation of <see cref="ILoginChoiceEager"/> that coordinates
/// between two HTTP requests: one to start login (returns Amazon login URL) and one
/// to complete login (accepts the redirect response URL).
/// </summary>
public class ServerLoginChoiceEager : ILoginChoiceEager
{
    public ILoginCallback LoginCallback { get; } = new ServerLoginCallback();

    private readonly TaskCompletionSource<string> _loginUrlTcs = new();
    private readonly TaskCompletionSource<string?> _responseUrlTcs = new();

    /// <summary>
    /// Called by EzApiCreator when interactive login is needed.
    /// Publishes the login URL and waits for the response URL from the client.
    /// </summary>
    public async Task<ChoiceOut?> StartAsync(ChoiceIn choiceIn)
    {
        // Publish the login URL so the HTTP endpoint can return it
        _loginUrlTcs.TrySetResult(choiceIn.LoginUrl);

        // Wait for the client to complete login and send back the response URL
        var responseUrl = await _responseUrlTcs.Task;

        if (string.IsNullOrWhiteSpace(responseUrl))
            return null;

        return ChoiceOut.External(responseUrl);
    }

    /// <summary>
    /// Wait for the login URL to become available (set by StartAsync when EzApiCreator calls it).
    /// </summary>
    public Task<string> GetLoginUrlAsync(CancellationToken ct = default)
    {
        ct.Register(() => _loginUrlTcs.TrySetCanceled());
        return _loginUrlTcs.Task;
    }

    /// <summary>
    /// Provide the response URL captured by the client after completing Amazon login.
    /// </summary>
    public void SetResponseUrl(string? responseUrl)
    {
        _responseUrlTcs.TrySetResult(responseUrl);
    }

    /// <summary>
    /// Cancel the login flow.
    /// </summary>
    public void Cancel()
    {
        _loginUrlTcs.TrySetCanceled();
        _responseUrlTcs.TrySetResult(null);
    }
}

/// <summary>
/// Server login callback that throws on all interactive prompts.
/// The Amazon OAuth browser flow handles 2FA/CAPTCHA/MFA directly.
/// </summary>
public class ServerLoginCallback : ILoginCallback
{
    public string DeviceName => "AudibleBookshelf-Server";

    public Task<(string email, string password)> GetLoginAsync()
        => throw new NotSupportedException("Server mode uses external browser login only");

    public Task<(string password, string guess)> GetCaptchaAnswerAsync(string password, byte[] captchaImage)
        => throw new NotSupportedException("Server mode uses external browser login only");

    public Task<(string name, string value)> GetMfaChoiceAsync(MfaConfig mfaConfig)
        => throw new NotSupportedException("Server mode uses external browser login only");

    public Task<string> Get2faCodeAsync(string prompt)
        => throw new NotSupportedException("Server mode uses external browser login only");

    public Task ShowApprovalNeededAsync()
        => throw new NotSupportedException("Server mode uses external browser login only");
}

