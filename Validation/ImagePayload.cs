namespace MdfTracker.Api.Validation;

/// <summary>
/// Checks that an uploaded description crop really is an image before it costs a Groq call.
///
/// Without this, any base64 blob - a text file, a truncated upload - is forwarded to Groq,
/// which spends a request and rate-limit budget only to fail. The signature check is cheap
/// and catches the realistic failure: a device that encoded the wrong buffer.
/// </summary>
public static class ImagePayload
{
    public enum Kind
    {
        Unknown,
        Jpeg,
        Png,
        WebP
    }

    /// <summary>Identifies the format from its magic bytes, ignoring the declared mime type.</summary>
    public static Kind Identify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Kind.Jpeg;
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return Kind.Png;
        }

        // RIFF....WEBP
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return Kind.WebP;
        }

        return Kind.Unknown;
    }

    public static string MimeTypeOf(Kind kind) => kind switch
    {
        Kind.Jpeg => "image/jpeg",
        Kind.Png => "image/png",
        Kind.WebP => "image/webp",
        _ => "application/octet-stream"
    };
}
