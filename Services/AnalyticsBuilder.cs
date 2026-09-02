using MdfTracker.Api.Responses;
using MdfTracker.Api.Models;

namespace MdfTracker.Api.Services;

/// <summary>
/// Turns raw frames + lifecycle events into what the dashboard detail view plots:
/// the movement path, the X/Y-over-time series and the timeline's red zones.
/// </summary>
public static class AnalyticsBuilder
{
    /// <summary>Movement below this many pixels counts as "not moving".</summary>
    private const int StationaryPixelThreshold = 4;

    /// <summary>A stationary stretch is only worth flagging past this duration.</summary>
    private static readonly TimeSpan StationaryMinDuration = TimeSpan.FromSeconds(2);

    public static SessionAnalyticsResponse Build(
        TrackingSession session,
        List<SessionFrame> framesAscending,
        List<SessionEvent> eventsAscending,
        SessionFrameStats stats)
    {
        var start = session.StartTime;

        var points = framesAscending
            .Select(f => new AnalyticsPoint
            {
                OffsetMs = (long)(f.FrameTimestamp - start).TotalMilliseconds,
                FrameTimestamp = f.FrameTimestamp,
                X = f.XCoordinate,
                Y = f.YCoordinate,
                Width = f.Width,
                Height = f.Height,
                Fps = f.Fps,
                TargetLatitude = f.TargetLatitude,
                TargetLongitude = f.TargetLongitude
            })
            .ToList();

        var sessionEnd = session.EndTime ?? framesAscending.LastOrDefault()?.FrameTimestamp ?? start;

        var drops = new List<TrackingDrop>();
        drops.AddRange(BuildLostZones(eventsAscending, start, sessionEnd));
        drops.AddRange(BuildStationaryZones(points));
        drops = drops.OrderBy(d => d.StartOffsetMs).ToList();

        return new SessionAnalyticsResponse
        {
            Session = SessionResponse.From(session, stats),
            Points = points,
            Drops = drops,
            Events = eventsAscending
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => new SessionEventResponse
                {
                    Id = e.Id,
                    EventType = e.EventType,
                    OccurredAt = e.OccurredAt
                })
                .ToList()
        };
    }

    /// <summary>Every 'lost' pairs with the next 'reacquired', or runs to the end of the session.</summary>
    private static IEnumerable<TrackingDrop> BuildLostZones(
        List<SessionEvent> eventsAscending,
        DateTimeOffset start,
        DateTimeOffset sessionEnd)
    {
        DateTimeOffset? lostAt = null;

        foreach (var sessionEvent in eventsAscending)
        {
            if (sessionEvent.EventType == SessionEventType.Lost)
            {
                lostAt ??= sessionEvent.OccurredAt;
            }
            else if (lostAt.HasValue)
            {
                yield return Drop("lost", start, lostAt.Value, sessionEvent.OccurredAt);
                lostAt = null;
            }
        }

        if (lostAt.HasValue)
        {
            yield return Drop("lost", start, lostAt.Value, sessionEnd);
        }
    }

    /// <summary>Stretches where the bounding box barely moved — a stalled or stuck tracker.</summary>
    private static IEnumerable<TrackingDrop> BuildStationaryZones(List<AnalyticsPoint> points)
    {
        if (points.Count < 2)
        {
            yield break;
        }

        var anchor = points[0];
        var runStart = points[0];

        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            var moved = Math.Abs(point.X - anchor.X) > StationaryPixelThreshold
                || Math.Abs(point.Y - anchor.Y) > StationaryPixelThreshold;

            if (moved)
            {
                var previous = points[i - 1];
                if (previous.OffsetMs - runStart.OffsetMs >= StationaryMinDuration.TotalMilliseconds)
                {
                    yield return Drop("stationary", runStart.OffsetMs, previous.OffsetMs);
                }

                anchor = point;
                runStart = point;
            }
        }

        var last = points[^1];
        if (last.OffsetMs - runStart.OffsetMs >= StationaryMinDuration.TotalMilliseconds)
        {
            yield return Drop("stationary", runStart.OffsetMs, last.OffsetMs);
        }
    }

    private static TrackingDrop Drop(string type, DateTimeOffset start, DateTimeOffset from, DateTimeOffset to) =>
        Drop(type, (long)(from - start).TotalMilliseconds, (long)(to - start).TotalMilliseconds);

    private static TrackingDrop Drop(string type, long startOffsetMs, long endOffsetMs) => new()
    {
        Type = type,
        StartOffsetMs = startOffsetMs,
        EndOffsetMs = endOffsetMs,
        DurationMs = Math.Max(0, endOffsetMs - startOffsetMs)
    };
}
