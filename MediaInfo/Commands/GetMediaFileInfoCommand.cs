using System.Runtime.ExceptionServices;
using System.Threading.Channels;
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
    private const int DefaultMaximumThrottleLimit = 2;
    private const int MaximumThrottleLimit = 8;
    private const int BufferedWorkItemsPerWorker = 4;

    private readonly SortedDictionary<long, MediaWorkOutcome> _pendingOutcomes = [];
    private readonly object _cancellationLock = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Channel<MediaWorkItem>? _workChannel;
    private Channel<MediaWorkOutcome>? _completionChannel;
    private Task? _completionTask;
    private long _nextWorkSequence;
    private long _nextOutputSequence;
    private int _outstandingWorkItemCount;
    private int _maximumBufferedWorkItemCount;

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

    /// <summary>
    /// Gets or sets the media types to include, determined from each file's
    /// filename extension before MediaInfo reads the file.
    /// </summary>
    [Parameter]
    [Alias("Media", "Type", "M")]
    public MediaType[]? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of files inspected concurrently.
    /// </summary>
    [Parameter]
    [ValidateRange(1, MaximumThrottleLimit)]
    [Alias("threads", "workers", "T")]
    public int ThrottleLimit { get; set; } = Math.Clamp(
        Environment.ProcessorCount,
        1,
        DefaultMaximumThrottleLimit);

    /// <inheritdoc />
    protected override void BeginProcessing()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _maximumBufferedWorkItemCount = checked(
            ThrottleLimit * BufferedWorkItemsPerWorker);

        _workChannel = Channel.CreateBounded<MediaWorkItem>(
            new BoundedChannelOptions(checked(ThrottleLimit * 2))
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = ThrottleLimit == 1,
                SingleWriter = false,
            });

        _completionChannel = Channel.CreateUnbounded<MediaWorkOutcome>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = ThrottleLimit == 1,
            });

        Task[] workers = Enumerable
            .Range(0, ThrottleLimit)
            .Select(_ => ProcessWorkItemsAsync(
                _workChannel.Reader,
                _completionChannel.Writer,
                _cancellationTokenSource.Token))
            .ToArray();

        _completionTask = CompleteCompletionChannelAsync(
            workers,
            _completionChannel.Writer);
    }

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

            ResolveAndProcessPath(path, MyInvocation.ExpectingInput);
            DrainAvailableOutcomes();
        }
    }

    /// <inheritdoc />
    protected override void EndProcessing()
    {
        if (_workChannel is null ||
            _completionChannel is null ||
            _completionTask is null)
        {
            return;
        }

        _workChannel.Writer.TryComplete();

        try
        {
            while (_completionChannel.Reader
                .WaitToReadAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult())
            {
                DrainAvailableOutcomes();
            }

            _completionTask.GetAwaiter().GetResult();

            if (_cancellationTokenSource?.IsCancellationRequested == true)
            {
                return;
            }

            if (_outstandingWorkItemCount != 0 || _pendingOutcomes.Count != 0)
            {
                throw new InvalidOperationException(
                    "Media inspection completed without returning every queued result.");
            }
        }
        finally
        {
            CancelWorkerPool(dispose: true);
        }
    }

    /// <inheritdoc />
    protected override void StopProcessing()
    {
        CancelWorkerPool(dispose: false);
        _workChannel?.Writer.TryComplete();
    }

    private void ResolveAndProcessPath(string path, bool isPipelineInput)
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
                    QueueFile(
                        resolvedPath,
                        path,
                        skipDirectories: isPipelineInput);
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

            if (IsFileSystemProvider(literalPathProvider) &&
                (File.Exists(literalPath) || Directory.Exists(literalPath)))
            {
                QueueFile(
                    literalPath,
                    path,
                    skipDirectories: isPipelineInput);
                return;
            }

            bool isWildcardPath = WildcardPattern.ContainsWildcardCharacters(path);

            IReadOnlyList<string> resolvedPaths =
                GetResolvedProviderPathFromPSPath(path, out ProviderInfo provider);

            if (!IsFileSystemProvider(provider))
            {
                WriteNonFileSystemProviderError(path, provider);
                return;
            }

            foreach (string resolvedPath in resolvedPaths)
            {
                QueueFile(
                    resolvedPath,
                    path,
                    skipDirectories: isPipelineInput || isWildcardPath);
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

    private void QueueFile(
        string resolvedPath,
        string originalPath,
        bool skipDirectories)
    {
        if (!File.Exists(resolvedPath))
        {
            bool isDirectory = Directory.Exists(resolvedPath);

            if (isDirectory && skipDirectories)
            {
                return;
            }

            string message = isDirectory
                ? $"'{originalPath}' resolves to a directory, not a file."
                : $"The file '{originalPath}' does not exist.";

            WritePathError(
                new FileNotFoundException(message, resolvedPath),
                "MediaFileNotFound",
                ErrorCategory.ObjectNotFound,
                originalPath);
            return;
        }

        if (!ShouldProcessMediaType(resolvedPath))
        {
            return;
        }

        if (_workChannel is null || _cancellationTokenSource is null)
        {
            throw new InvalidOperationException(
                "The media inspection worker pool has not been initialized.");
        }

        CancellationToken cancellationToken = _cancellationTokenSource.Token;

        try
        {
            DrainAvailableOutcomes();

            while (_outstandingWorkItemCount + _pendingOutcomes.Count >=
                _maximumBufferedWorkItemCount)
            {
                WaitForAvailableOutcome(cancellationToken);
            }

            MediaWorkItem workItem = new(_nextWorkSequence++, resolvedPath);

            _workChannel.Writer
                .WriteAsync(workItem, cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            _outstandingWorkItemCount++;
            DrainAvailableOutcomes();
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            return;
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

    private void DrainAvailableOutcomes()
    {
        if (_completionChannel is null)
        {
            return;
        }

        while (_completionChannel.Reader.TryRead(out MediaWorkOutcome? outcome))
        {
            _outstandingWorkItemCount--;
            _pendingOutcomes.Add(outcome.Sequence, outcome);

            while (_pendingOutcomes.Remove(
                _nextOutputSequence,
                out MediaWorkOutcome? nextOutcome))
            {
                WriteOutcome(nextOutcome);
                _nextOutputSequence++;
            }
        }
    }

    private void WaitForAvailableOutcome(CancellationToken cancellationToken)
    {
        if (_completionChannel is null)
        {
            throw new InvalidOperationException(
                "The media inspection completion channel has not been initialized.");
        }

        bool canRead = _completionChannel.Reader
            .WaitToReadAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (!canRead)
        {
            throw new InvalidOperationException(
                "The media inspection workers stopped before all queued files completed.");
        }

        DrainAvailableOutcomes();
    }

    private void CancelWorkerPool(bool dispose)
    {
        lock (_cancellationLock)
        {
            if (_cancellationTokenSource is not { } cancellationTokenSource)
            {
                return;
            }

            cancellationTokenSource.Cancel();

            if (dispose)
            {
                cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }

    private void WriteOutcome(MediaWorkOutcome outcome)
    {
        if (outcome.Exception is not null)
        {
            if (!IsMediaInfoReadException(outcome.Exception))
            {
                ExceptionDispatchInfo.Capture(outcome.Exception).Throw();
            }

            WritePathError(
                outcome.Exception,
                "MediaInfoReadError",
                ErrorCategory.ReadError,
                outcome.Path);
            return;
        }

        MediaInfoResult result = outcome.Result ??
            throw new InvalidOperationException(
                "A media inspection completed without a result or exception.");

        if (Detailed)
        {
            WriteDetailedResult(result);
        }
        else
        {
            WriteObject(result);
        }
    }

    private static bool IsFileSystemProvider(ProviderInfo provider) =>
        typeof(FileSystemProvider).IsAssignableFrom(provider.ImplementingType);

    private bool ShouldProcessMediaType(string path)
    {
        return MediaType is not { Length: > 0 } mediaTypes ||
            mediaTypes.Contains(MediaTypeExtensionClassifier.Classify(path));
    }

    private static bool IsMediaInfoReadException(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        DllNotFoundException or
        EntryPointNotFoundException or
        BadImageFormatException or
        PlatformNotSupportedException;

    private static async Task ProcessWorkItemsAsync(
        ChannelReader<MediaWorkItem> workReader,
        ChannelWriter<MediaWorkOutcome> completionWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (MediaWorkItem workItem in
                workReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                MediaWorkOutcome outcome;

                try
                {
                    using MediaInfoReader reader = new(workItem.Path);
                    MediaInfoResult result = MediaInfoResultFactory.Create(reader);
                    outcome = new MediaWorkOutcome(
                        workItem.Sequence,
                        workItem.Path,
                        result,
                        null);
                }
                catch (Exception exception)
                {
                    outcome = new MediaWorkOutcome(
                        workItem.Sequence,
                        workItem.Path,
                        null,
                        exception);
                }

                if (!completionWriter.TryWrite(outcome))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task CompleteCompletionChannelAsync(
        IReadOnlyCollection<Task> workers,
        ChannelWriter<MediaWorkOutcome> completionWriter)
    {
        Exception? completionException = null;

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completionException = exception;
        }

        completionWriter.TryComplete(completionException);
    }

    private sealed record MediaWorkItem(long Sequence, string Path);

    private sealed record MediaWorkOutcome(
        long Sequence,
        string Path,
        MediaInfoResult? Result,
        Exception? Exception);
}
