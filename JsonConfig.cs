using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdfTracker.Api;

/// <summary>
/// One JSON contract for the whole app: camelCase properties, lowercase string enums
/// (so 'csrt' / 'back' / 'lost' travel exactly as the API contract documents them).
/// </summary>
public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = Build();

    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions();
        Apply(options);
        return options;
    }
}
