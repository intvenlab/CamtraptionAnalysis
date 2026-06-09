using System.Threading.Channels;
using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public sealed record AnalysisPipelineResult(
    int FilesDiscovered,
    int SuccessCount,
    int ErrorCount,
    AnalysisSummary Summary,
    string ReportText,
    IReadOnlyList<ImageObservation> Observations,
    IReadOnlyList<CaptureTimelineEntry> Timeline);

public sealed class AnalysisPipeline
{
    private readonly MetadataFieldReader _reader = new();
    private readonly RamImageAnalysisPhase _analysisPhase = new();

    public async Task<AnalysisPipelineResult> RunAsync(
        string rootPath,
        int? maxFiles,
        IProgress<AnalysisPipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Input path not found: {rootPath}");
        }

        var workerCount = ParallelismSettings.MetadataWorkerCount;
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity: 256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });

        var results = new List<ImageObservation>();
        var resultsLock = new object();
        var filesDiscovered = 0;
        var filesProcessed = 0;
        var discoveryComplete = 0;
        var lastProcessedFileName = (string?)null;

        void Report(string? processedFileName = null)
        {
            if (!string.IsNullOrEmpty(processedFileName))
            {
                lastProcessedFileName = processedFileName;
            }

            progress?.Report(new AnalysisPipelineProgress(
                FilesDiscovered: Volatile.Read(ref filesDiscovered),
                FilesProcessed: Volatile.Read(ref filesProcessed),
                IsDiscoveryComplete: Volatile.Read(ref discoveryComplete) == 1,
                LastProcessedFileName: lastProcessedFileName));
        }

        var discoveryTask = Task.Run(
            () => DiscoverFiles(
                rootPath,
                maxFiles,
                channel.Writer,
                ref filesDiscovered,
                () => Report(),
                cancellationToken),
            cancellationToken);

        var workerTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(
                async () =>
                {
                    await foreach (var filePath in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var result = _reader.Read(filePath);
                        lock (resultsLock)
                        {
                            results.Add(result);
                        }

                        Interlocked.Increment(ref filesProcessed);
                        Report(Path.GetFileName(filePath));
                    }
                },
                cancellationToken))
            .ToArray();

        await discoveryTask.ConfigureAwait(false);
        Volatile.Write(ref discoveryComplete, 1);
        Report();

        await Task.WhenAll(workerTasks).ConfigureAwait(false);
        Report();

        var analysis = await Task.Run(
            () => _analysisPhase.Analyze(results),
            cancellationToken).ConfigureAwait(false);

        var ordered = analysis.OrderedResults;
        var timeline = analysis.Timeline;
        var successCount = ordered.Count(r => r.IsAnalyzable);
        var errorCount = ordered.Count - successCount;
        var summary = AnalysisSummaryBuilder.Build(rootPath, ordered, timeline);
        var report = CameraModeReportBuilder.Build(rootPath, ordered, timeline);

        return new AnalysisPipelineResult(
            FilesDiscovered: Volatile.Read(ref filesDiscovered),
            SuccessCount: successCount,
            ErrorCount: errorCount,
            Summary: summary,
            ReportText: report,
            Observations: ordered,
            Timeline: timeline);
    }

    private const int DiscoveryProgressInterval = 25;

    private static void DiscoverFiles(
        string rootPath,
        int? maxFiles,
        ChannelWriter<string> writer,
        ref int filesDiscovered,
        Action reportDiscovered,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var filePath in JpegFileEnumerator.EnumerateStreaming(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (maxFiles is > 0 && Volatile.Read(ref filesDiscovered) >= maxFiles.Value)
                {
                    break;
                }

                if (!writer.TryWrite(filePath))
                {
                    writer.WriteAsync(filePath, cancellationToken).AsTask().GetAwaiter().GetResult();
                }

                var discovered = Interlocked.Increment(ref filesDiscovered);
                if (discovered % DiscoveryProgressInterval == 0)
                {
                    reportDiscovered();
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }
}
