namespace MdfTracker.Api.Responses;

/// <summary>Body of the 404/409 replies, so error shapes are typed like everything else.</summary>
public class MessageResponse
{
    public MessageResponse()
    {
    }

    public MessageResponse(string message) => Message = message;

    public string Message { get; set; } = string.Empty;
}

public class HealthResponse
{
    public string Status { get; set; } = "ok";

    public int DroppedFrames { get; set; }

    public int DashboardConnections { get; set; }

    public int MobileConnections { get; set; }
}

public class ApiIndexResponse
{
    public string Name { get; set; } = string.Empty;

    public List<string> Endpoints { get; set; } = new();
}
