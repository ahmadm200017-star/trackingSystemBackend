using MdfTracker.Api.Data;
using MdfTracker.Api.Models;
using MdfTracker.Api.Realtime;
using MdfTracker.Api.Requests;
using MdfTracker.Api.Responses;
using MdfTracker.Api.Services;
using MdfTracker.Api.Services.Vision;
using MdfTracker.Api.Validation;
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
            ImuEnabled = request.ImuEnabled ?? false,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LocationAccuracyMeters = request.LocationAccuracyMeters.HasValue
                ? Math.Round(request.LocationAccuracyMeters.Value, 2)
                : null,
            DeviceModel = InputSanitizer.Clean(request.DeviceModel, 120),
            OsVersion = InputSanitizer.Clean(request.OsVersion, 60),
            AppVersion = InputSanitizer.Clean(request.AppVersion, 30),
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
    [HttpPost("{id:guid}/finish")]
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

        var endTime = request.EndTime ?? DateTimeOffset.UtcNow;

        // Checked here rather than on the DTO because it needs the stored start time.
        // Without it a client could close a session "before" it opened, and the API would
        // then report a negative durationSeconds to every dashboard.
        if (endTime < session.StartTime)
        {
            return BadRequest(new MessageResponse("endTime cannot be before the session start time."));
        }

        if (endTime - session.StartTime > TrackingLimits.MaxSessionDuration)
        {
            return BadRequest(new MessageResponse(
                $"endTime implies a session longer than {TrackingLimits.MaxSessionDuration.TotalHours:0} hours."));
        }

        if (endTime > DateTimeOffset.UtcNow + TrackingLimits.ClockSkewTolerance)
        {
            return BadRequest(new MessageResponse("endTime cannot be in the future."));
        }

        session.EndTime = endTime;
        session.AverageFps = request.AverageFps.HasValue ? Math.Round(request.AverageFps.Value, 2) : null;
        session.IsSuccessful = request.IsSuccessful ?? false;
        session.Status = SessionStatus.Completed;

        await _db.SaveChangesAsync(cancellationToken);

        var stats = await StatsAsync(new[] { session.Id }, cancellationToken);
        var response = SessionResponse.From(session, StatsOf(stats, session.Id));

        // Forget rather than just disconnect: the session is closed, so the reaper has no
        // further interest in it and its last-seen entry would otherwise linger.
        _broadcaster.ForgetSession(session.Id);
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
        if (!TryDecodeBase64(image, out var bytes))
        {
            return BadRequest(new MessageResponse("imageBase64 is not valid base64."));
        }

        if (bytes.Length > TrackingLimits.MaxImageBytes)
        {
            return BadRequest(new MessageResponse(
                $"The decoded image must be under {TrackingLimits.MaxImageBytes / (1024 * 1024)} MB."));
        }

        var kind = ImagePayload.Identify(bytes);
        if (kind == ImagePayload.Kind.Unknown)
        {
            return BadRequest(new MessageResponse(
                "imageBase64 is not a JPEG, PNG or WebP image."));
        }

        // The real format wins over the declared one, so a mislabelled upload still works
        // rather than being handed to Groq under a mime type that contradicts its bytes.
        var mimeType = ImagePayload.MimeTypeOf(kind);

        string description;
        try
        {
            description = await _vision.DescribeAsync(image, mimeType, cancellationToken);
        }
        catch (GroqVisionException error)
        {
            _logger.LogWarning(error, "Object description failed for session {SessionId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new MessageResponse(error.Message));
        }

        session.ObjectDescription = description;
        await _db.SaveChangesAsync(cancellationToken);

        var stats = await StatsAsync(new[] { id }, cancellationToken);
        var response = SessionResponse.From(session, StatsOf(stats, id));

        await _broadcaster.BroadcastSessionDescribedAsync(response);

        return Ok(response);
    }

    /// <summary>
    /// REST ingest for tracking data, accepting one point or a batch. The app streams over
    /// the socket instead - far cheaper per point - but this exists so the ingest contract
    /// can be driven with nothing but curl.
    ///
    /// Re-sending the same data is safe: a point identical to one already stored for this
    /// session is counted as a duplicate and skipped rather than written twice, so a client
    /// that retries after a timeout cannot corrupt the series.
    /// </summary>
    [HttpPost("{id:guid}/data")]
    public async Task<ActionResult<SessionDataIngestResponse>> AddData(
        Guid id,
        SessionDataRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _db.TrackingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (session is null)
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        if (session.Status == SessionStatus.Completed)
        {
            return Conflict(new MessageResponse("Session is already completed; it accepts no more data."));
        }

        var now = DateTimeOffset.UtcNow;
        var points = request.AsPoints();

        // Existing points for this session, so a retry of the same payload is recognised.
        // Compared on the whole tuple rather than the timestamp alone: at 30 fps two frames
        // can share a millisecond, and rejecting the second would lose real data.
        var existing = (await _db.SessionFrames.AsNoTracking()
                .Where(f => f.SessionId == id)
                .Select(f => new { f.FrameTimestamp, f.XCoordinate, f.YCoordinate, f.Width, f.Height })
                .ToListAsync(cancellationToken))
            .Select(f => (f.FrameTimestamp, f.XCoordinate, f.YCoordinate, f.Width, f.Height))
            .ToHashSet();

        var frames = new List<SessionFrame>();
        var events = new List<SessionEvent>();
        var duplicates = 0;

        foreach (var point in points)
        {
            var timestamp = point.Timestamp ?? now;

            if (IngestValidator.ValidateRestPoint(
                    timestamp, point.X!.Value, point.Y!.Value, point.Width, point.Height,
                    point.Fps, session, now) is { } problem)
            {
                return BadRequest(new MessageResponse(problem));
            }

            var width = point.Width ?? 0;
            var height = point.Height ?? 0;
            var key = (timestamp, point.X!.Value, point.Y!.Value, width, height);

            // Also catches duplicates inside a single batch, not just against the database.
            if (!existing.Add(key))
            {
                duplicates++;
                continue;
            }

            frames.Add(new SessionFrame
            {
                SessionId = id,
                FrameTimestamp = timestamp,
                XCoordinate = point.X!.Value,
                YCoordinate = point.Y!.Value,
                Width = width,
                Height = height,
                Fps = point.Fps.HasValue ? Math.Round(point.Fps.Value, 2) : null
            });

            if (point.State is { Length: > 0 } state &&
                Enum.TryParse<SessionEventType>(state, ignoreCase: true, out var eventType))
            {
                events.Add(new SessionEvent
                {
                    SessionId = id,
                    EventType = eventType,
                    OccurredAt = timestamp
                });
            }
        }

        if (frames.Count > 0)
        {
            _db.SessionFrames.AddRange(frames);
        }

        if (events.Count > 0)
        {
            _db.SessionEvents.AddRange(events);
        }

        if (frames.Count > 0 || events.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Relayed to dashboards so REST ingest shows up live exactly like the socket does.
        foreach (var frame in frames)
        {
            _broadcaster.ReportFps(id, frame.Fps.HasValue ? (double)frame.Fps.Value : null);
            await _broadcaster.BroadcastFrameAsync(new LiveFrameMessage
            {
                SessionId = id,
                SessionNumber = session.SessionNumber,
                FrameTimestamp = frame.FrameTimestamp,
                X = frame.XCoordinate,
                Y = frame.YCoordinate,
                Width = frame.Width,
                Height = frame.Height,
                Fps = frame.Fps.HasValue ? (double)frame.Fps.Value : null
            });
        }

        foreach (var sessionEvent in events)
        {
            await _broadcaster.BroadcastStatusAsync(new LiveStatusMessage
            {
                SessionId = id,
                SessionNumber = session.SessionNumber,
                State = sessionEvent.EventType,
                OccurredAt = sessionEvent.OccurredAt
            });
        }

        return Ok(new SessionDataIngestResponse
        {
            SessionId = id,
            Accepted = frames.Count,
            Duplicates = duplicates,
            Events = events.Count,
            TotalFrames = await _db.SessionFrames.CountAsync(f => f.SessionId == id, cancellationToken)
        });
    }

    /// <summary>
    /// Target position and FPS ordered by time. Same data as <c>/frames</c> but always
    /// chronological and without paging, which is the shape a chart wants.
    /// </summary>
    [HttpGet("{id:guid}/data")]
    public async Task<ActionResult<List<FrameResponse>>> Data(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 50_000)
    {
        if (!await _db.TrackingSessions.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return NotFound(new MessageResponse("Session not found."));
        }

        limit = Math.Clamp(limit, 1, 50_000);

        var data = await _db.SessionFrames.AsNoTracking()
            .Where(f => f.SessionId == id)
            .OrderBy(f => f.FrameTimestamp)
            .Take(limit)
            .Select(f => new FrameResponse
            {
                Id = f.Id,
                FrameTimestamp = f.FrameTimestamp,
                X = f.XCoordinate,
                Y = f.YCoordinate,
                Width = f.Width,
                Height = f.Height,
                Fps = f.Fps
            })
            .ToListAsync(cancellationToken);

        return Ok(data);
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

        var stats = await StatsAsync(sessions.Select(s => s.Id).ToList(), cancellationToken);

        var data = sessions
            .Select(s => SessionResponse.From(s, StatsOf(stats, s.Id)))
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

        var stats = await StatsAsync(new[] { id }, cancellationToken);
        return Ok(SessionResponse.From(session, StatsOf(stats, id)));
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
                Height = f.Height,
                Fps = f.Fps
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

        // Charts stay responsive on long sessions: keep an even stride of frames. The FPS
        // range is taken from every frame, not the sampled subset, so the reported minimum
        // and maximum are the real ones rather than whichever the stride happened to keep.
        var reported = frames.Where(f => f.Fps.HasValue).Select(f => f.Fps!.Value).ToList();
        var stats = new SessionFrameStats(
            frames.Count,
            lostCount,
            reported.Count > 0 ? reported.Min() : null,
            reported.Count > 0 ? reported.Max() : null);

        var sampled = Downsample(frames, maxPoints);

        return Ok(AnalyticsBuilder.Build(session, sampled, events, stats));
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

    /// <summary>
    /// Decodes once, so the same pass rejects malformed base64 and yields the bytes the
    /// signature check needs. Keeps a bad upload to a 400 instead of a Groq round trip.
    /// </summary>
    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (value.Length == 0)
        {
            return false;
        }

        var buffer = new byte[(value.Length / 4 + 1) * 3];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written == 0)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

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

    /// <summary>
    /// Frame count, lost count and the FPS range for each of <paramref name="ids"/>, in two
    /// grouped queries regardless of how many sessions are asked for.
    /// </summary>
    private async Task<Dictionary<Guid, SessionFrameStats>> StatsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, SessionFrameStats>();
        }

        var frameStats = await _db.SessionFrames.AsNoTracking()
            .Where(f => ids.Contains(f.SessionId))
            .GroupBy(f => f.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                Count = g.Count(),
                // Frames stored before fps was persisted are null, and Min/Max over a
                // nullable column ignores them, so a partially-populated session still
                // reports the range of what it does have.
                MinFps = g.Min(f => f.Fps),
                MaxFps = g.Max(f => f.Fps)
            })
            .ToListAsync(cancellationToken);

        var lostCounts = await _db.SessionEvents.AsNoTracking()
            .Where(e => ids.Contains(e.SessionId) && e.EventType == SessionEventType.Lost)
            .GroupBy(e => e.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var frames = frameStats.FirstOrDefault(c => c.SessionId == id);
                var lost = lostCounts.FirstOrDefault(c => c.SessionId == id)?.Count ?? 0;
                return new SessionFrameStats(frames?.Count ?? 0, lost, frames?.MinFps, frames?.MaxFps);
            });
    }

    private static SessionFrameStats StatsOf(Dictionary<Guid, SessionFrameStats> stats, Guid id) =>
        stats.TryGetValue(id, out var value) ? value : SessionFrameStats.Empty;
}
