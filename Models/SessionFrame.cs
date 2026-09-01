namespace MdfTracker.Api.Models;

public class SessionFrame
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public DateTimeOffset FrameTimestamp { get; set; }

    public int XCoordinate { get; set; }

    public int YCoordinate { get; set; }

    /// <summary>Bounding box size pushed by the tracker, kept so the detail view can replay the box.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Tracker throughput at the moment this frame was processed. The device already sends
    /// it with every frame and the dashboard shows it live; persisting it is what makes a
    /// session's highest and lowest FPS answerable after the fact, and lets the detail page
    /// plot throughput over time rather than only the final average.
    /// </summary>
    public decimal? Fps { get; set; }

    public TrackingSession? Session { get; set; }
}
