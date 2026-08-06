using Decanterr.Api.Hubs;
using Decanterr.Api.Models;
using DataLayer;
using Dinah.Core.Net.Http;
using FileLiberator;
using LibationFileManager;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Decanterr.Api.Services;

public class LiberationQueueService : BackgroundService
{
    private readonly Channel<LiberationWorkItem> _channel = Channel.CreateUnbounded<LiberationWorkItem>();
    private readonly ConcurrentDictionary<string, LiberationWorkItem> _activeItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHubContext<ProgressHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiberationQueueService> _logger;
    private readonly AudiobookshelfUploadService _absUploader;

    public LiberationQueueService(
        IHubContext<ProgressHub> hubContext,
        IConfiguration configuration,
        ILogger<LiberationQueueService> logger,
        AudiobookshelfUploadService absUploader)
    {
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
        _absUploader = absUploader;
    }

    public void Enqueue(LibraryBook libraryBook)
    {
        var asin = libraryBook.Book.AudibleProductId;

        if (_activeItems.ContainsKey(asin))
        {
            _logger.LogInformation("Book {Asin} is already queued or processing", asin);
            return;
        }

        var item = new LiberationWorkItem
        {
            Asin = asin,
            Title = libraryBook.Book.TitleWithSubtitle,
            Status = "queued",
            QueuedAt = DateTime.UtcNow
        };

        if (!_activeItems.TryAdd(asin, item))
            return;

        _channel.Writer.TryWrite(item);
        _ = _hubContext.Clients.All.SendAsync("BookQueued", new QueueItemDto
        {
            Asin = item.Asin,
            Title = item.Title,
            Status = "queued",
            QueuedAt = item.QueuedAt
        });
    }

    public bool TryRemove(string asin)
    {
        if (_activeItems.TryGetValue(asin, out var item) && item.Status == "queued")
        {
            item.Status = "cancelled";
            _activeItems.TryRemove(asin, out _);
            return true;
        }
        return false;
    }

    public int ClearPending()
    {
        var pending = _activeItems.Where(kvp => kvp.Value.Status == "queued").ToList();
        foreach (var kvp in pending)
        {
            kvp.Value.Status = "cancelled";
            _activeItems.TryRemove(kvp.Key, out _);
        }
        return pending.Count;
    }

    public List<QueueItemDto> GetQueueItems()
    {
        return _activeItems.Values
            .OrderBy(i => i.QueuedAt)
            .Select(i => new QueueItemDto
            {
                Asin = i.Asin,
                Title = i.Title,
                Status = i.Status,
                ProgressPercent = i.ProgressPercent,
                StatusMessage = i.StatusMessage,
                QueuedAt = i.QueuedAt
            })
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Liberation queue service started");

        await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (workItem.Status == "cancelled")
                continue;

            try
            {
                await ProcessWorkItemAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing liberation for {Asin}", workItem.Asin);
                workItem.Status = "error";
                workItem.StatusMessage = ex.Message;

                await _hubContext.Clients.All.SendAsync("BookFailed", new QueueItemDto
                {
                    Asin = workItem.Asin,
                    Title = workItem.Title,
                    Status = "error",
                    StatusMessage = ex.Message,
                    QueuedAt = workItem.QueuedAt
                }, stoppingToken);
            }
            finally
            {
                _activeItems.TryRemove(workItem.Asin, out _);
            }
        }
    }

    private async Task ProcessWorkItemAsync(LiberationWorkItem workItem, CancellationToken stoppingToken)
    {
        workItem.Status = "processing";

        await _hubContext.Clients.All.SendAsync("ProgressUpdate", new QueueItemDto
        {
            Asin = workItem.Asin,
            Title = workItem.Title,
            Status = "processing",
            StatusMessage = "Starting download...",
            QueuedAt = workItem.QueuedAt
        }, stoppingToken);

        // Re-fetch from DB to get tracked entity
        var lb = ApplicationServices.DbContexts.GetLibraryBook_Flat_NoTracking(workItem.Asin);
        if (lb is null)
        {
            workItem.Status = "error";
            workItem.StatusMessage = "Book not found in library";
            return;
        }

        var processable = DownloadDecryptBook.Create(Configuration.Instance);

        string? createdFilePath = null;

        processable.FileCreated += (_, args) =>
        {
            createdFilePath = args.path;
            _logger.LogInformation("File created for {Asin}: {Path}", workItem.Asin, args.path);
        };

        processable.StreamingProgressChanged += (_, progress) =>
        {
            workItem.ProgressPercent = progress.ProgressPercentage ?? 0;
            workItem.StatusMessage = $"Downloading: {progress.ProgressPercentage ?? 0:F1}%";

            _ = _hubContext.Clients.All.SendAsync("ProgressUpdate", new QueueItemDto
            {
                Asin = workItem.Asin,
                Title = workItem.Title,
                Status = "processing",
                ProgressPercent = progress.ProgressPercentage ?? 0,
                StatusMessage = workItem.StatusMessage,
                QueuedAt = workItem.QueuedAt
            });
        };

        processable.StatusUpdate += (_, message) =>
        {
            workItem.StatusMessage = message;
        };

        processable.Completed += (_, completedLb) =>
        {
            workItem.Status = "completed";
            workItem.ProgressPercent = 100;
            workItem.StatusMessage = "Liberation complete";
        };

        var status = await processable.ProcessSingleAsync(lb, validate: true);

        if (status.HasErrors)
        {
            workItem.Status = "error";
            workItem.StatusMessage = string.Join("; ", status.Errors);

            await _hubContext.Clients.All.SendAsync("BookFailed", new QueueItemDto
            {
                Asin = workItem.Asin,
                Title = workItem.Title,
                Status = "error",
                StatusMessage = workItem.StatusMessage,
                QueuedAt = workItem.QueuedAt
            }, stoppingToken);
        }
        else
        {
            workItem.Status = "completed";
            workItem.ProgressPercent = 100;

            await _hubContext.Clients.All.SendAsync("BookCompleted", new QueueItemDto
            {
                Asin = workItem.Asin,
                Title = workItem.Title,
                Status = "completed",
                ProgressPercent = 100,
                StatusMessage = "Liberation complete",
                QueuedAt = workItem.QueuedAt
            }, stoppingToken);

            // Upload to Audiobookshelf if configured
            if (createdFilePath is not null)
            {
                try
                {
                    await _absUploader.UploadAsync(workItem.Asin, createdFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload {Asin} to Audiobookshelf", workItem.Asin);
                }
            }
        }
    }

    internal class LiberationWorkItem
    {
        public string Asin { get; set; } = "";
        public string? Title { get; set; }
        public string Status { get; set; } = "queued";
        public double ProgressPercent { get; set; }
        public string? StatusMessage { get; set; }
        public DateTime QueuedAt { get; set; }
    }
}

