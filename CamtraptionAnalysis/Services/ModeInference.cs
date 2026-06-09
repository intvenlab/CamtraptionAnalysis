namespace CamtraptionAnalysis.Services;

public sealed record ModeInferenceResult(string Mode, string Confidence, string Reason);

public static class ModeInference
{
    public static ModeInferenceResult Infer(string shutterMode, string flashExposureComp)
    {
        var shutterModeLower = shutterMode.ToLowerInvariant();
        var fecStops = ParseFlashExposureCompStops(flashExposureComp);

        if (shutterModeLower.Contains("electronic") && !shutterModeLower.Contains("first curtain"))
        {
            return new ModeInferenceResult("C1", "high", "ShutterMode is Electronic Shutter");
        }

        if (shutterModeLower.Contains("electronic first curtain"))
        {
            if (fecStops is not null)
            {
                if (Math.Abs(fecStops.Value - (-1.0 / 3.0)) <= 0.01)
                {
                    return new ModeInferenceResult(
                        "C2",
                        "high",
                        $"EFCS + FlashExposureComp {flashExposureComp} indicates C2");
                }

                return new ModeInferenceResult(
                    "C3",
                    "high",
                    $"EFCS + FlashExposureComp {flashExposureComp} indicates C3");
            }

            return new ModeInferenceResult("C2/C3?", "low", "EFCS detected but FlashExposureComp is missing/unparseable");
        }

        return new ModeInferenceResult(
            "UNKNOWN",
            "low",
            $"Unrecognized ShutterMode: {(string.IsNullOrWhiteSpace(shutterMode) ? "(missing)" : shutterMode)}");
    }

    public static double? ParseFlashExposureCompStops(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        if (text.Contains('/'))
        {
            var parts = text.Split('/', 2);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], out var numerator) &&
                double.TryParse(parts[1], out var denominator) &&
                denominator != 0)
            {
                return numerator / denominator;
            }

            return null;
        }

        return double.TryParse(text, out var number) ? number : null;
    }
}
