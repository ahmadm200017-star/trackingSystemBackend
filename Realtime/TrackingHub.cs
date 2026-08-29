using MdfTracker.Api.Data;
using MdfTracker.Api.Responses;
using MdfTracker.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// The dashboard side of the live tracking room, at <c>/hubs/tracking</c>.
///
/// Frames are high volume, so they are group-scoped: a dashboard only receives the
/// session it asked for. Session lifecycle is low volume and everyone needs it to keep
/// their session list current, so it goes to all clients.
///
/// The mobile app does not connect here — it keeps pushing over the plain WebSocket at
/// <c>/ws/track</c> (Flutter's <c>web_socket_channel</c>), and the server relays.
/// </summary>
public class TrackingHub : Hub<ITrackingClient>
{
    private readonly AppDbContext _db;
    private readonly TrackingBroadcaster _broadcaster;
    private readonly ILogger<TrackingHub> _logger;

    public TrackingHub(AppDbContext db, TrackingBroadcaster broadcaster, ILogger<TrackingHub> logger)
    {
        _db = db;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public static string GroupFor(Guid sessionId) => $"session-{sessionId}";

    public override async Task OnConnectedAsync()
    {
        _broadcaster.DashboardConnected(Context.ConnectionId);

        var active = await _db.TrackingSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync(Context.ConnectionAborted);

        await Clients.Caller.ActiveSessions(active.Select(s => SessionResponse.From(s)).ToList());

        _logger.LogInformation("Dashboard {ConnectionId} joined the live room", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _broadcaster.DashboardDisconnected(Context.ConnectionId);
        _logger.LogInformation("Dashboard {ConnectionId} left the live room", Context.ConnectionId);

        // Group membership is dropped with the connection; nothing to clean up by hand.
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Start receiving <see cref="ITrackingClient.Frame"/> / <see cref="ITrackingClient.Status"/> for one session.</summary>
    public Task SubscribeToSession(Guid sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(sessionId), Context.ConnectionAborted);

    public Task UnsubscribeFromSession(Guid sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(sessionId), Context.ConnectionAborted);

    /// <summary>Re-fetch the active list without reconnecting (used after a reconnect gap).</summary>
    public async Task<List<SessionResponse>> GetActiveSessions()
    {
        var active = await _db.TrackingSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync(Context.ConnectionAborted);

        return active.Select(s => SessionResponse.From(s)).ToList();
    }
}
