using Decanterr.Api.Models;
using Decanterr.Api.Services;
using ApplicationServices;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class LiberateController : ControllerBase
{
    private readonly LiberationQueueService _queue;

    public LiberateController(LiberationQueueService queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Simple liberation endpoint — accepts ASIN, Audible URL, or product ID.
    /// Designed for Siri Shortcuts and automation.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<LiberateResponseDto>> Liberate([FromBody] LiberateRequestDto dto)
    {
        var asin = ExtractAsin(dto.Input);
        if (string.IsNullOrWhiteSpace(asin))
            return BadRequest(new LiberateResponseDto
            {
                Status = "error",
                Message = $"Could not extract ASIN from input: '{dto.Input}'"
            });

        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
        {
            // Check if book exists but is deleted
            var deletedLb = DbContexts.GetLibraryBook_Flat_NoTracking(asin, includeDeleted: true);
            if (deletedLb is not null)
            {
                // Restore the book from trash before liberating
                await new[] { deletedLb }.RestoreBooksAsync();
                // Re-fetch after restore
                lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
                if (lb is null)
                    return StatusCode(500, new LiberateResponseDto
                    {
                        Status = "error",
                        Asin = asin,
                        Message = "Book was restored but could not be re-fetched"
                    });
            }
            else
            {
                // Book not in library yet — caller may need to scan first
                return NotFound(new LiberateResponseDto
                {
                    Status = "not-found",
                    Asin = asin,
                    Message = "Book not found in library. Try scanning your library first (POST /api/library/scan)."
                });
            }
        }

        _queue.Enqueue(lb);

        return Accepted(new LiberateResponseDto
        {
            Status = "queued",
            Asin = asin,
            Title = lb.Book.TitleWithSubtitle,
            Message = "Book queued for liberation"
        });
    }

    [HttpPost("{asin}")]
    public async Task<ActionResult<LiberateResponseDto>> LiberateByAsin(string asin)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
        {
            var deletedLb = DbContexts.GetLibraryBook_Flat_NoTracking(asin, includeDeleted: true);
            if (deletedLb is not null)
            {
                await new[] { deletedLb }.RestoreBooksAsync();
                lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
                if (lb is null)
                    return StatusCode(500, new LiberateResponseDto { Status = "error", Asin = asin, Message = "Book was restored but could not be re-fetched" });
            }
            else
            {
                return NotFound(new LiberateResponseDto
                {
                    Status = "not-found",
                    Asin = asin,
                    Message = "Book not found in library"
                });
            }
        }

        _queue.Enqueue(lb);

        return Accepted(new LiberateResponseDto
        {
            Status = "queued",
            Asin = asin,
            Title = lb.Book.TitleWithSubtitle
        });
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<List<LiberateResponseDto>>> LiberateBulk([FromBody] BulkLiberateDto dto)
    {
        var results = new List<LiberateResponseDto>();

        foreach (var asin in dto.Asins)
        {
            var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
            if (lb is null)
            {
                var deletedLb = DbContexts.GetLibraryBook_Flat_NoTracking(asin, includeDeleted: true);
                if (deletedLb is not null)
                {
                    await new[] { deletedLb }.RestoreBooksAsync();
                    lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
                    if (lb is null)
                    {
                        results.Add(new LiberateResponseDto { Status = "error", Asin = asin, Message = "Book was restored but could not be re-fetched" });
                        continue;
                    }
                }
                else
                {
                    results.Add(new LiberateResponseDto
                    {
                        Status = "not-found",
                        Asin = asin,
                        Message = "Book not found in library"
                    });
                    continue;
                }
            }

            _queue.Enqueue(lb);
            results.Add(new LiberateResponseDto
            {
                Status = "queued",
                Asin = asin,
                Title = lb.Book.TitleWithSubtitle
            });
        }

        return Accepted(results);
    }

    internal static string? ExtractAsin(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        // Direct ASIN (10-char alphanumeric starting with B)
        if (AsinPattern().IsMatch(input))
            return input.ToUpperInvariant();

        // Audible URL: extract ASIN from path
        // e.g., https://www.audible.com/pd/Book-Title/B08G9PRS1K
        var urlMatch = AudibleUrlPattern().Match(input);
        if (urlMatch.Success)
            return urlMatch.Groups[1].Value.ToUpperInvariant();

        return null;
    }

    [GeneratedRegex(@"^[Bb][0-9A-Za-z]{9}$")]
    private static partial Regex AsinPattern();

    [GeneratedRegex(@"audible\.[^/]+/pd/[^/]+/([Bb][0-9A-Za-z]{9})", RegexOptions.IgnoreCase)]
    private static partial Regex AudibleUrlPattern();
}

