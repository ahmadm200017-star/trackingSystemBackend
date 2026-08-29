using MdfTracker.Api.Models;

namespace MdfTracker.Api.Realtime;

/// <summary>Anything the mobile app sends over the plain tracking socket.</summary>
public class IncomingWsMessage
{
    /// <summary>"frame", "status" or "ping".</summary>
    public string? Type { get; set; }

    public DateTimeOffset? FrameTimestamp { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? Fps { get; set; }

    /// <summary>For "status" messages: "lost" or "reacquired".</summary>
    public string? State { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

// The payloads below travel to dashboards over SignalR. They carry no "type" field —
// the hub method name (Frame / Status / SessionStarted / ...) is the discriminator.

/// <summary>Frame relayed to the dashboards watching this session, and persisted asynchronously.</summary>
public class LiveFrameMessage
{
    public Guid SessionId { get; set; }

    public string SessionNumber { get; set; } = string.Empty;

    public DateTimeOffset FrameTimestamp { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double? Fps { get; set; }
}

public class LiveStatusMessage
{
    public Guid SessionId { get; set; }

    public string SessionNumber { get; set; } = string.Empty;

    public SessionEventType State { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
