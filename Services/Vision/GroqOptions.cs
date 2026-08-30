namespace MdfTracker.Api.Services.Vision;

/// <summary>
/// Bound from the "Groq" configuration section. The key is deliberately server-side only:
/// the mobile app uploads the cropped frame and never sees a Groq credential.
/// </summary>
public class GroqOptions
{
    public const string SectionName = "Groq";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.groq.com";

    /// <summary>
    /// Must be one of Groq's vision-capable models. Scout is the cheaper of the two
    /// Llama 4 checkpoints and is more than enough to name an object in a crop.
    /// </summary>
    public string Model { get; set; } = "meta-llama/llama-4-scout-17b-16e-instruct";

    /// <summary>Descriptions are one short sentence; the column is capped at 500 chars.</summary>
    public int MaxCompletionTokens { get; set; } = 80;

    public double Temperature { get; set; } = 0.2;

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
