using System.ComponentModel.DataAnnotations;

namespace MdfTracker.Api.Requests;

/// <summary>Body of <c>POST /api/sessions/{id}/end</c> — the session summary from the device.</summary>
public class EndSessionRequest
{
    /// <summary>Defaults to server time when omitted.</summary>
    public DateTimeOffset? EndTime { get; set; }

    [Range(0, 1000, ErrorMessage = "averageFps must be between 0 and 1000.")]
    public decimal? AverageFps { get; set; }

    /// <summary>Tracked until a manual stop (true) versus lost (false). Defaults to false.</summary>
    public bool? IsSuccessful { get; set; }
}
