using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public sealed class RamImageAnalysisPhase
{
    public RamAnalysisResult Analyze(IReadOnlyList<ImageObservation> results)
    {
        foreach (var obs in results)
        {
            if (!obs.IsAnalyzable)
            {
                continue;
            }

            var inference = ModeInference.Infer(obs.ShutterMode, obs.FlashExposureComp);
            obs.InferredMode = inference.Mode;
            obs.Confidence = inference.Confidence;
            obs.Reason = inference.Reason;
            obs.ExpectedMode = ScheduleAnalyzer.ExpectedModeForCapture(obs.CaptureInstant, obs.Artist);
            obs.ScheduleMatch = ScheduleAnalyzer.EvaluateScheduleMatch(obs.InferredMode, obs.ExpectedMode);
            obs.LoggedScheduleMatch = ScheduleAnalyzer.EvaluateLoggedScheduleMatch(obs.CopyrightMode, obs.ExpectedMode);
            obs.LoggedInferredMatch = ScheduleAnalyzer.EvaluateLoggedInferredMatch(obs.CopyrightMode, obs.InferredMode);
        }

        var ordered = ObservationOrdering.SortBySerialThenCaptureTime(results);

        var timeline = CaptureTimelineBuilder.Build(ordered);
        return new RamAnalysisResult(ordered, timeline);
    }
}

public sealed record RamAnalysisResult(
    IReadOnlyList<ImageObservation> OrderedResults,
    IReadOnlyList<CaptureTimelineEntry> Timeline);
