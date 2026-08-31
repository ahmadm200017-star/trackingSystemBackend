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

    /// <summary>
    /// Deliberately small. A description is one short sentence - measured completions are
    /// 8 to 9 tokens - and the value counts against the account's tokens-per-minute limit
    /// as a reservation, not as tokens actually used.
    ///
    /// That matters on the free tier's 8,000 TPM: at 2048 each call reserved roughly
    /// 1,350 prompt + 2,048 completion, so barely two descriptions fitted in a minute and
    /// the third was rate limited. At 96 it is about 1,450 a call, or five to six a minute.
    /// Groq's published example uses 2048; that is a playground default, not a requirement.
    /// </summary>
    public int MaxCompletionTokens { get; set; } = 96;

    public double Temperature { get; set; } = 0.6;

    /// <summary>Nucleus sampling, sent as <c>top_p</c>. Omitted when null.</summary>
    public double? TopP { get; set; } = 0.95;

    /// <summary>Sent as <c>stop</c>. Null means no stop sequence, which is the default.</summary>
    public string? Stop { get; set; }

    /// <summary>
    /// When true the completion is requested as a server-sent event stream and reassembled
    /// here. The endpoint still answers with one finished description either way - it has to
    /// store the whole string - but streaming keeps the connection producing bytes, which
    /// avoids an idle-timeout on a long generation.
    /// </summary>
    public bool Stream { get; set; } = true;

    /// <summary>
    /// Groq has been observed taking several seconds even to reject a bad key, so this is
    /// deliberately generous. It must stay below the mobile client's own deadline for
    /// POST /description, otherwise the device gives up before the server can answer.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Sent as <c>reasoning_effort</c>. "default" is what Groq's example for this model uses,
    /// and measured against this prompt it returns the same answer for the same token count
    /// as "none" - the model does not spend reasoning on a request this small. "none" is
    /// still available if that changes. An empty string omits the field, for models that
    /// reject it.
    /// </summary>
    public string ReasoningEffort { get; set; } = "default";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
