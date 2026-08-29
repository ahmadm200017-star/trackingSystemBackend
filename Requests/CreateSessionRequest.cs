using System.ComponentModel.DataAnnotations;
using MdfTracker.Api.Models;

namespace MdfTracker.Api.Requests;

/// <summary>Body of <c>POST /api/sessions</c>, sent by the mobile app when tracking starts.</summary>
public class CreateSessionRequest
{
    [Required(ErrorMessage = "cameraType is required ('front' or 'back').")]
    public CameraType? CameraType { get; set; }

    [Required(ErrorMessage = "trackerAlgorithm is required ('csrt' or 'kcf').")]
    public TrackerAlgorithm? TrackerAlgorithm { get; set; }

    /// <summary>Defaults to server time when omitted.</summary>
    public DateTimeOffset? StartTime { get; set; }

    [Range(0, 20000, ErrorMessage = "screenWidth must be between 0 and 20000.")]
    public int? ScreenWidth { get; set; }

    [Range(0, 20000, ErrorMessage = "screenHeight must be between 0 and 20000.")]
    public int? ScreenHeight { get; set; }
}
