namespace MdfTracker.Api.Models;

public class TrackingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SessionNumber { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public CameraType CameraType { get; set; }

    public TrackerAlgorithm TrackerAlgorithm { get; set; }

    public decimal? AverageFps { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Active;

    public bool IsSuccessful { get; set; }

    /// <summary>Mobile screen resolution, so the dashboard live grid can map coordinates 1:1.</summary>
    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public List<SessionFrame> Frames { get; set; } = new();

    public List<SessionEvent> Events { get; set; } = new();
}
