namespace MdfTracker.Api.Models;

public enum CameraType
{
    Front,
    Back
}

public enum TrackerAlgorithm
{
    Csrt,
    Kcf
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
