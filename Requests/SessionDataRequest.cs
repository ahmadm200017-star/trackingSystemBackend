using System.ComponentModel.DataAnnotations;
using MdfTracker.Api.Validation;

namespace MdfTracker.Api.Requests;

/// <summary>
/// One tracking data point for <c>POST /api/sessions/{id}/data</c>.
///
/// This is the REST equivalent of a frame pushed over the tracking socket. The socket is
/// what the app actually uses - it is far cheaper per point - but the REST path exists so
/// the ingest contract can be exercised with nothing but curl, and so a client that cannot
/// hold a socket open still has a way in.
/// </summary>
public class SessionDataPoint
{
    /// <summary>Defaults to server time when omitted.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    [Required(ErrorMessage = "x is required.")]
    [Range(TrackingLimits.MinCoordinate, TrackingLimits.MaxCoordinate,
        ErrorMessage = "x is outside the accepted range.")]
    public int? X { get; set; }

    [Required(ErrorMessage = "y is required.")]
    [Range(TrackingLimits.MinCoordinate, TrackingLimits.MaxCoordinate,
        ErrorMessage = "y is outside the accepted range.")]
    public int? Y { get; set; }

    [Range(TrackingLimits.MinSize, TrackingLimits.MaxSize, ErrorMessage = "width is outside the accepted range.")]
    public int? Width { get; set; }

    [Range(TrackingLimits.MinSize, TrackingLimits.MaxSize, ErrorMessage = "height is outside the accepted range.")]
    public int? Height { get; set; }

    [Range(TrackingLimits.MinFps, TrackingLimits.MaxFps, ErrorMessage = "fps must be between 0 and 1000.")]
    public decimal? Fps { get; set; }

    /// <summary>Estimated real-world position of the tracked object; sent only as a pair.</summary>
    [Range(-90, 90, ErrorMessage = "targetLatitude must be between -90 and 90.")]
    public decimal? TargetLatitude { get; set; }

    [Range(-180, 180, ErrorMessage = "targetLongitude must be between -180 and 180.")]
    public decimal? TargetLongitude { get; set; }

    /// <summary>
    /// Tracking state at this point: "tracking", "lost" or "reacquired". A state of lost or
    /// reacquired also records a lifecycle event, which is what the dashboard timeline draws
    /// its red zones from. Omitted or "tracking" records only the frame.
    /// </summary>
    public string? State { get; set; }
}

/// <summary>
/// Body of <c>POST /api/sessions/{id}/data</c>. Accepts a single point or a batch, so a
/// client can trade round trips against latency without a second endpoint.
/// </summary>
public class SessionDataRequest : IValidatableObject
{
    /// <summary>A batch of points. Mutually exclusive with the single-point fields.</summary>
    public List<SessionDataPoint>? Points { get; set; }

    // ---- single-point form, so a caller can post one point without wrapping it ----

    public DateTimeOffset? Timestamp { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public decimal? Fps { get; set; }

    public decimal? TargetLatitude { get; set; }

    public decimal? TargetLongitude { get; set; }

    public string? State { get; set; }

    /// <summary>
    /// Normalises either shape into one list. The single-point form is only read when no
    /// batch was supplied, so a request carrying both does not silently lose half its data.
    /// </summary>
    public List<SessionDataPoint> AsPoints()
    {
        if (Points is { Count: > 0 })
        {
            return Points;
        }

        return new List<SessionDataPoint>
        {
            new()
            {
                Timestamp = Timestamp,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                Fps = Fps,
                TargetLatitude = TargetLatitude,
                TargetLongitude = TargetLongitude,
                State = State
            }
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var points = AsPoints();

        if (points.Count == 0)
        {
            yield return new ValidationResult("Send either points[] or a single x/y point.", new[] { "points" });
            yield break;
        }

        if (points.Count > MaxBatchSize)
        {
            yield return new ValidationResult(
                $"points[] holds at most {MaxBatchSize} entries per request.",
                new[] { "points" });
        }

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var prefix = Points is { Count: > 0 } ? $"points[{i}]." : string.Empty;

            if (point.X is null || point.Y is null)
            {
                yield return new ValidationResult($"{prefix}x and {prefix}y are required.",
                    new[] { $"{prefix}x", $"{prefix}y" });
                continue;
            }

            if (point.State is { Length: > 0 } state && !IsKnownState(state))
            {
                yield return new ValidationResult(
                    $"{prefix}state must be 'tracking', 'lost' or 'reacquired'.",
                    new[] { $"{prefix}state" });
            }

            if (point.TargetLatitude.HasValue != point.TargetLongitude.HasValue)
            {
                yield return new ValidationResult(
                    $"{prefix}targetLatitude and {prefix}targetLongitude must be sent together.",
                    new[] { $"{prefix}targetLatitude", $"{prefix}targetLongitude" });
            }
        }
    }

    public const int MaxBatchSize = 1000;

    public static bool IsKnownState(string state) =>
        state.Equals("tracking", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("lost", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("reacquired", StringComparison.OrdinalIgnoreCase);
}
