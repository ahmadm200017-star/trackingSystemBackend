namespace MdfTracker.Api.Responses;

/// <summary>
/// Outcome of <c>POST /api/sessions/{id}/data</c>.
///
/// Reports accepted versus duplicate separately because the two mean different things to a
/// client: accepted is progress, duplicate means the point was already stored and the retry
/// was harmless. A client that cannot tell them apart either double-counts or panics.
/// </summary>
public class SessionDataIngestResponse
{
    public Guid SessionId { get; set; }

    /// <summary>Points written for the first time.</summary>
    public int Accepted { get; set; }

    /// <summary>Points that exactly matched something already stored, and were skipped.</summary>
    public int Duplicates { get; set; }

    /// <summary>Lost / reacquired events recorded from the points' state field.</summary>
    public int Events { get; set; }

    public int TotalFrames { get; set; }
}
