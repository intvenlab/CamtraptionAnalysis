using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class AnalysisSummaryBuilder
{
    public static AnalysisSummary Build(
        string rootPath,
        IReadOnlyList<ImageObservation> ordered,
        IReadOnlyList<CaptureTimelineEntry> timeline)
    {
        var readable = ordered.Where(o => o.IsAnalyzable).ToList();
        var eventRows = timeline.Where(e => e.IsEventRow).ToList();
        var serials = readable
            .Select(o => o.SerialNumber)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        var artists = readable
            .Select(o => o.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AnalysisSummary
        {
            RootPath = rootPath,
            FilesAnalyzed = readable.Count,
            ReadErrors = ordered.Count - readable.Count,
            SerialDisplay = serials.Count switch
            {
                0 => "(missing)",
                1 => serials[0],
                _ => string.Join(", ", serials),
            },
            ArtistDisplay = artists.Count switch
            {
                0 => "(missing)",
                1 => artists[0],
                _ => string.Join(" | ", artists),
            },
            MultipleSerialsWarning = serials.Count > 1,
            MultipleArtistsWarning = artists.Count > 1,
            InferredMismatches = readable.Count(o => o.ScheduleMatch == "MISMATCH"),
            LoggedScheduleMismatches = readable.Count(o => o.LoggedScheduleMatch == "MISMATCH"),
            LoggedInferredMismatches = readable.Count(o => o.LoggedInferredMatch == "MISMATCH"),
            CopyrightParseFailures = 0,
            TimelineEventCount = eventRows.Count,
            TimelineErrorCount = eventRows.Count(e => e.Event?.Severity == AnalysisEventSeverity.Error),
            TimelineWarningCount = eventRows.Count(e => e.Event?.Severity == AnalysisEventSeverity.Warning),
            TimelineNoteCount = eventRows.Count(e => e.Event?.Severity == AnalysisEventSeverity.Note),
        };
    }
}
