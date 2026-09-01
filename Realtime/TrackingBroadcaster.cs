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

    /// <summary>
    /// When each session's device was last connected. Deliberately kept after the socket is
    /// removed, because that is exactly the moment the reaper needs to measure from: without
    /// it, a disconnected session is indistinguishable from one that never connected.
    ///
    /// Entries are dropped when the session is closed, so this cannot grow without bound.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSeen = new();
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

    public void AddMobile(Guid sessionId, string sessionNumber)
    {
        _mobiles[sessionId] = new MobileConnection(sessionId, sessionNumber);
        _lastSeen[sessionId] = DateTimeOffset.UtcNow;
    }

    public void RemoveMobile(Guid sessionId)
    {
        _mobiles.TryRemove(sessionId, out _);
        // Stamped on the way out, not on the way in, so the gap is measured from the
        // disconnect rather than from the start of a long healthy stream.
        _lastSeen[sessionId] = DateTimeOffset.UtcNow;
    }

    /// <summary>True while a device is streaming this session.</summary>
    public bool IsStreaming(Guid sessionId) => _mobiles.ContainsKey(sessionId);

    /// <summary>
    /// When this session's device was last connected, or null if it never has been - which
    /// is the normal state for the moment between creating a session over REST and the
    /// socket handshake landing.
    /// </summary>
    public DateTimeOffset? LastSeen(Guid sessionId) =>
        _lastSeen.TryGetValue(sessionId, out var seen) ? seen : null;

    /// <summary>Called once a session is closed; nothing needs to track it any more.</summary>
    public void ForgetSession(Guid sessionId)
    {
        _mobiles.TryRemove(sessionId, out _);
        _lastSeen.TryRemove(sessionId, out _);
    }

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
