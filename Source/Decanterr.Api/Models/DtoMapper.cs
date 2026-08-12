using DataLayer;
using Decanterr.Api.Services;

namespace Decanterr.Api.Models;

public static class DtoMapper
{
    public static BookDto ToDto(this LibraryBook lb)
    {
        var book = lb.Book;
        return new BookDto
        {
            AudibleProductId = book.AudibleProductId,
            InAudiobookshelf = AudiobookshelfUploadTracker.IsUploaded(book.AudibleProductId)
                || AudiobookshelfLibraryCache.Contains(book.AudibleProductId),
            Title = book.Title,
            Subtitle = book.Subtitle,
            TitleWithSubtitle = book.TitleWithSubtitle,
            Description = book.Description,
            LengthInMinutes = book.LengthInMinutes,
            ContentType = book.ContentType.ToString(),
            Authors = book.Authors.Select(c => c.Name).ToList(),
            Narrators = book.Narrators.Select(c => c.Name).ToList(),
            Series = book.SeriesLink.Select(s => $"{s.Series.Name} #{s.Order}").ToList(),
            Categories = book.LowestCategoryNames().ToList(),
            Locale = book.Locale,
            IsAbridged = book.IsAbridged,
            PictureId = book.PictureId,
            DatePublished = book.DatePublished,
            Publisher = book.Publisher,
            CommunityRating = book.Rating is null ? null : new RatingDto
            {
                OverallRating = book.Rating.OverallRating,
                PerformanceRating = book.Rating.PerformanceRating,
                StoryRating = book.Rating.StoryRating
            },
            UserData = new UserDefinedItemDto
            {
                BookStatus = book.UserDefinedItem.BookStatus.ToString(),
                PdfStatus = book.UserDefinedItem.PdfStatus?.ToString(),
                Tags = book.UserDefinedItem.Tags,
                UserRating = book.UserDefinedItem.Rating?.OverallRating ?? 0,
                LastDownloaded = book.UserDefinedItem.LastDownloaded,
                IsFinished = book.UserDefinedItem.IsFinished
            },
            LibraryInfo = new LibraryBookDto
            {
                DateAdded = lb.DateAdded,
                Account = lb.Account,
                IsDeleted = lb.IsDeleted,
                IsAudiblePlus = lb.IsAudiblePlus,
                AbsentFromLastScan = lb.AbsentFromLastScan
            }
        };
    }
}

