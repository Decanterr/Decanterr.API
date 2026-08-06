using Decanterr.Api.Models;
using ApplicationServices;
using AudibleUtilities;
using LibationFileManager;
using Microsoft.AspNetCore.Mvc;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibraryController : ControllerBase
{
    [HttpPost("scan")]
    public async Task<ActionResult<ScanResponseDto>> ScanAll()
    {
        if (LibraryCommands.Scanning)
            return Conflict(new ScanResponseDto
            {
                Status = "already-scanning",
                Message = "A scan is already in progress"
            });

        try
        {
            var accounts = GetScanAccounts();
            if (accounts.Length == 0)
                return BadRequest(new ScanResponseDto
                {
                    Status = "error",
                    Message = "No accounts configured for scanning. Add accounts first."
                });

            var (totalCount, newCount) = await LibraryCommands.ImportAccountAsync(accounts);

            return Ok(new ScanResponseDto
            {
                Status = "completed",
                TotalCount = totalCount,
                NewCount = newCount
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Error(ex, "Error during library scan");
            return StatusCode(500, new ScanResponseDto
            {
                Status = "error",
                Message = ex.Message
            });
        }
    }

    [HttpPost("scan/{accountId}")]
    public async Task<ActionResult<ScanResponseDto>> ScanAccount(string accountId, [FromQuery] string? locale = null)
    {
        if (LibraryCommands.Scanning)
            return Conflict(new ScanResponseDto
            {
                Status = "already-scanning",
                Message = "A scan is already in progress"
            });

        try
        {
            var matches = GetScanAccounts()
                .Where(a => a.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(locale))
                matches = matches.Where(a => a.Locale?.Name?.Equals(locale, StringComparison.OrdinalIgnoreCase) == true);

            var accounts = matches.ToArray();

            if (accounts.Length == 0)
                return NotFound(new ScanResponseDto
                {
                    Status = "error",
                    Message = $"Account '{accountId}' (locale: {locale ?? "any"}) not found or not enabled for scanning"
                });

            var (totalCount, newCount) = await LibraryCommands.ImportAccountAsync(accounts);

            return Ok(new ScanResponseDto
            {
                Status = "completed",
                TotalCount = totalCount,
                NewCount = newCount
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Error(ex, "Error during library scan for account {AccountId}", accountId);
            return StatusCode(500, new ScanResponseDto
            {
                Status = "error",
                Message = ex.Message
            });
        }
    }

    [HttpGet("scan/status")]
    public ActionResult GetScanStatus()
    {
        return Ok(new { scanning = LibraryCommands.Scanning });
    }

    [HttpGet("export")]
    public ActionResult Export([FromQuery] string format = "json")
    {
        var library = DbContexts.GetLibrary_Flat_NoTracking();
        var books = library.Where(lb => !lb.IsDeleted).Select(lb => lb.ToDto()).ToList();

        return format.ToLowerInvariant() switch
        {
            "json" => Ok(books),
            _ => BadRequest(new { error = $"Unsupported format: {format}. Supported: json" })
        };
    }

    private static Account[] GetScanAccounts()
    {
        using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
        return persister.AccountsSettings.Accounts
            .Where(a => a.LibraryScan)
            .ToArray();
    }
}

