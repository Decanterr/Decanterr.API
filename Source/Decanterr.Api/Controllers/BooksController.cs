using Decanterr.Api.Models;
using ApplicationServices;
using DataLayer;
using LibationFileManager;
using Microsoft.AspNetCore.Mvc;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<BookDto>> GetAll(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null)
    {
        List<LibraryBook> library;
        if (includeDeleted)
        {
            // GetLibrary_Flat_NoTracking() always excludes deleted books at the query level,
            // so deleted books must be fetched separately via GetDeletedLibraryBooks().
            var seenAsins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            library = [];
            foreach (var lb in DbContexts.GetLibrary_Flat_NoTracking().Concat(DbContexts.GetDeletedLibraryBooks()))
            {
                if (seenAsins.Add(lb.Book.AudibleProductId))
                    library.Add(lb);
            }
        }
        else
        {
            library = DbContexts.GetLibrary_Flat_NoTracking();
        }

        var total = library.Count;

        if (skip.HasValue)
            library = library.Skip(skip.Value).ToList();
        if (take.HasValue)
            library = library.Take(take.Value).ToList();

        var books = library.Select(lb => lb.ToDto()).ToList();
        return Ok(books);
    }

    [HttpGet("{asin}")]
    public ActionResult<BookDto> GetByAsin(string asin)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        return Ok(lb.ToDto());
    }

    [HttpGet("search")]
    public ActionResult<SearchResultDto> Search([FromQuery] string q = "")
    {
        var results = SearchEngineCommands.Search(q);
        var library = DbContexts.GetLibrary_Flat_NoTracking();

        var matchedAsins = results.Docs.Select(d => d.ProductId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = library.Where(lb => matchedAsins.Contains(lb.Book.AudibleProductId)).ToList();

        return Ok(new SearchResultDto
        {
            Books = matched.Select(lb => lb.ToDto()).ToList(),
            TotalCount = matched.Count,
            Query = q
        });
    }

    [HttpGet("{asin}/cover")]
    public ActionResult GetCover(string asin)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        var pictureId = lb.Book.PictureId;
        if (string.IsNullOrWhiteSpace(pictureId))
            return NotFound(new { error = "No cover image available" });

        var picDef = new PictureDefinition(pictureId, PictureSize._500x500);
        var picturePath = PictureStorage.GetPicturePathSynchronously(picDef);
        if (string.IsNullOrWhiteSpace(picturePath) || !System.IO.File.Exists(picturePath))
            return NotFound(new { error = "Cover image file not found" });

        return PhysicalFile(picturePath, "image/jpeg");
    }

    [HttpPut("{asin}/tags")]
    public async Task<ActionResult> UpdateTags(string asin, [FromBody] UpdateTagsDto dto)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        await lb.UpdateUserDefinedItemAsync(tags: dto.Tags);
        return Ok(new { message = "Tags updated" });
    }

    [HttpPut("{asin}/rating")]
    public async Task<ActionResult> UpdateRating(string asin, [FromBody] UpdateRatingDto dto)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        var rating = new Rating(dto.Rating, dto.Rating, dto.Rating);
        await lb.UpdateUserDefinedItemAsync(rating: rating);
        return Ok(new { message = "Rating updated" });
    }

    [HttpPut("{asin}/status")]
    public async Task<ActionResult> UpdateStatus(string asin, [FromBody] UpdateStatusDto dto)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        if (!Enum.TryParse<LiberatedStatus>(dto.Status, true, out var status))
            return BadRequest(new { error = $"Invalid status: {dto.Status}. Valid values: {string.Join(", ", Enum.GetNames<LiberatedStatus>())}" });

        await lb.UpdateUserDefinedItemAsync(bookStatus: status);
        return Ok(new { message = "Status updated" });
    }

    [HttpDelete("{asin}")]
    public async Task<ActionResult> Delete(string asin)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        await new[] { lb }.RemoveBooksAsync();
        return Ok(new { message = "Book deleted (soft)" });
    }

    [HttpPost("{asin}/restore")]
    public async Task<ActionResult> Restore(string asin)
    {
        var lb = DbContexts.GetLibraryBook_Flat_NoTracking(asin, includeDeleted: true);
        if (lb is null)
            return NotFound(new { error = $"Book with ASIN '{asin}' not found" });

        await new[] { lb }.RestoreBooksAsync();
        return Ok(new { message = "Book restored" });
    }

    [HttpGet("stats")]
    public ActionResult<StatsDto> GetStats()
    {
        var library = DbContexts.GetLibrary_Flat_NoTracking();
        var active = library.Where(lb => !lb.IsDeleted).ToList();

        return Ok(new StatsDto
        {
            TotalBooks = active.Count,
            Liberated = active.Count(lb => lb.Book.UserDefinedItem.BookStatus == LiberatedStatus.Liberated),
            NotLiberated = active.Count(lb => lb.Book.UserDefinedItem.BookStatus == LiberatedStatus.NotLiberated),
            InError = active.Count(lb => lb.Book.UserDefinedItem.BookStatus == LiberatedStatus.Error),
            Podcasts = active.Count(lb => lb.Book.ContentType == ContentType.Episode)
        });
    }
}

