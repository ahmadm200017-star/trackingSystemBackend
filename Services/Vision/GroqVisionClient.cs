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
    /// </summary>
    private const string Prompt =
        "This is a crop from a phone camera showing an object that is about to be tracked. " +
        "Describe the main object at the centre in one short sentence, 12 words or fewer. " +
        "Lead with its colour and type, for example \"a red ceramic mug\". " +
        "Reply with the description only, no preamble, no punctuation beyond a final period. " +
        "If the crop is too blurry or dark to tell, reply exactly: Unrecognisable.";

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

        var payload = new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            max_completion_tokens = _options.MaxCompletionTokens,
            messages = new object[]
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

        using var request = new HttpRequestMessage(HttpMethod.Post, CompletionsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The key is never in the body, but the body can be long; log it once and
                // hand the caller a short reason.
                _logger.LogWarning("Groq returned {Status}: {Body}", (int)response.StatusCode, body);
                throw new GroqVisionException($"Groq returned {(int)response.StatusCode}: {ErrorMessageOf(body)}");
            }

            var description = ContentOf(body);
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new GroqVisionException("Groq returned an empty description.");
            }

            return Trim(description);
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
