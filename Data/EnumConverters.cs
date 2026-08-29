using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Models = MdfTracker.Api.Models;

namespace MdfTracker.Api.Data;

/// <summary>
/// Enums are stored as the lowercase strings the schema documents ('front', 'csrt',
/// 'active', 'lost') rather than ints, so the tables stay readable in a SQL client.
/// </summary>
public static class EnumConverters
{
    public static readonly ValueConverter<Models.CameraType, string> CameraType =
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<Models.CameraType>(v, true));

    public static readonly ValueConverter<Models.TrackerAlgorithm, string> TrackerAlgorithm =
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<Models.TrackerAlgorithm>(v, true));

    public static readonly ValueConverter<Models.SessionStatus, string> SessionStatus =
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<Models.SessionStatus>(v, true));

    public static readonly ValueConverter<Models.SessionEventType, string> SessionEventType =
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<Models.SessionEventType>(v, true));
}
