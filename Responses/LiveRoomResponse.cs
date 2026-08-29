namespace MdfTracker.Api.Responses;

/// <summary>Who is connected to the live tracking room right now.</summary>
public class LiveRoomResponse
{
    public int DashboardConnections { get; set; }

    public int MobileConnections { get; set; }

    /// <summary>Frames discarded because the writer could not keep up. Should stay 0.</summary>
    public int DroppedFrames { get; set; }

    /// <summary>Sessions currently streaming, newest first.</summary>
    public List<LiveStreamItem> Streams { get; set; } = new();
}

public class LiveStreamItem
{
    public Guid SessionId { get; set; }

    public string SessionNumber { get; set; } = string.Empty;

    public double? LastFps { get; set; }
}
