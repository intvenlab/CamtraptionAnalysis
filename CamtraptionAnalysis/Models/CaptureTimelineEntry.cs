using CamtraptionAnalysis.Services;

namespace CamtraptionAnalysis.Models;

public sealed class CaptureTimelineEntry
{
    public bool IsEventRow { get; init; }
    public ImageObservation? Observation { get; init; }
    public AnalysisEvent? Event { get; init; }
    public DateTime SortTime { get; init; }
    public int SortOrder { get; init; }

    public string SortSerial { get; init; } = "";

    public static CaptureTimelineEntry FromObservation(ImageObservation observation, int sortOrder = 10) =>
        new()
        {
            IsEventRow = false,
            Observation = observation,
            SortSerial = observation.SerialNumber,
            SortTime = observation.CaptureInstant ?? DateTime.MinValue,
            SortOrder = sortOrder,
        };

    public static CaptureTimelineEntry FromEvent(
        AnalysisEvent analysisEvent,
        string serialNumber,
        int sortOrder = 5) =>
        new()
        {
            IsEventRow = true,
            Event = analysisEvent,
            SortSerial = serialNumber,
            SortTime = analysisEvent.EventTime ?? DateTime.MinValue,
            SortOrder = sortOrder,
        };

    public string RowKind => IsEventRow ? "EVENT" : "CAPTURE";

    public string ExpectedModeDisplay
    {
        get
        {
            if (IsEventRow)
            {
                return Event?.EventType ?? "";
            }

            return Observation?.ExpectedModeDisplay ?? "";
        }
    }

    public string InferredMode =>
        IsEventRow ? Event?.Message ?? "" : Observation?.InferredMode ?? "";

    public string CopyrightMode
    {
        get
        {
            if (IsEventRow)
            {
                if (!string.IsNullOrEmpty(Event?.FromMode) || !string.IsNullOrEmpty(Event?.ToMode))
                {
                    return $"{Event?.FromMode} → {Event?.ToMode}";
                }

                return Event?.Severity.ToString().ToUpperInvariant() ?? "";
            }

            return Observation?.CopyrightMode ?? "";
        }
    }

    public string CaptureTimeDisplay =>
        CaptureTimeFormatter.Format(IsEventRow ? Event?.EventTime : Observation?.CaptureInstant);

    public string SourceFile =>
        IsEventRow ? Event?.RelatedFile ?? "" : Observation?.SourceFile ?? "";

    public string ScheduleMatch
    {
        get
        {
            if (IsEventRow)
            {
                return Event?.Severity switch
                {
                    AnalysisEventSeverity.Error => "ERROR",
                    AnalysisEventSeverity.Warning => "WARN",
                    AnalysisEventSeverity.Note => "NOTE",
                    _ => "EVENT",
                };
            }

            return Observation?.ScheduleMatch ?? "";
        }
    }

    public string ShutterTypeDisplay => IsEventRow ? "" : Observation?.ShutterTypeDisplay ?? "";
    public string ShutterSpeed => IsEventRow ? "" : Observation?.ShutterSpeed ?? "";
    public string Iso => IsEventRow ? "" : Observation?.Iso ?? "";
    public string FlashExposureComp => IsEventRow ? "" : Observation?.FlashExposureComp ?? "";
    public string SerialNumber => IsEventRow ? SortSerial : Observation?.SerialNumber ?? "";
    public string CopyrightText => IsEventRow ? "" : Observation?.CopyrightTextDisplay ?? "";

    public bool IsErrorHighlight =>
        (IsEventRow && Event?.Severity == AnalysisEventSeverity.Error) ||
        (!IsEventRow && Observation is not null && (
            Observation.IsScheduleMismatch ||
            (Observation.LoggedScheduleMatch == "MISMATCH" &&
             !ScheduleAnalyzer.IsCopyrightLogWriteFailure(Observation)) ||
            (Observation.LoggedInferredMatch == "MISMATCH" &&
             !ScheduleAnalyzer.IsCopyrightLogWriteFailure(Observation)) ||
            Observation.HasConfigError));

    public bool IsWarningHighlight =>
        (IsEventRow && Event?.Severity == AnalysisEventSeverity.Warning) ||
        (!IsEventRow && Observation is not null &&
         ScheduleAnalyzer.IsCopyrightLogWriteFailure(Observation));

    public bool IsCopyrightLogFailedHighlight =>
        IsEventRow &&
        string.Equals(Event?.EventType, "Copyright Log Failed", StringComparison.Ordinal);

    public bool IsNoteHighlight =>
        IsEventRow &&
        Event?.Severity == AnalysisEventSeverity.Note &&
        Event.EventType != "Scheduled Wake";

    public bool IsWakeHighlight =>
        IsEventRow &&
        string.Equals(Event?.EventType, "Scheduled Wake", StringComparison.Ordinal);

    public bool IsModeChangeHighlight =>
        IsEventRow &&
        string.Equals(Event?.EventType, "Mode Change", StringComparison.Ordinal);

    public bool IsTransitionHighlight =>
        IsEventRow &&
        string.Equals(Event?.EventType, "Schedule Transition", StringComparison.Ordinal);

    public bool IsScheduleMismatch =>
        !IsEventRow && Observation?.IsScheduleMismatch == true;

    public bool IsNotableForFilter =>
        IsErrorHighlight ||
        IsWarningHighlight ||
        IsNoteHighlight ||
        IsWakeHighlight ||
        IsModeChangeHighlight ||
        IsTransitionHighlight ||
        IsCopyrightLogFailedHighlight ||
        IsScheduleMismatch;
}
