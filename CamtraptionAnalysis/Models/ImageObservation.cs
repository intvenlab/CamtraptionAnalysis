using CamtraptionAnalysis.Services;

namespace CamtraptionAnalysis.Models;

public sealed class ImageObservation
{
    public string FilePath { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public string? ReadError { get; init; }

    public string SerialNumber { get; init; } = "";
    public string Artist { get; init; } = "";
    public string CopyrightText { get; init; } = "";
    public CopyrightStamp CopyrightParsed { get; init; } = new();
    public DateTime? CaptureInstant { get; init; }
    public string ShutterSpeed { get; init; } = "";
    public string Iso { get; init; } = "";
    public string ShutterMode { get; init; } = "";
    public string FlashExposureComp { get; init; } = "";
    public string ShutterSpeedRange { get; init; } = "";

    public string InferredMode { get; set; } = "";
    public string ExpectedMode { get; set; } = "";
    public string ScheduleMatch { get; set; } = "";
    public string LoggedScheduleMatch { get; set; } = "";
    public string LoggedInferredMatch { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string Reason { get; set; } = "";

    public string CopyrightMode => CopyrightParsed.Mode;
    public string CopyrightTimeHhmmss => CopyrightParsed.TimeHhmmss;
    public string CopyrightVoltage => CopyrightParsed.Voltage;
    public string CopyrightError => CopyrightParsed.Error;

    public string CopyrightErrorDisplay => CameraErrorDecoder.FormatErrorField(CopyrightParsed.Error);

    public string CopyrightTextDisplay =>
        CameraErrorDecoder.EnhanceCopyrightText(CopyrightText, CopyrightParsed.Error);

    public bool IsReadable => ReadError is null;

    public bool HasCopyrightField => !string.IsNullOrWhiteSpace(CopyrightText);

    public bool IsAnalyzable => IsReadable && CopyrightParsed.IsParsed;

    public string ExpectedModeDisplay =>
        ScheduleMatch == "MISMATCH" && ExpectedMode is not "" and not "UNKNOWN"
            ? ExpectedMode + "!"
            : ExpectedMode;

    public string CaptureTimeDisplay =>
        CaptureTimeFormatter.Format(CaptureInstant);

    public string ShutterTypeDisplay
    {
        get
        {
            var mode = ShutterMode.ToLowerInvariant();
            if (mode.Contains("electronic first curtain"))
            {
                return "EFCS";
            }

            if (mode.Contains("electronic"))
            {
                return "Electronic";
            }

            return string.IsNullOrWhiteSpace(ShutterMode) ? "" : "Unknown";
        }
    }

    public bool IsScheduleMismatch => ScheduleMatch == "MISMATCH";

    public bool HasMissingCopyrightLog =>
        HasCopyrightField && !CopyrightParsed.IsParsed;

    public bool HasConfigError =>
        CopyrightParsed.IsParsed &&
        !string.IsNullOrWhiteSpace(CopyrightParsed.Error) &&
        !CopyrightParsed.Error.Equals("None", StringComparison.OrdinalIgnoreCase);
}
