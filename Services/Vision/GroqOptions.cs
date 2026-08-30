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
    /// Must be one of Groq's vision-capable models. As of this writing those are
    /// qwen/qwen3.8-27b and qwen/qwen3.6-27b; the Llama 4 vision checkpoints this
    /// originally used were retired from Groq. Check the live list before changing it:
    /// a model Groq no longer serves fails the call with a 404, not a fallback.
    /// </summary>
    public string Model { get; set; } = "qwen/qwen3.8-27b";

    /// <summary>Descriptions are one short sentence; the column is capped at 500 chars.</summary>
    public int MaxCompletionTokens { get; set; } = 80;

    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Groq has been observed taking several seconds even to reject a bad key, so this is
    /// deliberately generous. It must stay below the mobile client's own deadline for
    /// POST /description, otherwise the device gives up before the server can answer.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Sent as <c>reasoning_effort</c>. The Qwen 3 vision models reason by default, which
    /// costs seconds and tokens to answer "what is this object" - "none" turns that off.
    /// Set to an empty string to omit the field for models that reject it.
    /// </summary>
    public string ReasoningEffort { get; set; } = "none";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
