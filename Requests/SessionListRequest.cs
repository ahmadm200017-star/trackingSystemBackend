using Microsoft.AspNetCore.Mvc;
using MdfTracker.Api.Models;

namespace MdfTracker.Api.Requests;

/// <summary>
/// Query string of <c>GET /api/sessions</c>. Results are always newest first.
///
/// Each property pins its wire name so a binding failure is reported under the camelCase
/// key the client sent, not the C# property name.
/// </summary>
public class SessionListRequest
{
    private const int MaxPerPage = 100;

    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [FromQuery(Name = "perPage")]
    public int PerPage { get; set; } = 20;

    [FromQuery(Name = "status")]
    public SessionStatus? Status { get; set; }

    [FromQuery(Name = "trackerAlgorithm")]
    public TrackerAlgorithm? TrackerAlgorithm { get; set; }

    [FromQuery(Name = "cameraType")]
    public CameraType? CameraType { get; set; }

    [FromQuery(Name = "isSuccessful")]
    public bool? IsSuccessful { get; set; }

    /// <summary>Matches part of a session number.</summary>
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    /// <summary>Clamps paging into a sane range instead of rejecting the request over it.</summary>
    public void Normalize()
    {
        Page = Page < 1 ? 1 : Page;
        PerPage = Math.Clamp(PerPage, 1, MaxPerPage);
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }
}
