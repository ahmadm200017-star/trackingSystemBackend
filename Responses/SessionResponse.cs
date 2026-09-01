using MdfTracker.Api.Models;

namespace MdfTracker.Api.Responses;

/// <summary>
/// Per-session aggregates read from its frames and events.
///
/// Passed into <see cref="SessionResponse.From"/> rather than computed inside it, so a list
/// endpoint can gather them for every row in one grouped query instead of one query per row.
/// </summary>
public record SessionFrameStats(int FrameCount, int LostCount, decimal? MinFps, decimal? MaxFps)
{
    public static readonly SessionFrameStats Empty = new(0, 0, null, null);
}

public class SessionResponse
{
    public Guid Id { get; set; }

    public string SessionNumber { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public CameraType CameraType { get; set; }

    public TrackerAlgorithm TrackerAlgorithm { get; set; }

    /// <summary>Average over the whole run, reported by the device when the session ended.</summary>
    public decimal? AverageFps { get; set; }

    /// <summary>
    /// Lowest and highest per-frame throughput seen during the run, computed from the stored
    /// frames. Null for sessions recorded before per-frame FPS was persisted.
    /// </summary>
    public decimal? MinFps { get; set; }

    public decimal? MaxFps { get; set; }

    public SessionStatus Status { get; set; }

    public bool IsSuccessful { get; set; }

    /// <summary>
    /// The server closed this session because the device stopped talking, rather than the
    /// app reporting a summary. The dashboard shows it as a lost connection.
    /// </summary>
    public bool AutoClosed { get; set; }

    /// <summary>Whether inertial sensors were used to stabilise the tracker during this run.</summary>
    public bool ImuEnabled { get; set; }

    /// <summary>Groq's description of the tracked object, or null when it was never produced.</summary>
    public string? ObjectDescription { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? LocationAccuracyMeters { get; set; }

    public string? DeviceModel { get; set; }

    public string? OsVersion { get; set; }

    public string? AppVersion { get; set; }

    public decimal? ProcessingScale { get; set; }

    /// <summary>
    /// Camera image dimensions the coordinates are expressed in - the capture resolution,
    /// not the phone's display. The dashboard maps the tracked box against these.
    /// </summary>
    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public double? DurationSeconds { get; set; }

    /// <summary>Frames in which the target was tracked successfully.</summary>
    public int FrameCount { get; set; }

    /// <summary>Times the tracker reported losing the target.</summary>
    public int LostCount { get; set; }

    public static SessionResponse From(TrackingSession session, SessionFrameStats? stats = null)
    {
        var aggregate = stats ?? SessionFrameStats.Empty;

        return new SessionResponse
        {
            Id = session.Id,
            SessionNumber = session.SessionNumber,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            CameraType = session.CameraType,
            TrackerAlgorithm = session.TrackerAlgorithm,
            AverageFps = session.AverageFps,
            MinFps = aggregate.MinFps,
            MaxFps = aggregate.MaxFps,
            Status = session.Status,
            IsSuccessful = session.IsSuccessful,
            AutoClosed = session.AutoClosed,
            ImuEnabled = session.ImuEnabled,
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
            FrameCount = aggregate.FrameCount,
            LostCount = aggregate.LostCount
        };
    }
}
