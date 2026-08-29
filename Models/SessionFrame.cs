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

    public TrackingSession? Session { get; set; }
}
