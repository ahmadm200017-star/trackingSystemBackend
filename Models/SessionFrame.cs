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

    /// <summary>
    /// Estimated real-world position of the tracked object at this frame, computed on the
    /// device from its GPS fix, compass heading and this pixel - see the mobile app's
    /// TargetGeoLocator. An estimate under a ground-plane assumption, not a measurement;
    /// null on the large majority of frames, since it needs a settled compass reading and a
    /// camera angle pointed below the horizon.
    /// </summary>
    public decimal? TargetLatitude { get; set; }

    public decimal? TargetLongitude { get; set; }

    public TrackingSession? Session { get; set; }
}
