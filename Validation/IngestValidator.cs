using MdfTracker.Api.Models;
using MdfTracker.Api.Realtime;

namespace MdfTracker.Api.Validation;

/// <summary>
/// A frame that has passed validation, with the optional wire fields already resolved to
/// the values that will be stored. Handing this back rather than the raw message means the
/// caller cannot accidentally re-read a nullable field the validator has already settled.
/// </summary>
public readonly record struct ValidatedFrame(
    DateTimeOffset Timestamp,
    int X,
    int Y,
    int Width,
    int Height,
    double? Fps,
    double? TargetLatitude,
    double? TargetLongitude);

/// <summary>
/// Validates what arrives over the tracking socket.
///
/// The REST surface gets DataAnnotations for free; the socket does not go through model
/// binding, so without this every field reaches the database unchecked. That mattered:
/// int.MaxValue coordinates and timestamps in 1900 or 3000 were being persisted, and each
/// one is enough to make the dashboard's charts unreadable, since every series is plotted
/// as an offset from the session's start time.
/// </summary>
public static class IngestValidator
{
    /// <summary>
    /// True when the frame is usable, with <paramref name="frame"/> carrying the resolved
    /// values. False sets <paramref name="problem"/> to the reason to send to the device.
    /// </summary>
    public static bool TryValidateFrame(
        IncomingWsMessage message,
        TrackingSession session,
        DateTimeOffset now,
        out ValidatedFrame frame,
        out string? problem)
    {
        frame = default;
        problem = Validate(message, session, now);
        if (problem is not null)
        {
            return false;
        }

        frame = new ValidatedFrame(
            message.FrameTimestamp ?? now,
            message.X!.Value,
            message.Y!.Value,
            message.Width ?? 0,
            message.Height ?? 0,
            message.Fps,
            message.TargetLatitude,
            message.TargetLongitude);
        return true;
    }

    /// <summary>Returns null when the frame is usable, otherwise the reason.</summary>
    private static string? Validate(IncomingWsMessage message, TrackingSession session, DateTimeOffset now)
    {
        if (message.X is null || message.Y is null)
        {
            return "frame.x and frame.y are required.";
        }

        // Bounds come from the session's own frame size when the device reported one. An
        // absolute cap is far too loose to be useful: x = -5000 sits well inside it, yet on
        // a 720-wide frame it is 5000 px off the edge and plots as a wild spike.
        var (minX, maxX) = CoordinateRange(session.ScreenWidth);
        var (minY, maxY) = CoordinateRange(session.ScreenHeight);

        if (!InRange(message.X.Value, minX, maxX) || !InRange(message.Y.Value, minY, maxY))
        {
            return $"frame.x must be between {minX} and {maxX}, frame.y between {minY} and {maxY}.";
        }

        // Width/height default to 0 when omitted, which is the "lost box" case and legal.
        var maxWidth = SizeCeiling(session.ScreenWidth);
        var maxHeight = SizeCeiling(session.ScreenHeight);

        if (message.Width is { } width && !InRange(width, TrackingLimits.MinSize, maxWidth))
        {
            return $"frame.width must be between {TrackingLimits.MinSize} and {maxWidth}.";
        }

        if (message.Height is { } height && !InRange(height, TrackingLimits.MinSize, maxHeight))
        {
            return $"frame.height must be between {TrackingLimits.MinSize} and {maxHeight}.";
        }

        if (message.Fps is { } fps &&
            (double.IsNaN(fps) || double.IsInfinity(fps) || fps < TrackingLimits.MinFps || fps > TrackingLimits.MaxFps))
        {
            return $"frame.fps must be between {TrackingLimits.MinFps} and {TrackingLimits.MaxFps}.";
        }

        if (ValidateTargetLocation(message.TargetLatitude, message.TargetLongitude) is { } locationProblem)
        {
            return locationProblem;
        }

        return ValidateTimestamp(message.FrameTimestamp, session, now, "frame.frameTimestamp");
    }

    /// <summary>
    /// Same bounds as a socket frame, for the REST ingest path. Kept here rather than
    /// duplicated in the controller so the two entry points cannot drift apart - which was
    /// the whole reason the socket went unvalidated for so long.
    /// </summary>
    public static string? ValidateRestPoint(
        DateTimeOffset timestamp,
        int x,
        int y,
        int? width,
        int? height,
        decimal? fps,
        decimal? targetLatitude,
        decimal? targetLongitude,
        TrackingSession session,
        DateTimeOffset now)
    {
        var message = new IncomingWsMessage
        {
            FrameTimestamp = timestamp,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Fps = fps.HasValue ? (double)fps.Value : null,
            TargetLatitude = targetLatitude.HasValue ? (double)targetLatitude.Value : null,
            TargetLongitude = targetLongitude.HasValue ? (double)targetLongitude.Value : null
        };

        return Validate(message, session, now);
    }

    /// <summary>Same "sent as a pair, in range" rule as the session's own recorded location.</summary>
    private static string? ValidateTargetLocation(double? latitude, double? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            return "frame.targetLatitude and frame.targetLongitude must be sent together.";
        }

        if (latitude is < -90 or > 90)
        {
            return "frame.targetLatitude must be between -90 and 90.";
        }

        if (longitude is < -180 or > 180)
        {
            return "frame.targetLongitude must be between -180 and 180.";
        }

        return null;
    }

    /// <summary>Returns null when the status event is usable, otherwise the reason.</summary>
    public static string? ValidateStatus(IncomingWsMessage message, TrackingSession session, DateTimeOffset now) =>
        ValidateTimestamp(message.OccurredAt, session, now, "status.occurredAt");

    /// <summary>
    /// A client timestamp has to sit inside the session's own lifetime, give or take clock
    /// skew. Omitted is fine - the caller substitutes server time.
    /// </summary>
    private static string? ValidateTimestamp(
        DateTimeOffset? timestamp,
        TrackingSession session,
        DateTimeOffset now,
        string field)
    {
        if (timestamp is not { } value)
        {
            return null;
        }

        var earliest = session.StartTime - TrackingLimits.ClockSkewTolerance;
        var latest = now + TrackingLimits.ClockSkewTolerance;

        if (value < earliest)
        {
            return $"{field} is before the session started.";
        }

        if (value > latest)
        {
            return $"{field} is in the future.";
        }

        return null;
    }

    /// <summary>
    /// One frame of slack on each side, so a tracker following a target off the edge is
    /// still recorded, while anything further out is rejected. Falls back to the absolute
    /// cap when the device never reported its frame size.
    /// </summary>
    private static (int Min, int Max) CoordinateRange(int frameDimension) =>
        frameDimension > 0
            ? (-frameDimension, frameDimension * 2)
            : (TrackingLimits.MinCoordinate, TrackingLimits.MaxCoordinate);

    /// <summary>A box cannot be more than twice the frame it was measured in.</summary>
    private static int SizeCeiling(int frameDimension) =>
        frameDimension > 0 ? frameDimension * 2 : TrackingLimits.MaxSize;

    private static bool InRange(int value, int min, int max) => value >= min && value <= max;
}
