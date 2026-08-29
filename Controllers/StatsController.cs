using MdfTracker.Api.Data;
using MdfTracker.Api.Models;
using MdfTracker.Api.Realtime;
using MdfTracker.Api.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TrackingBroadcaster _broadcaster;
    private readonly FrameQueue _queue;

    public StatsController(AppDbContext db, TrackingBroadcaster broadcaster, FrameQueue queue)
    {
        _db = db;
        _broadcaster = broadcaster;
        _queue = queue;
    }

    /// <summary>KPI cards and the success/failure breakdown by tracker.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewResponse>> Overview(CancellationToken cancellationToken)
    {
        var sessions = await _db.TrackingSessions.AsNoTracking()
            .Select(s => new
            {
                s.Status,
                s.IsSuccessful,
                s.TrackerAlgorithm,
                s.AverageFps
            })
            .ToListAsync(cancellationToken);

        var totalFrames = await _db.SessionFrames.AsNoTracking().CountAsync(cancellationToken);

        var completed = sessions.Where(s => s.Status == SessionStatus.Completed).ToList();
        var reportedFps = completed.Where(s => s.AverageFps.HasValue).Select(s => s.AverageFps!.Value).ToList();

        var response = new OverviewResponse
        {
            TotalSessions = sessions.Count,
            ActiveSessions = sessions.Count(s => s.Status == SessionStatus.Active),
            CompletedSessions = completed.Count,
            SuccessRate = Percentage(completed.Count(s => s.IsSuccessful), completed.Count),
            LowestFps = reportedFps.Count > 0 ? reportedFps.Min() : null,
            HighestFps = reportedFps.Count > 0 ? reportedFps.Max() : null,
            AverageFps = reportedFps.Count > 0 ? Math.Round(reportedFps.Average(), 2) : null,
            TotalFrames = totalFrames,
            ByAlgorithm = Enum.GetValues<TrackerAlgorithm>()
                .Select(algorithm =>
                {
                    var group = completed.Where(s => s.TrackerAlgorithm == algorithm).ToList();
                    var fps = group.Where(s => s.AverageFps.HasValue).Select(s => s.AverageFps!.Value).ToList();
                    var successCount = group.Count(s => s.IsSuccessful);

                    return new AlgorithmBreakdownItem
                    {
                        TrackerAlgorithm = algorithm,
                        SuccessCount = successCount,
                        FailureCount = group.Count - successCount,
                        TotalCount = group.Count,
                        SuccessRate = Percentage(successCount, group.Count),
                        AverageFps = fps.Count > 0 ? Math.Round(fps.Average(), 2) : null
                    };
                })
                .ToList()
        };

        return Ok(response);
    }

    /// <summary>Who is connected to the live tracking room right now.</summary>
    [HttpGet("live")]
    public ActionResult<LiveRoomResponse> Live()
    {
        return Ok(new LiveRoomResponse
        {
            DashboardConnections = _broadcaster.DashboardCount,
            MobileConnections = _broadcaster.MobileCount,
            DroppedFrames = _queue.Dropped,
            Streams = _broadcaster.Mobiles
                .Select(m => new LiveStreamItem
                {
                    SessionId = m.SessionId,
                    SessionNumber = m.SessionNumber,
                    LastFps = m.LastFps
                })
                .OrderByDescending(m => m.SessionNumber)
                .ToList()
        });
    }

    private static decimal Percentage(int part, int total) =>
        total == 0 ? 0 : Math.Round(part * 100m / total, 2);
}
