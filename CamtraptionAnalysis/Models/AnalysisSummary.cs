namespace CamtraptionAnalysis.Models;

public sealed class AnalysisSummary
{
    public string RootPath { get; init; } = "";
    public int FilesAnalyzed { get; init; }
    public int ReadErrors { get; init; }
    public string SerialDisplay { get; init; } = "";
    public string ArtistDisplay { get; init; } = "";
    public bool MultipleSerialsWarning { get; init; }
    public bool MultipleArtistsWarning { get; init; }
    public int InferredMismatches { get; init; }
    public int LoggedScheduleMismatches { get; init; }
    public int LoggedInferredMismatches { get; init; }
    public int CopyrightParseFailures { get; init; }
    public int TimelineEventCount { get; init; }
    public int TimelineErrorCount { get; init; }
    public int TimelineWarningCount { get; init; }
    public int TimelineNoteCount { get; init; }

    public string ToDisplayText(string? reportFilePath)
    {
        var lines = new List<string>
        {
            $"Root: {RootPath}",
            $"Files analyzed: {FilesAnalyzed}",
            $"Read errors: {ReadErrors}",
            $"Camera serial: {SerialDisplay}",
            $"Schedule (Artist): {ArtistDisplay}",
            $"Inferred schedule mismatches: {InferredMismatches}",
            $"Logged schedule mismatches: {LoggedScheduleMismatches}",
            $"Logged vs inferred mismatches: {LoggedInferredMismatches}",
            $"Malformed copyright stamps: {CopyrightParseFailures}",
            $"Timeline events: {TimelineEventCount} (errors {TimelineErrorCount}, warnings {TimelineWarningCount}, notes {TimelineNoteCount})",
        };

        if (MultipleSerialsWarning)
        {
            lines.Add("WARNING: Multiple serial numbers detected in this scan.");
        }

        if (MultipleArtistsWarning)
        {
            lines.Add("WARNING: Multiple author schedule strings detected in this scan.");
        }

        if (!string.IsNullOrWhiteSpace(reportFilePath))
        {
            lines.Add($"Report saved: {reportFilePath}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
