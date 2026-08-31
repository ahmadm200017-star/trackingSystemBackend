using MdfTracker.Api.Models;

namespace MdfTracker.Api.Responses;

public class SessionResponse
{
    public Guid Id { get; set; }

    public string SessionNumber { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public CameraType CameraType { get; set; }

    public TrackerAlgorithm TrackerAlgorithm { get; set; }

    public decimal? AverageFps { get; set; }

    public SessionStatus Status { get; set; }

    public bool IsSuccessful { get; set; }

    /// <summary>Groq's description of the tracked object, or null when it was never produced.</summary>
    public string? ObjectDescription { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? LocationAccuracyMeters { get; set; }

    public string? DeviceModel { get; set; }

    public string? OsVersion { get; set; }

    public string? AppVersion { get; set; }

    public decimal? ProcessingScale { get; set; }

    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public double? DurationSeconds { get; set; }

    public int FrameCount { get; set; }

    public int LostCount { get; set; }

    public static SessionResponse From(TrackingSession session, int frameCount = 0, int lostCount = 0) => new()
    {
        Id = session.Id,
        SessionNumber = session.SessionNumber,
        StartTime = session.StartTime,
        EndTime = session.EndTime,
        CameraType = session.CameraType,
        TrackerAlgorithm = session.TrackerAlgorithm,
        AverageFps = session.AverageFps,
        Status = session.Status,
        IsSuccessful = session.IsSuccessful,
        ObjectDescription = session.ObjectDescription,
        Latitude = session.Latitude,
        Longitude = session.Longitude,
        LocationAccuracyMeters = session.LocationAccuracyMeters,
        DeviceModel = session.DeviceModel,
        OsVersion = session.OsVersion,
        AppVersion = session.AppVersion,
        ProcessingScale = session.ProcessingScale,
        ScreenWidth = session.ScreenWidth,
        ScreenHeight = session.ScreenHeight,
        DurationSeconds = session.EndTime.HasValue
            ? Math.Round((session.EndTime.Value - session.StartTime).TotalSeconds, 2)
            : null,
        FrameCount = frameCount,
        LostCount = lostCount
    };
}
