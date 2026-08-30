namespace MdfTracker.Api.Models;

public enum CameraType
{
    Front,
    Back
}

public enum TrackerAlgorithm
{
    Csrt,
    Kcf,

    /// <summary>
    /// The app has offered MIL since it began wrapping cv::TrackerMIL, but this enum did
    /// not list it, so every MIL session was rejected at POST /api/sessions with a 400 and
    /// went unrecorded. MIL is also the app's default algorithm, so a fresh install
    /// recorded nothing at all until the user changed the setting.
    /// </summary>
    Mil
}

public enum SessionStatus
{
    Active,
    Completed
}

/// <summary>
/// Tracking lifecycle events streamed by the mobile app. They are what lets the
/// dashboard paint the "red zones" on the session timeline.
/// </summary>
public enum SessionEventType
{
    Lost,
    Reacquired
}
