using MdfTracker.Api.Realtime;
using MdfTracker.Api.Responses;
using Microsoft.AspNetCore.Mvc;

namespace MdfTracker.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly FrameQueue _queue;
    private readonly TrackingBroadcaster _broadcaster;

    public HealthController(FrameQueue queue, TrackingBroadcaster broadcaster)
    {
        _queue = queue;
        _broadcaster = broadcaster;
    }

    [HttpGet("/")]
    public ActionResult<ApiIndexResponse> Index() => Ok(new ApiIndexResponse
    {
        Name = "MDF Object Tracking API",
        Endpoints = new List<string>
        {
            "POST   /api/sessions",
            "POST   /api/sessions/{id}/data                (one point or a batch; duplicates ignored)",
            "POST   /api/sessions/{id}/description",
            "POST   /api/sessions/{id}/end",
            "POST   /api/sessions/{id}/finish              (alias of /end)",
            "GET    /api/sessions?page=1&perPage=20&status=&trackerAlgorithm=&cameraType=&isSuccessful=&search=",
            "GET    /api/sessions/{id}",
            "GET    /api/sessions/{id}/frames?page=1&perPage=100&sort=desc",
            "GET    /api/sessions/{id}/data?limit=50000   (position + fps, time order)",
            "GET    /api/sessions/{id}/events",
            "GET    /api/sessions/{id}/analytics?maxPoints=5000",
            "DELETE /api/sessions/{id}",
            "GET    /api/stats/overview",
            "GET    /api/stats/live",
            "GET    /api/health",
            "WS     /ws/track?sessionId={id}          (mobile ingest, plain WebSocket)",
            "HUB    /hubs/tracking                    (dashboards, SignalR)"
        }
    });

    [HttpGet("/api/health")]
    public ActionResult<HealthResponse> Health() => Ok(new HealthResponse
    {
        Status = "ok",
        DroppedFrames = _queue.Dropped,
        DashboardConnections = _broadcaster.DashboardCount,
        MobileConnections = _broadcaster.MobileCount
    });
}
