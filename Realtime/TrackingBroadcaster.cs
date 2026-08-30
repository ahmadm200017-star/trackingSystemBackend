using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MdfTracker.Api.Responses;
using Microsoft.AspNetCore.SignalR;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// Fan-out from the mobile stream to the dashboards.
///
/// Dashboards are SignalR connections (<see cref="TrackingHub"/>); the mobile app is a
/// plain WebSocket, so this also keeps the small registry of who is streaming and the
/// raw send helper the socket handler uses to reply to a device.
/// </summary>
public class TrackingBroadcaster
{
    private readonly IHubContext<TrackingHub, ITrackingClient> _hub;
    private readonly ConcurrentDictionary<string, byte> _dashboards = new();
    private readonly ConcurrentDictionary<Guid, MobileConnection> _mobiles = new();
    private readonly JsonSerializerOptions _json = JsonConfig.Options;

    public TrackingBroadcaster(IHubContext<TrackingHub, ITrackingClient> hub)
    {
        _hub = hub;
    }

    public int DashboardCount => _dashboards.Count;

    public int MobileCount => _mobiles.Count;

    public IReadOnlyCollection<MobileConnection> Mobiles => _mobiles.Values.ToList();

    // ---------- registration ----------

    public void DashboardConnected(string connectionId) => _dashboards[connectionId] = 0;

    public void DashboardDisconnected(string connectionId) => _dashboards.TryRemove(connectionId, out _);

    public void AddMobile(Guid sessionId, string sessionNumber) =>
        _mobiles[sessionId] = new MobileConnection(sessionId, sessionNumber);

    public void RemoveMobile(Guid sessionId) => _mobiles.TryRemove(sessionId, out _);

    public void ReportFps(Guid sessionId, double? fps)
    {
        if (fps.HasValue && _mobiles.TryGetValue(sessionId, out var mobile))
        {
            mobile.LastFps = fps;
        }
    }

    // ---------- fan-out to dashboards ----------

    /// <summary>Group-scoped: only dashboards watching this session pay for its frame rate.</summary>
    public Task BroadcastFrameAsync(LiveFrameMessage frame) =>
        _hub.Clients.Group(TrackingHub.GroupFor(frame.SessionId)).Frame(frame);

    public Task BroadcastStatusAsync(LiveStatusMessage status) =>
        _hub.Clients.Group(TrackingHub.GroupFor(status.SessionId)).Status(status);

    /// <summary>Lifecycle reaches every dashboard so session pickers stay current without polling.</summary>
    public Task BroadcastSessionStartedAsync(SessionResponse session) =>
        _hub.Clients.All.SessionStarted(session);

    public Task BroadcastSessionEndedAsync(SessionResponse session) =>
        _hub.Clients.All.SessionEnded(session);

    public Task BroadcastSessionDescribedAsync(SessionResponse session) =>
        _hub.Clients.All.SessionDescribed(session);

    // ---------- raw socket replies (mobile only) ----------

    public async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _json));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}

public class MobileConnection
{
    public MobileConnection(Guid sessionId, string sessionNumber)
    {
        SessionId = sessionId;
        SessionNumber = sessionNumber;
    }

    public Guid SessionId { get; }

    public string SessionNumber { get; }

    public double? LastFps { get; set; }
}
