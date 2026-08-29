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
}

public class SessionEventResponse
{
    public Guid Id { get; set; }

    public SessionEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
