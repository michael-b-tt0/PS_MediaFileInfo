using GetMediaInfo.Interop;

namespace GetMediaInfo;

/// <summary>
/// Provides managed access to the subset of MediaInfo used by Get-MediaFileInfo.
/// A reader owns one native MediaInfo instance and one opened file.
/// </summary>
public sealed class MediaInfoReader : IDisposable
{
    private readonly MediaInfoHandle _handle;

    /// <summary>
    /// Opens a media file for inspection.
    /// </summary>
    /// <param name="path">The path to a file understood by MediaInfo.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="path"/> does not identify an existing file.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// MediaInfo could not create an instance or open the file.
    /// </exception>
    public MediaInfoReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The media file was not found.", fullPath);
        }

        _handle = MediaInfoNative.New();

        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new InvalidOperationException(
                "MediaInfo_New returned an invalid native handle.");
        }

        try
        {
            if (MediaInfoNative.Open(_handle, fullPath) == 0)
            {
                throw new InvalidOperationException(
                    $"MediaInfo could not open '{fullPath}'.");
            }

            FilePath = fullPath;
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the normalized path opened by this reader.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets a MediaInfo parameter value.
    /// </summary>
    public string GetInfo(
        MediaInfoStreamKind streamKind,
        int streamIndex,
        string parameter)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);

        nint value = MediaInfoNative.Get(
            _handle,
            streamKind,
            checked((nuint)streamIndex),
            parameter,
            MediaInfoValueKind.Text,
            MediaInfoValueKind.Name);

        return MediaInfoNative.CopyString(value);
    }

    /// <summary>
    /// Gets the number of streams of a particular kind.
    /// </summary>
    public int GetStreamCount(MediaInfoStreamKind streamKind)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        nuint count = MediaInfoNative.CountGet(
            _handle,
            streamKind,
            nuint.MaxValue);

        return checked((int)count);
    }

    /// <summary>
    /// Gets MediaInfo's formatted report for the opened file.
    /// </summary>
    public string GetSummary(bool complete = false, bool raw = false)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        SetOption("Language", raw ? "raw" : string.Empty);
        SetOption("Complete", complete ? "1" : "0");

        return MediaInfoNative.CopyString(MediaInfoNative.Inform(_handle, 0));
    }

    /// <summary>
    /// Sets an instance-level MediaInfo option and returns its response.
    /// </summary>
    public string SetOption(string option, string value = "")
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        ArgumentNullException.ThrowIfNull(value);

        return MediaInfoNative.CopyString(
            MediaInfoNative.Option(_handle, option, value));
    }

    /// <summary>
    /// Releases the native MediaInfo instance.
    /// </summary>
    public void Dispose() => _handle.Dispose();
}
