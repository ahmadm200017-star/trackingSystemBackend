namespace MdfTracker.Api.Models;

public class SessionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public SessionEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public TrackingSession? Session { get; set; }
}
