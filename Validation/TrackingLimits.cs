namespace MdfTracker.Api.Validation;

/// <summary>
/// One place for the bounds every ingest path enforces.
///
/// The REST DTOs and the tracking socket both validate against these, so a limit cannot
/// drift between the two. They are deliberately generous - the goal is to keep obvious
/// garbage out of the database, not to second-guess a tracker that legitimately reports a
/// box partly off the edge of the frame.
/// </summary>
public static class TrackingLimits
{
    /// <summary>
    /// Bounding-box origin, in camera-image pixels. A tracked box may sit partly outside
    /// the frame, so negatives are legal; this only rules out nonsense.
    /// </summary>
    public const int MinCoordinate = -100_000;

    public const int MaxCoordinate = 100_000;

    /// <summary>Box size. Zero is allowed: a lost tracker reports an empty box.</summary>
    public const int MinSize = 0;

    public const int MaxSize = 100_000;

    public const double MinFps = 0;

    public const double MaxFps = 1_000;

    public const double MinProcessingScale = 0.05;

    public const double MaxProcessingScale = 1.0;

    public const int MaxScreenDimension = 20_000;

    /// <summary>
    /// Device clocks drift, so a frame stamped slightly before its session started or
    /// slightly in the future is accepted rather than dropped.
    /// </summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far from server time a client-supplied session time may sit. Wide enough for a
    /// device with a badly wrong timezone, narrow enough to keep 1900 and 3000 out - those
    /// destroy the dashboard's time axis, because every chart plots offsets from start_time.
    /// </summary>
    public static readonly TimeSpan MaxSessionTimeDrift = TimeSpan.FromDays(2);

    /// <summary>A session cannot last longer than this, which bounds end_time.</summary>
    public static readonly TimeSpan MaxSessionDuration = TimeSpan.FromDays(1);

    /// <summary>
    /// Paging: <c>(page - 1) * perPage</c> is computed as an int by EF's Skip, so an
    /// unclamped page near int.MaxValue overflows and the request fails with a 500.
    /// </summary>
    public const int MaxPage = 100_000;

    /// <summary>Decoded size cap for an uploaded description crop. Groq's own limit is 4 MB of base64.</summary>
    public const int MaxImageBytes = 3 * 1024 * 1024;
}
