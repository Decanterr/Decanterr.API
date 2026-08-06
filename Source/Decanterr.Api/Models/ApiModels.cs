namespace Decanterr.Api.Models;

public record BookDto
{
    public string AudibleProductId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public string TitleWithSubtitle { get; init; } = "";
    public string? Description { get; init; }
    public int LengthInMinutes { get; init; }
    public string ContentType { get; init; } = "";
    public List<string> Authors { get; init; } = [];
    public List<string> Narrators { get; init; } = [];
    public List<string> Series { get; init; } = [];
    public List<string> Categories { get; init; } = [];
    public string? Locale { get; init; }
    public bool IsAbridged { get; init; }
    public RatingDto? CommunityRating { get; init; }
    public UserDefinedItemDto? UserData { get; init; }
    public LibraryBookDto? LibraryInfo { get; init; }
    public string? PictureId { get; init; }
    public DateTime? DatePublished { get; init; }
    public string? Publisher { get; init; }
}

public record RatingDto
{
    public float OverallRating { get; init; }
    public float PerformanceRating { get; init; }
    public float StoryRating { get; init; }
}

public record UserDefinedItemDto
{
    public string BookStatus { get; init; } = "";
    public string? PdfStatus { get; init; }
    public string? Tags { get; init; }
    public float? UserRating { get; init; }
    public DateTime? LastDownloaded { get; init; }
    public bool IsFinished { get; init; }
}

public record LibraryBookDto
{
    public DateTime DateAdded { get; init; }
    public string Account { get; init; } = "";
    public bool IsDeleted { get; init; }
    public bool IsAudiblePlus { get; init; }
    public bool AbsentFromLastScan { get; init; }
}

public record SearchResultDto
{
    public List<BookDto> Books { get; init; } = [];
    public int TotalCount { get; init; }
    public string? Query { get; init; }
}

public record LiberateRequestDto
{
    /// <summary>ASIN, Audible URL, or product ID</summary>
    public string Input { get; init; } = "";
}

public record LiberateResponseDto
{
    public string Status { get; init; } = "";
    public string Asin { get; init; } = "";
    public string? Title { get; init; }
    public string? Message { get; init; }
}

public record ScanResponseDto
{
    public string Status { get; init; } = "";
    public int TotalCount { get; init; }
    public int NewCount { get; init; }
    public string? Message { get; init; }
}

public record AccountDto
{
    public string AccountId { get; init; } = "";
    public string? AccountName { get; init; }
    public string? Locale { get; init; }
    public bool LibraryScan { get; init; }
    public bool HasTokens { get; init; }
}

public record QueueItemDto
{
    public string Asin { get; init; } = "";
    public string? Title { get; init; }
    public string Status { get; init; } = "";
    public double ProgressPercent { get; init; }
    public string? StatusMessage { get; init; }
    public DateTime QueuedAt { get; init; }
}

public record StatsDto
{
    public int TotalBooks { get; init; }
    public int Liberated { get; init; }
    public int NotLiberated { get; init; }
    public int InError { get; init; }
    public int InQueue { get; init; }
    public int Podcasts { get; init; }
}

public record UpdateTagsDto
{
    public string Tags { get; init; } = "";
}

public record UpdateRatingDto
{
    public float Rating { get; init; }
}

public record UpdateStatusDto
{
    public string Status { get; init; } = "";
}

public record BulkLiberateDto
{
    public List<string> Asins { get; init; } = [];
}

