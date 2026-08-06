using Decanterr.Api.Models;
using AudibleUtilities;
using Microsoft.AspNetCore.Mvc;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<AccountDto>> GetAll()
    {
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        var accounts = persister.AccountsSettings.Accounts
            .Select(a => new AccountDto
            {
                AccountId = a.AccountId,
                AccountName = a.AccountName,
                Locale = a.Locale?.Name,
                LibraryScan = a.LibraryScan,
                HasTokens = a.IdentityTokens is not null
            })
            .ToList();

        return Ok(accounts);
    }

    [HttpGet("{accountId}")]
    public ActionResult<AccountDto> GetById(string accountId, [FromQuery] string? locale = null)
    {
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        var account = FindAccount(persister.AccountsSettings, accountId, locale);

        if (account is null)
            return NotFound(new { error = $"Account '{accountId}' (locale: {locale ?? "any"}) not found" });

        return Ok(new AccountDto
        {
            AccountId = account.AccountId,
            AccountName = account.AccountName,
            Locale = account.Locale?.Name,
            LibraryScan = account.LibraryScan,
            HasTokens = account.IdentityTokens is not null
        });
    }

    [HttpPut("{accountId}/scan")]
    public ActionResult SetScanEnabled(string accountId, [FromQuery] bool enabled = true, [FromQuery] string? locale = null)
    {
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        var account = FindAccount(persister.AccountsSettings, accountId, locale);

        if (account is null)
            return NotFound(new { error = $"Account '{accountId}' (locale: {locale ?? "any"}) not found" });

        account.LibraryScan = enabled;
        return Ok(new { message = $"Scan {(enabled ? "enabled" : "disabled")} for account '{accountId}'" });
    }

    [HttpDelete("{accountId}")]
    public ActionResult Delete(string accountId, [FromQuery] string? locale = null)
    {
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        var account = FindAccount(persister.AccountsSettings, accountId, locale);

        if (account is null)
            return NotFound(new { error = $"Account '{accountId}' (locale: {locale ?? "any"}) not found" });

        persister.AccountsSettings.Delete(account);
        return Ok(new { message = $"Account '{accountId}' removed" });
    }

    /// <summary>
    /// Find an account by ID and optional locale. When locale is provided, matches exactly.
    /// When locale is null, matches by accountId alone (backward-compatible).
    /// </summary>
    private static Account? FindAccount(AccountsSettings settings, string accountId, string? locale)
    {
        var matches = settings.Accounts
            .Where(a => a.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(locale))
            matches = matches.Where(a => a.Locale?.Name?.Equals(locale, StringComparison.OrdinalIgnoreCase) == true);

        return matches.FirstOrDefault();
    }
}

