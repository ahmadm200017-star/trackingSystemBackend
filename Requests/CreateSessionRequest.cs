using System.ComponentModel.DataAnnotations;
using MdfTracker.Api.Models;
using MdfTracker.Api.Validation;

namespace MdfTracker.Api.Requests;

/// <summary>Body of <c>POST /api/sessions</c>, sent by the mobile app when tracking starts.</summary>
public class CreateSessionRequest : IValidatableObject
{
    [Required(ErrorMessage = "cameraType is required ('front' or 'back').")]
    public CameraType? CameraType { get; set; }

    [Required(ErrorMessage = "trackerAlgorithm is required ('csrt', 'kcf' or 'mil').")]
    public TrackerAlgorithm? TrackerAlgorithm { get; set; }

    /// <summary>Defaults to server time when omitted.</summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Whether the run used inertial sensors to stabilise the tracker. Defaults to false,
    /// which is the truth today: IMU integration is not implemented.
    /// </summary>
    public bool? ImuEnabled { get; set; }

    /// <summary>Latitude in decimal degrees. Sent only when a fix was available.</summary>
    [Range(-90, 90, ErrorMessage = "latitude must be between -90 and 90.")]
    public decimal? Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "longitude must be between -180 and 180.")]
    public decimal? Longitude { get; set; }

    /// <summary>Horizontal accuracy of the fix, in metres.</summary>
    [Range(0, 100000, ErrorMessage = "locationAccuracyMeters must be between 0 and 100000.")]
    public decimal? LocationAccuracyMeters { get; set; }

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

    /// <summary>
    /// A start time far from server time is rejected rather than stored. Every dashboard
    /// chart plots offsets from start_time, so a year-1900 or year-3000 value does not just
    /// look odd - it stretches the time axis until the real data is a single pixel.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A single coordinate is not a location. Rejecting the pair outright is better than
        // storing half of one, which would put a session on the equator or the prime meridian.
        if (Latitude.HasValue != Longitude.HasValue)
        {
            yield return new ValidationResult(
                "latitude and longitude must be sent together.",
                // Spelled as the wire names: member names given here bypass the camelCase
                // metadata provider configured in Program.cs.
                new[] { "latitude", "longitude" });
        }

        if (StartTime is { } start)
        {
            var drift = (start - DateTimeOffset.UtcNow).Duration();
            if (drift > TrackingLimits.MaxSessionTimeDrift)
            {
                yield return new ValidationResult(
                    $"startTime must be within {TrackingLimits.MaxSessionTimeDrift.TotalDays:0} days of server time.",
                    new[] { "startTime" });
            }
        }
    }
}
