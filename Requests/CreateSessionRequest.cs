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

    /// <summary>Handset model, e.g. "Google Pixel 7".</summary>
    [StringLength(120, ErrorMessage = "deviceModel must be 120 characters or fewer.")]
    public string? DeviceModel { get; set; }

    /// <summary>Platform and version, e.g. "Android 14 (SDK 34)".</summary>
    [StringLength(60, ErrorMessage = "osVersion must be 60 characters or fewer.")]
    public string? OsVersion { get; set; }

    [StringLength(30, ErrorMessage = "appVersion must be 30 characters or fewer.")]
    public string? AppVersion { get; set; }

    /// <summary>Frame downscale factor used by the tracker, 0.25-1.0.</summary>
    [Range(0.05, 1.0, ErrorMessage = "processingScale must be between 0.05 and 1.")]
    public decimal? ProcessingScale { get; set; }

    [Range(0, 20000, ErrorMessage = "screenWidth must be between 0 and 20000.")]
    public int? ScreenWidth { get; set; }

    [Range(0, 20000, ErrorMessage = "screenHeight must be between 0 and 20000.")]
    public int? ScreenHeight { get; set; }
}
