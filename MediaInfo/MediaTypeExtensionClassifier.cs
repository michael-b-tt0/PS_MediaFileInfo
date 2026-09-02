namespace GetMediaInfo;

/// <summary>
/// Classifies common media filename extensions for input filtering.
/// </summary>
internal static class MediaTypeExtensionClassifier
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2", ".3gp", ".asf", ".avi", ".divx", ".f4v", ".flv", ".m2ts", ".m4v",
        ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".mxf", ".ogv", ".rm",
        ".rmvb", ".ts", ".vob", ".webm", ".wmv",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".aif", ".aiff", ".alac", ".amr", ".ape", ".au", ".caf", ".dts",
        ".flac", ".m4a", ".m4b", ".mka", ".mp2", ".mp3", ".mpc", ".oga", ".ogg",
        ".opus", ".ra", ".tak", ".tta", ".wav", ".weba", ".wma", ".wv",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".dng", ".exr", ".gif", ".heic", ".heif", ".ico", ".j2k",
        ".jp2", ".jpe", ".jpeg", ".jpg", ".jxl", ".png", ".psd", ".tif", ".tiff",
        ".webp",
    };

    /// <summary>
    /// Gets the media type associated with the file's extension, or Unknown
    /// when the extension is not in the known media-extension sets.
    /// </summary>
    internal static MediaType Classify(string path)
    {
        string extension = Path.GetExtension(path);

        if (VideoExtensions.Contains(extension))
        {
            return MediaType.Video;
        }

        if (AudioExtensions.Contains(extension))
        {
            return MediaType.Audio;
        }

        return ImageExtensions.Contains(extension)
            ? MediaType.Image
            : MediaType.Unknown;
    }
}
