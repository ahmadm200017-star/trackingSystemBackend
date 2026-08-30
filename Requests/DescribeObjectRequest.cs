using System.ComponentModel.DataAnnotations;

namespace MdfTracker.Api.Requests;

/// <summary>
/// Body of <c>POST /api/sessions/{id}/description</c>. The device sends the padded crop
/// around the seed box taken from the first frame of the run, and the server asks Groq
/// what it is — the Groq key stays here and never ships in the app.
/// </summary>
public class DescribeObjectRequest
{
    /// <summary>
    /// Raw base64 of the crop, with no <c>data:</c> prefix. Groq accepts up to 4 MB of
    /// base64; a padded crop is a few tens of KB, so the cap below is a sanity bound.
    /// </summary>
    [Required(ErrorMessage = "imageBase64 is required.")]
    [MaxLength(4_000_000, ErrorMessage = "imageBase64 must be under 4 MB.")]
    public string? ImageBase64 { get; set; }

    /// <summary>Defaults to image/jpeg, which is what the app encodes.</summary>
    [RegularExpression("^image/(jpeg|png|webp)$", ErrorMessage = "mimeType must be image/jpeg, image/png or image/webp.")]
    public string? MimeType { get; set; }
}
