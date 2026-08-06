using Decanterr.Api.Models;
using Decanterr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Decanterr.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QueueController : ControllerBase
{
    private readonly LiberationQueueService _queue;

    public QueueController(LiberationQueueService queue)
    {
        _queue = queue;
    }

    [HttpGet]
    public ActionResult<List<QueueItemDto>> GetQueue()
    {
        return Ok(_queue.GetQueueItems());
    }

    [HttpDelete("{asin}")]
    public ActionResult Cancel(string asin)
    {
        var removed = _queue.TryRemove(asin);
        if (!removed)
            return NotFound(new { error = $"Item '{asin}' not found in queue or already processing" });

        return Ok(new { message = $"Removed '{asin}' from queue" });
    }

    [HttpDelete]
    public ActionResult ClearQueue()
    {
        var count = _queue.ClearPending();
        return Ok(new { message = $"Cleared {count} pending items from queue" });
    }
}

