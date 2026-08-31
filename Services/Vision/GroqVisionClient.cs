using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MdfTracker.Api.Services.Vision;

/// <summary>Raised when Groq could not produce a description; the caller turns it into a 502.</summary>
public class GroqVisionException : Exception
{
    public GroqVisionException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Calls Groq's OpenAI-compatible chat completions endpoint with an image part.
///
/// The `groq` pub.dev package cannot do this: its message content is a plain string, while
/// vision needs content to be an array of {text, image_url} parts. So the request body is
/// built by hand here, which is also why the call lives on the server rather than the device.
/// </summary>
public class GroqVisionClient
{
    private const string CompletionsPath = "/openai/v1/chat/completions";

    /// <summary>
    /// Instructions ride in the user turn rather than a system message: Groq's vision models
    /// reject (or quietly ignore) a system prompt on a request that carries an image.
    ///
    /// The detail rules earn their length. Measured on the same crop, the terse earlier
    /// version returned "A red circle." where this one returns "A matte red textured
    /// circular disc." It costs about 185 more prompt tokens a call; on the free tier
    /// that is the difference between roughly five and six descriptions a minute, since
    /// the image still dominates at about 1,350 of the 1,530 total.
    /// </summary>
    private const string Prompt =
        """
        CONTEXT: You are a highly observant computer vision analyzer processing a camera crop of a target object for tracking.

        TASK: Identify the primary object exactly at the center and describe it with MAXIMUM detail density.

        CONSTRAINTS & RULES:
        1. Length: STRICTLY 12 words or fewer.
        2. Attributes to scan: Extract its color, material, shape, texture, condition (e.g., worn/shiny), and any visible text/logos.
        3. Format: Lead with the main color and type, then densely pack the descriptive attributes.
        4. Focus: Ignore the background, shadows, and any hands holding the object.
        5. Output Strictness: Return the description ONLY. Zero preamble, zero filler words. Use NO punctuation except a single final period.

        EDGE CASE:
        If the crop is too dark, blurry, or ambiguous to extract distinct details, output exactly and ONLY: "Unrecognisable."

        EXAMPLES:
        Input: Clear image -> Output: A glossy black rectangular leather wallet with a silver zipper.
        Input: Object with text -> Output: A worn blue cylindrical plastic bottle reading 'Aquafina'.
        """;

    private readonly HttpClient _http;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqVisionClient> _logger;

    public GroqVisionClient(HttpClient http, IOptions<GroqOptions> options, ILogger<GroqVisionClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Describes the object in <paramref name="imageBase64"/> (raw base64, no data: prefix).
    /// </summary>
    public async Task<string> DescribeAsync(string imageBase64, string mimeType, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new GroqVisionException("Groq is not configured. Set Groq:ApiKey to enable object descriptions.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["temperature"] = _options.Temperature,
            ["max_completion_tokens"] = _options.MaxCompletionTokens,
            ["stream"] = _options.Stream,
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = Prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mimeType};base64,{imageBase64}" }
                        }
                    }
                }
            }
        };

        // Omitted entirely when blank: models outside the Qwen 3 family reject the field.
        if (!string.IsNullOrWhiteSpace(_options.ReasoningEffort))
        {
            payload["reasoning_effort"] = _options.ReasoningEffort;
        }

        if (_options.TopP is { } topP)
        {
            payload["top_p"] = topP;
        }

        if (!string.IsNullOrWhiteSpace(_options.Stop))
        {
            payload["stop"] = _options.Stop;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, CompletionsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                request,
                _options.Stream
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GroqVisionException($"Groq did not respond within {_options.TimeoutSeconds}s.");
        }
        catch (HttpRequestException error)
        {
            throw new GroqVisionException($"Could not reach Groq: {error.Message}", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // An error is a normal JSON body even when streaming was requested, so it is
                // safe to read whole. The key is never in it, but it can be long - log once
                // and hand the caller a short reason. A 429 arrives here too, carrying Groq's
                // own "try again in Ns" text.
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Groq returned {Status}: {Body}", (int)response.StatusCode, error);
                throw new GroqVisionException($"Groq returned {(int)response.StatusCode}: {ErrorMessageOf(error)}");
            }

            var description = _options.Stream
                ? await ReadStreamedContentAsync(response, cancellationToken)
                : ContentOf(await response.Content.ReadAsStringAsync(cancellationToken));

            if (string.IsNullOrWhiteSpace(description))
            {
                throw new GroqVisionException("Groq returned an empty description.");
            }

            return Trim(description);
        }
    }

    /// <summary>
    /// Reassembles a server-sent event stream into the finished text.
    ///
    /// Groq emits one `data: {...}` line per token carrying choices[0].delta.content, then
    /// a final `data: [DONE]`. The caller needs the whole string to store it, so the deltas
    /// are concatenated rather than surfaced incrementally.
    /// </summary>
    private static async Task<string> ReadStreamedContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const string dataPrefix = "data:";
        const string doneMarker = "[DONE]";

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith(dataPrefix, StringComparison.Ordinal))
            {
                // Blank separator lines, and any comment lines a proxy might inject.
                continue;
            }

            var data = line[dataPrefix.Length..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == doneMarker)
            {
                break;
            }

            builder.Append(DeltaOf(data));
        }

        return builder.ToString();
    }

    /// <summary>
    /// choices[0].delta.content from one chunk, or empty. A malformed chunk is skipped
    /// rather than failing the whole description - the first chunk carries only the role,
    /// and the last may carry only a finish_reason.
    /// </summary>
    private static string DeltaOf(string chunk)
    {
        try
        {
            using var document = JsonDocument.Parse(chunk);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            if (!choices[0].TryGetProperty("delta", out var delta) ||
                !delta.TryGetProperty("content", out var content))
            {
                return string.Empty;
            }

            return content.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Pulls choices[0].message.content out of the completion.</summary>
    private static string? ContentOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new GroqVisionException("Groq returned a completion in an unexpected shape.", error);
        }
    }

    private static string ErrorMessageOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "unknown error";
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw body.
        }

        return body.Length > 200 ? body[..200] : body;
    }

    /// <summary>Models sometimes wrap the answer in quotes; the column is 500 chars.</summary>
    private static string Trim(string description)
    {
        var cleaned = description.Trim().Trim('"', '\'', '`').Trim();
        return cleaned.Length > 500 ? cleaned[..500] : cleaned;
    }
}
