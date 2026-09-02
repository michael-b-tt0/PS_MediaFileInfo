using System.Management.Automation;
using Microsoft.PowerShell.Commands;

namespace GetMediaInfo.Commands;

/// <summary>
/// Gets technical metadata for media files.
/// </summary>
[Cmdlet(
    VerbsCommon.Get,
    "MediaFileInfo",
    DefaultParameterSetName = PathParameterSet)]
[OutputType(
    typeof(VideoMediaInfoResult),
    typeof(AudioMediaInfoResult),
    typeof(ImageMediaInfoResult),
    typeof(UnknownMediaInfoResult))]
public sealed class GetMediaFileInfoCommand : PSCmdlet
{
    private const string PathParameterSet = "Path";
    private const string LiteralPathParameterSet = "LiteralPath";

    /// <summary>
    /// Gets or sets paths to resolve, including wildcard patterns.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = PathParameterSet,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = [];

    /// <summary>
    /// Gets or sets paths that should be used exactly as entered.
    /// </summary>
    [Parameter(
        Mandatory = true,
        ParameterSetName = LiteralPathParameterSet,
        ValueFromPipelineByPropertyName = true)]
    [Alias("LP", "PSPath")]
    public string[] LiteralPath { get; set; } = [];

    /// <summary>
    /// Gets or sets whether to display all properties available on each result.
    /// This affects only the default formatting view; the emitted result object
    /// remains strongly typed.
    /// </summary>
    [Parameter]
    [Alias("D")]
    public SwitchParameter Detailed { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        string[] paths = ParameterSetName == LiteralPathParameterSet
            ? LiteralPath
            : Path;

        foreach (string path in paths)
        {
            if (Stopping)
            {
                return;
            }

            ResolveAndProcessPath(path);
        }
    }

    private void ResolveAndProcessPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            WritePathError(
                new ArgumentException("The path cannot be empty or whitespace."),
                "EmptyMediaPath",
                ErrorCategory.InvalidArgument,
                path);
            return;
        }

        try
        {
            if (ParameterSetName == LiteralPathParameterSet)
            {
                string resolvedPath = SessionState.Path
                    .GetUnresolvedProviderPathFromPSPath(
                        path,
                        out ProviderInfo literalParameterProvider,
                        out _);

                if (IsFileSystemProvider(literalParameterProvider))
                {
                    ProcessFile(resolvedPath, path);
                }
                else
                {
                    WriteNonFileSystemProviderError(path, literalParameterProvider);
                }

                return;
            }

            // Prefer an exact filesystem match before asking PowerShell to
            // expand wildcards. This lets ordinary paths containing wildcard
            // metacharacters (such as release tags in square brackets) work
            // without requiring callers to select -LiteralPath explicitly.
            string literalPath = SessionState.Path
                .GetUnresolvedProviderPathFromPSPath(
                    path,
                    out ProviderInfo literalPathProvider,
                    out _);

            if (IsFileSystemProvider(literalPathProvider) && File.Exists(literalPath))
            {
                ProcessFile(literalPath, path);
                return;
            }

            IReadOnlyList<string> resolvedPaths =
                GetResolvedProviderPathFromPSPath(path, out ProviderInfo provider);

            if (!IsFileSystemProvider(provider))
            {
                WriteNonFileSystemProviderError(path, provider);
                return;
            }

            foreach (string resolvedPath in resolvedPaths)
            {
                ProcessFile(resolvedPath, path);
            }
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ItemNotFoundException or
            ProviderNotFoundException or
            System.Management.Automation.DriveNotFoundException or
            NotSupportedException)
        {
            WritePathError(
                exception,
                "MediaPathNotFound",
                ErrorCategory.ObjectNotFound,
                path);
        }
    }

    private void ProcessFile(string resolvedPath, string originalPath)
    {
        if (!File.Exists(resolvedPath))
        {
            string message = Directory.Exists(resolvedPath)
                ? $"'{originalPath}' resolves to a directory, not a file."
                : $"The file '{originalPath}' does not exist.";

            WritePathError(
                new FileNotFoundException(message, resolvedPath),
                "MediaFileNotFound",
                ErrorCategory.ObjectNotFound,
                originalPath);
            return;
        }

        try
        {
            using MediaInfoReader reader = new(resolvedPath);
            MediaInfoResult result = MediaInfoResultFactory.Create(reader);

            if (Detailed)
            {
                WriteDetailedResult(result);
            }
            else
            {
                WriteObject(result);
            }
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException or
            PlatformNotSupportedException)
        {
            WritePathError(
                exception,
                "MediaInfoReadError",
                ErrorCategory.ReadError,
                resolvedPath);
        }
    }

    private void WriteNonFileSystemProviderError(
        string path,
        ProviderInfo provider)
    {
        WritePathError(
            new NotSupportedException(
                $"Provider '{provider.Name}' is not a filesystem provider."),
            "MediaPathNotFileSystem",
            ErrorCategory.InvalidArgument,
            path);
    }

    private void WritePathError(
        Exception exception,
        string errorId,
        ErrorCategory category,
        object? target)
    {
        WriteError(new ErrorRecord(exception, errorId, category, target));
    }

    private void WriteDetailedResult(MediaInfoResult result)
    {
        PSObject displayResult = PSObject.AsPSObject(result);
        displayResult.TypeNames.Insert(0, $"{result.GetType().FullName}.Extended");
        WriteObject(displayResult);
    }

    private static bool IsFileSystemProvider(ProviderInfo provider) =>
        typeof(FileSystemProvider).IsAssignableFrom(provider.ImplementingType);
}
