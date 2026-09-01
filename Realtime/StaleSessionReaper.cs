using MdfTracker.Api.Data;
using MdfTracker.Api.Models;
using MdfTracker.Api.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MdfTracker.Api.Realtime;

/// <summary>Tunables for <see cref="StaleSessionReaper"/>, bound from the "Sessions" section.</summary>
public class SessionReaperOptions
{
    public const string SectionName = "Sessions";

    /// <summary>
    /// How long a session may sit with no connected device before the server closes it.
    ///
    /// Must comfortably exceed the app's reconnect ceiling, which is a 15-second backoff
    /// retried indefinitely: a tunnel, a lock screen or a Wi-Fi handover produces a gap of
    /// tens of seconds and the app recovers on its own. Closing a session it is still trying
    /// to resume would be worse than leaving it open a little longer, because the socket
    /// refuses a completed session and the run could not continue.
    /// </summary>
    public int AutoCloseAfterSeconds { get; set; } = 90;

    /// <summary>How often to look. Cheap: one indexed query over active sessions.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>Set false to leave abandoned sessions open, as they were before this existed.</summary>
    public bool AutoCloseEnabled { get; set; } = true;
}

/// <summary>
/// Closes sessions whose device stopped talking.
///
/// A session is opened over REST and closed over REST, which assumes the app gets to post its
/// summary. It often does not: the process is killed, the phone sleeps, the network drops, or
/// the server itself restarts mid-run. Those sessions used to sit in the database claiming to
/// be active forever, which made the live room and the session list lie.
///
/// Liveness is the socket, not the frames. A tracker that has lost its target sends status
/// events and stops sending frames, so frame recency would reap a session that is running
/// perfectly well and simply cannot see the object.
/// </summary>
public class StaleSessionReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TrackingBroadcaster _broadcaster;
    private readonly SessionReaperOptions _options;
    private readonly ILogger<StaleSessionReaper> _logger;

    public StaleSessionReaper(
        IServiceScopeFactory scopes,
        TrackingBroadcaster broadcaster,
        IOptions<SessionReaperOptions> options,
        ILogger<StaleSessionReaper> logger)
    {
        _scopes = scopes;
        _broadcaster = broadcaster;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoCloseEnabled)
        {
            _logger.LogInformation("Automatic session closing is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.SweepIntervalSeconds));
        var grace = TimeSpan.FromSeconds(Math.Max(10, _options.AutoCloseAfterSeconds));

        _logger.LogInformation(
            "Closing abandoned sessions after {Grace}s, checked every {Interval}s",
            grace.TotalSeconds, interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(grace, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                // A database outage must not kill the loop: it retries on the next tick.
                _logger.LogError(error, "Session sweep failed; retrying next tick");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(TimeSpan grace, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var active = await db.TrackingSessions
            .Where(s => s.Status == SessionStatus.Active)
            .ToListAsync(cancellationToken);

        if (active.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var closed = new List<TrackingSession>();

        foreach (var session in active)
        {
            if (_broadcaster.IsStreaming(session.Id))
            {
                continue;
            }

            // A session that never connected is measured from when it was created, which
            // covers both a crash between POST /api/sessions and the handshake, and a server
            // restart that wiped the in-memory registry.
            var silentSince = _broadcaster.LastSeen(session.Id) ?? session.StartTime;
            if (now - silentSince < grace)
            {
                continue;
            }

            // The run really ended when its data stopped, not when this sweep noticed. Using
            // "now" would inflate every abandoned session's duration by up to the grace
            // period plus however long the server was down.
            var lastFrame = await db.SessionFrames
                .Where(f => f.SessionId == session.Id)
                .MaxAsync(f => (DateTimeOffset?)f.FrameTimestamp, cancellationToken);

            var endTime = lastFrame ?? silentSince;
            if (endTime < session.StartTime)
            {
                endTime = session.StartTime;
            }

            session.EndTime = endTime;
            session.Status = SessionStatus.Completed;
            session.AutoClosed = true;

            // It did not end with the target held, so it is not a success.
            session.IsSuccessful = false;

            // The device never got to report an average, so derive one from what arrived.
            // Null when no frame carried a reading, which is honest rather than zero.
            session.AverageFps ??= await db.SessionFrames
                .Where(f => f.SessionId == session.Id && f.Fps != null)
                .AverageAsync(f => (decimal?)f.Fps, cancellationToken) is { } average
                ? Math.Round(average, 2)
                : null;

            closed.Add(session);
        }

        if (closed.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var session in closed)
        {
            _logger.LogInformation(
                "Closed {SessionNumber}: no device for over {Grace}s",
                session.SessionNumber, grace.TotalSeconds);

            _broadcaster.ForgetSession(session.Id);

            var frameCount = await db.SessionFrames
                .CountAsync(f => f.SessionId == session.Id, cancellationToken);
            var lostCount = await db.SessionEvents
                .CountAsync(e => e.SessionId == session.Id && e.EventType == SessionEventType.Lost,
                    cancellationToken);

            await _broadcaster.BroadcastSessionEndedAsync(
                SessionResponse.From(session, new SessionFrameStats(frameCount, lostCount, null, null)));
        }
    }
}
