namespace MdfTracker.Api.Responses;

/// <summary>Everything the detail view plots for one session.</summary>
public class SessionAnalyticsResponse
{
    public SessionResponse Session { get; set; } = new();

    /// <summary>Frame-by-frame points, chronological, for the movement path / X-Y-over-time charts.</summary>
    public List<AnalyticsPoint> Points { get; set; } = new();

    /// <summary>Red zones for the timeline: tracking lost, or object stationary for too long.</summary>
    public List<TrackingDrop> Drops { get; set; } = new();

    public List<SessionEventResponse> Events { get; set; } = new();
}

public class AnalyticsPoint
{
    public long OffsetMs { get; set; }

    public DateTimeOffset FrameTimestamp { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Tracker throughput at this frame, so the detail page can plot FPS over time on the
    /// same axis as the movement series. Null for frames stored before fps was persisted.
    /// </summary>
    public decimal? Fps { get; set; }

    /// <summary>Estimated real-world position of the tracked object at this frame, when available.</summary>
    public decimal? TargetLatitude { get; set; }

    public decimal? TargetLongitude { get; set; }
}

public class TrackingDrop
{
    /// <summary>"lost" (tracker reported a failure) or "stationary" (no meaningful movement).</summary>
    public string Type { get; set; } = string.Empty;

    public long StartOffsetMs { get; set; }

    public long EndOffsetMs { get; set; }

    public long DurationMs { get; set; }
}
