using Microsoft.AspNetCore.Mvc;

namespace MdfTracker.Api.Requests;

/// <summary>Query string of <c>GET /api/sessions/{id}/frames</c>.</summary>
public class FrameListRequest
{
    private const int MaxPerPage = 5000;

    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [FromQuery(Name = "perPage")]
    public int PerPage { get; set; } = 100;

    /// <summary>"desc" (default, newest first) or "asc" for charts that need chronology.</summary>
    [FromQuery(Name = "sort")]
    public string Sort { get; set; } = "desc";

    public bool Ascending => string.Equals(Sort, "asc", StringComparison.OrdinalIgnoreCase);

    public void Normalize()
    {
        Page = Page < 1 ? 1 : Page;
        PerPage = Math.Clamp(PerPage, 1, MaxPerPage);
    }
}
