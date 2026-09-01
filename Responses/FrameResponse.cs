using MdfTracker.Api.Models;

namespace MdfTracker.Api.Responses;

public class FrameResponse
{
    public Guid Id { get; set; }

    public DateTimeOffset FrameTimestamp { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Tracker throughput at this frame; null for frames stored before it was persisted.</summary>
    public decimal? Fps { get; set; }
}

public class SessionEventResponse
{
    public Guid Id { get; set; }

    public SessionEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
