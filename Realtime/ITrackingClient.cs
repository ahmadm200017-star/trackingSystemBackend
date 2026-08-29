using MdfTracker.Api.Responses;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// Everything the server can call on a connected dashboard. Strongly typed so a renamed
/// method breaks the build here instead of silently going unheard in the browser.
/// </summary>
public interface ITrackingClient
{
    /// <summary>Sent once, on connect: the sessions currently streaming, newest first.</summary>
    Task ActiveSessions(List<SessionResponse> sessions);

    /// <summary>Only to the group for that session — subscribe first, see <see cref="TrackingHub"/>.</summary>
    Task Frame(LiveFrameMessage frame);

    /// <summary>Lost / reacquired, group-scoped like <see cref="Frame"/>.</summary>
    Task Status(LiveStatusMessage status);

    /// <summary>Lifecycle goes to every dashboard, subscribed or not, so session lists stay current.</summary>
    Task SessionStarted(SessionResponse session);

    Task SessionEnded(SessionResponse session);
}
