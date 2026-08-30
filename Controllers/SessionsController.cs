using MdfTracker.Api.Data;
using MdfTracker.Api.Models;
using MdfTracker.Api.Realtime;
using MdfTracker.Api.Requests;
using MdfTracker.Api.Responses;
using MdfTracker.Api.Services;
using MdfTracker.Api.Services.Vision;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private const int DefaultAnalyticsPoints = 5000;

    private readonly AppDbContext _db;
    private readonly SessionNumberGenerator _numbers;
    private readonly TrackingBroadcaster _broadcaster;
    private readonly GroqVisionClient _vision;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        AppDbContext db,
        SessionNumberGenerator numbers,
        TrackingBroadcaster broadcaster,
        GroqVisionClient vision,
        ILogger<SessionsController> logger)
    {
        _db = db;
        _numbers = numbers;
        _broadcaster = broadcaster;
        _vision = vision;
        _logger = logger;
    }

    /// <summary>Start a session. The device then streams frames over /ws/track.</summary>
    [HttpPost]
    public async Task<ActionResult<SessionResponse>> Create(CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var startTime = request.StartTime ?? DateTimeOffset.UtcNow;

        var session = new TrackingSession
        {
            SessionNumber = await _numbers.NextAsync(startTime, cancellationToken),
            StartTime = startTime,
            CameraType = request.CameraType!.Value,
            TrackerAlgorithm = request.TrackerAlgorithm!.Value,
            Status = SessionStatus.Active,
            IsSuccessful = false,
            DeviceModel = Blank(request.DeviceModel),
            OsVersion = Blank(request.OsVersion),
            AppVersion = Blank(request.AppVersion),
            ProcessingScale = request.ProcessingScale.HasValue
                ? Math.Round(request.ProcessingScale.Value, 2)
                : null,
            ScreenWidth = request.ScreenWidth ?? 0,
            ScreenHeight = request.ScreenHeight ?? 0
        };

        _db.TrackingSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        var response = SessionResponse.From(session);
        await _broadcaster.BroadcastSessionStartedAsync(response);

        return CreatedAtAction(nameof(Get), new { id = session.Id }, response);
    }

    /// <summary>Close a session with its summary: average FPS and whether tracking held.</summary>
    [HttpPost("{id:guid}/end")]
    public async Task<ActionResult<SessionResponse>> End(Guid id, EndSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await _db.TrackingSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        if (session.Status == SessionStatus.Completed)
        {
            return Conflict(new MessageResponse("Session is already completed."));
        }

        session.EndTime = request.EndTime ?? DateTimeOffset.UtcNow;
        session.AverageFps = request.AverageFps.HasValue ? Math.Round(request.AverageFps.Value, 2) : null;
        session.IsSuccessful = request.IsSuccessful ?? false;
        session.Status = SessionStatus.Completed;

        await _db.SaveChangesAsync(cancellationToken);

        var counts = await CountsAsync(new[] { session.Id }, cancellationToken);
        var response = SessionResponse.From(session, FrameCountOf(counts, session.Id), LostCountOf(counts, session.Id));

        _broadcaster.RemoveMobile(session.Id);
        await _broadcaster.BroadcastSessionEndedAsync(response);

        return Ok(response);
    }

    /// <summary>
    /// Describe the tracked object from the first frame of the run.
    ///
    /// The device posts the padded crop around its seed box as soon as the tracker is
    /// seeded; this asks Groq what it is, stores the answer on the session and pushes it to
    /// the dashboards. Called at most once per session — a second call is rejected rather
    /// than spending another Groq request on the same object.
    /// </summary>
    [HttpPost("{id:guid}/description")]
    public async Task<ActionResult<SessionResponse>> Describe(
        Guid id,
        DescribeObjectRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _db.TrackingSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        if (!string.IsNullOrWhiteSpace(session.ObjectDescription))
        {
            return Conflict(new MessageResponse("This session already has an object description."));
        }

        if (!_vision.IsConfigured)
        {
            // Not an error the device can act on: tracking is unaffected, the session simply
            // has no description. 503 keeps it distinct from a Groq outage.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new MessageResponse("Object description is disabled — Groq:ApiKey is not configured."));
        }

        var image = StripDataUrl(request.ImageBase64!);
        if (!IsBase64(image))
        {
            return BadRequest(new MessageResponse("imageBase64 is not valid base64."));
        }

        string description;
        try
        {
            description = await _vision.DescribeAsync(
                image,
                request.MimeType ?? "image/jpeg",
                cancellationToken);
        }
        catch (GroqVisionException error)
        {
            _logger.LogWarning(error, "Object description failed for session {SessionId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new MessageResponse(error.Message));
        }

        session.ObjectDescription = description;
        await _db.SaveChangesAsync(cancellationToken);

        var counts = await CountsAsync(new[] { id }, cancellationToken);
        var response = SessionResponse.From(session, FrameCountOf(counts, id), LostCountOf(counts, id));

        await _broadcaster.BroadcastSessionDescribedAsync(response);

        return Ok(response);
    }

    /// <summary>Session history, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SessionResponse>>> List(
        [FromQuery] SessionListRequest request,
        CancellationToken cancellationToken)
    {
        request.Normalize();

        var query = _db.TrackingSessions.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(s => s.Status == request.Status.Value);
        }

        if (request.TrackerAlgorithm.HasValue)
        {
            query = query.Where(s => s.TrackerAlgorithm == request.TrackerAlgorithm.Value);
        }

        if (request.CameraType.HasValue)
        {
            query = query.Where(s => s.CameraType == request.CameraType.Value);
        }

        if (request.IsSuccessful.HasValue)
        {
            query = query.Where(s => s.IsSuccessful == request.IsSuccessful.Value);
        }

        if (request.Search is not null)
        {
            query = query.Where(s => s.SessionNumber.Contains(request.Search));
        }

        var total = await query.CountAsync(cancellationToken);

        var sessions = await query
            .OrderByDescending(s => s.StartTime)
            .ThenByDescending(s => s.SessionNumber)
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage)
            .ToListAsync(cancellationToken);

        var counts = await CountsAsync(sessions.Select(s => s.Id).ToList(), cancellationToken);

        var data = sessions
            .Select(s => SessionResponse.From(s, FrameCountOf(counts, s.Id), LostCountOf(counts, s.Id)))
            .ToList();

        return Ok(PagedResponse<SessionResponse>.Create(data, request.Page, request.PerPage, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SessionResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var session = await _db.TrackingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        var counts = await CountsAsync(new[] { id }, cancellationToken);
        return Ok(SessionResponse.From(session, FrameCountOf(counts, id), LostCountOf(counts, id)));
    }

    /// <summary>Frames, newest first by default; charts ask for sort=asc.</summary>
    [HttpGet("{id:guid}/frames")]
    public async Task<ActionResult<PagedResponse<FrameResponse>>> Frames(
        Guid id,
        [FromQuery] FrameListRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _db.TrackingSessions.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        request.Normalize();

        var query = _db.SessionFrames.AsNoTracking().Where(f => f.SessionId == id);
        var total = await query.CountAsync(cancellationToken);

        query = request.Ascending
            ? query.OrderBy(f => f.FrameTimestamp)
            : query.OrderByDescending(f => f.FrameTimestamp);

        var frames = await query
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage)
            .Select(f => new FrameResponse
            {
                Id = f.Id,
                FrameTimestamp = f.FrameTimestamp,
                X = f.XCoordinate,
                Y = f.YCoordinate,
                Width = f.Width,
                Height = f.Height
            })
            .ToListAsync(cancellationToken);

        return Ok(PagedResponse<FrameResponse>.Create(frames, request.Page, request.PerPage, total));
    }

    /// <summary>Lost / reacquired events, newest first.</summary>
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<List<SessionEventResponse>>> Events(Guid id, CancellationToken cancellationToken)
    {
        if (!await _db.TrackingSessions.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        var events = await _db.SessionEvents.AsNoTracking()
            .Where(e => e.SessionId == id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new SessionEventResponse
            {
                Id = e.Id,
                EventType = e.EventType,
                OccurredAt = e.OccurredAt
            })
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    /// <summary>Movement path, X/Y over time and the timeline's red zones.</summary>
    [HttpGet("{id:guid}/analytics")]
    public async Task<ActionResult<SessionAnalyticsResponse>> Analytics(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] int maxPoints = DefaultAnalyticsPoints)
    {
        var session = await _db.TrackingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        maxPoints = Math.Clamp(maxPoints, 100, 50_000);

        var frames = await _db.SessionFrames.AsNoTracking()
            .Where(f => f.SessionId == id)
            .OrderBy(f => f.FrameTimestamp)
            .ToListAsync(cancellationToken);

        var events = await _db.SessionEvents.AsNoTracking()
            .Where(e => e.SessionId == id)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        var lostCount = events.Count(e => e.EventType == SessionEventType.Lost);

        // Charts stay responsive on long sessions: keep an even stride of frames.
        var sampled = Downsample(frames, maxPoints);

        return Ok(AnalyticsBuilder.Build(session, sampled, events, frames.Count, lostCount));
    }

    /// <summary>Deletes the session together with its frames and events.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var session = await _db.TrackingSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        _db.TrackingSessions.Remove(session);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // ---------- helpers ----------

    /// <summary>Empty strings from the device are stored as null, not as blanks.</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Accepts either raw base64 or a full <c>data:image/...;base64,</c> URL.</summary>
    private static string StripDataUrl(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var comma = trimmed.IndexOf(',');
        return comma >= 0 ? trimmed[(comma + 1)..] : trimmed;
    }

    /// <summary>Checked here so a malformed upload is a 400 rather than a Groq round trip.</summary>
    private static bool IsBase64(string value) =>
        value.Length > 0 && Convert.TryFromBase64String(value, new Span<byte>(new byte[value.Length]), out _);

    private static List<SessionFrame> Downsample(List<SessionFrame> frames, int maxPoints)
    {
        if (frames.Count <= maxPoints)
        {
            return frames;
        }

        var stride = (int)Math.Ceiling(frames.Count / (double)maxPoints);
        var sampled = new List<SessionFrame>(maxPoints + 1);

        for (var i = 0; i < frames.Count; i += stride)
        {
            sampled.Add(frames[i]);
        }

        if (sampled[^1] != frames[^1])
        {
            sampled.Add(frames[^1]);
        }

        return sampled;
    }

    private record SessionCounts(int FrameCount, int LostCount);

    private async Task<Dictionary<Guid, SessionCounts>> CountsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, SessionCounts>();
        }

        var frameCounts = await _db.SessionFrames.AsNoTracking()
            .Where(f => ids.Contains(f.SessionId))
            .GroupBy(f => f.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lostCounts = await _db.SessionEvents.AsNoTracking()
            .Where(e => ids.Contains(e.SessionId) && e.EventType == SessionEventType.Lost)
            .GroupBy(e => e.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return ids.ToDictionary(
            id => id,
            id => new SessionCounts(
                frameCounts.FirstOrDefault(c => c.SessionId == id)?.Count ?? 0,
                lostCounts.FirstOrDefault(c => c.SessionId == id)?.Count ?? 0));
    }

    private static int FrameCountOf(Dictionary<Guid, SessionCounts> counts, Guid id) =>
        counts.TryGetValue(id, out var value) ? value.FrameCount : 0;

    private static int LostCountOf(Dictionary<Guid, SessionCounts> counts, Guid id) =>
        counts.TryGetValue(id, out var value) ? value.LostCount : 0;
}
