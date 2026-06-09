using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public sealed record FieldProbeProgress(int Done, int Total, string FileName);

public sealed record FieldProbeRunResult(
    string ReportText,
    int FilesFoundTotal,
    int SuccessCount,
    int ErrorCount,
    IReadOnlyList<ImageObservation> Results);

public sealed class FieldProbeRunner
{
    private readonly AnalysisPipeline _pipeline = new();

    public async Task<FieldProbeRunResult> RunAsync(
        string rootPath,
        int maxFiles,
        IProgress<FieldProbeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pipelineProgress = progress is null
            ? null
            : new Progress<AnalysisPipelineProgress>(p =>
            {
                var total = Math.Max(p.FilesDiscovered, 1);
                progress.Report(new FieldProbeProgress(
                    p.FilesProcessed,
                    total,
                    p.LastProcessedFileName ?? string.Empty));
            });

        var result = await _pipeline.RunAsync(
            rootPath,
            maxFiles > 0 ? maxFiles : null,
            pipelineProgress,
            cancellationToken).ConfigureAwait(false);

        return new FieldProbeRunResult(
            result.ReportText,
            result.FilesDiscovered,
            result.SuccessCount,
            result.ErrorCount,
            result.Observations);
    }
}
