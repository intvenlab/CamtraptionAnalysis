namespace CamtraptionAnalysis.Models;

public sealed class AnalysisEvent
{
    public string EventType { get; init; } = "";
    public AnalysisEventSeverity Severity { get; init; } = AnalysisEventSeverity.Info;
    public DateTime? EventTime { get; init; }
    public string Message { get; init; } = "";
    public string? FromMode { get; init; }
    public string? ToMode { get; init; }
    public string? RelatedFile { get; init; }
}
