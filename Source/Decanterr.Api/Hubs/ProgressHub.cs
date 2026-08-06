using Microsoft.AspNetCore.SignalR;

namespace Decanterr.Api.Hubs;

/// <summary>
/// SignalR hub for real-time liberation progress and queue updates.
/// 
/// Client events:
///   BookQueued       - A book was added to the liberation queue
///   ProgressUpdate   - Download/decrypt progress update
///   BookCompleted    - A book was successfully liberated
///   BookFailed       - A book liberation failed
///   ScanProgress     - Library scan progress update
/// </summary>
public class ProgressHub : Microsoft.AspNetCore.SignalR.Hub
{
    public override async Task OnConnectedAsync()
    {
        Serilog.Log.Logger.Information("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Serilog.Log.Logger.Information("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

