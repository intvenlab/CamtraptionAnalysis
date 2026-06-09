namespace CamtraptionAnalysis.Services;

public sealed record AnalysisPipelineProgress(
    int FilesDiscovered,
    int FilesProcessed,
    bool IsDiscoveryComplete,
    string? LastProcessedFileName);
