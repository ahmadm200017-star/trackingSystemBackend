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

    /// <summary>
    /// One-sentence description of the tracked object, produced by a Groq vision model from
    /// the first frame the user seeded the tracker with. Null until the device uploads that
    /// crop, and stays null when Groq is unconfigured or the call failed.
    /// </summary>
    public string? ObjectDescription { get; set; }

    /// <summary>Handset the run came from, e.g. "Google Pixel 7". Null for older sessions.</summary>
    public string? DeviceModel { get; set; }

    /// <summary>Platform and version, e.g. "Android 14 (SDK 34)".</summary>
    public string? OsVersion { get; set; }

    /// <summary>Build of the tracker app, e.g. "1.0.0+1", so a regression can be pinned to a release.</summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Factor frames were downscaled by before reaching the tracker (0.25-1.0). Recorded
    /// because it is the single biggest lever on average FPS - comparing FPS across
    /// sessions is meaningless without it.
    /// </summary>
    public decimal? ProcessingScale { get; set; }

    /// <summary>Mobile screen resolution, so the dashboard live grid can map coordinates 1:1.</summary>
    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public List<SessionFrame> Frames { get; set; } = new();

    public List<SessionEvent> Events { get; set; } = new();
}
