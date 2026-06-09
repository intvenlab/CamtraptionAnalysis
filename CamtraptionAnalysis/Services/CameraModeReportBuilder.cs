using System.Globalization;
using System.Text;
using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class CameraModeReportBuilder
{
    public static string Build(
        string rootPath,
        IReadOnlyList<ImageObservation> ordered,
        IReadOnlyList<CaptureTimelineEntry> timeline)
    {
        var readable = ordered.Where(o => o.IsAnalyzable).ToList();
        var serials = readable.Select(o => o.SerialNumber).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
        var artists = readable
            .Select(o => o.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serialDisplay = serials.Count switch
        {
            0 => "(missing)",
            1 => serials[0],
            _ => string.Join(", ", serials),
        };

        var artistDisplay = artists.Count switch
        {
            0 => "(missing)",
            1 => artists[0],
            _ => string.Join(" | ", artists),
        };

        var inferredMismatches = readable.Count(o => o.ScheduleMatch == "MISMATCH");
        var loggedScheduleMismatches = readable.Count(o => o.LoggedScheduleMatch == "MISMATCH");
        var loggedInferredMismatches = readable.Count(o => o.LoggedInferredMatch == "MISMATCH");
        var copyrightParseFailures = 0;
        var eventRows = timeline.Where(e => e.IsEventRow).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Camera Mode Analysis Report");
        builder.AppendLine("===========================");
        builder.AppendLine($"Root: {rootPath}");
        builder.AppendLine($"Files analyzed: {readable.Count}");
        builder.AppendLine($"Read errors: {ordered.Count - readable.Count}");
        builder.AppendLine($"Camera Serial Number: {serialDisplay}");
        builder.AppendLine($"Author (Schedule String): {artistDisplay}");
        if (serials.Count > 1)
        {
            builder.AppendLine("WARNING: Multiple serial numbers detected in this scan.");
        }

        if (artists.Count > 1)
        {
            builder.AppendLine("WARNING: Multiple author schedule strings detected in this scan.");
        }

        builder.AppendLine($"Inferred schedule mismatches: {inferredMismatches}");
        builder.AppendLine($"Logged schedule mismatches: {loggedScheduleMismatches}");
        builder.AppendLine($"Logged vs inferred mismatches: {loggedInferredMismatches}");
        builder.AppendLine($"Malformed/missing copyright stamps: {copyrightParseFailures}");
        builder.AppendLine($"Timeline events: {eventRows.Count}");
        builder.AppendLine();
        builder.AppendLine("Capture Timeline");
        builder.AppendLine("----------------");
        builder.AppendLine(
            "Timestamp,Filename,Row Type,Event Type,Severity,Message,From Mode,To Mode," +
            "Expected Mode,Actual Mode,Logged Mode,Author,Copyright," +
            "Copyright Time,Copyright Voltage,Copyright ERR," +
            "Schedule Match (Inferred),Schedule Match (Logged),Logged vs Inferred," +
            "Shutter Type,Shutter Speed,ISO,FlashExposureComp,Serial");

        foreach (var entry in timeline)
        {
            if (entry.IsEventRow)
            {
                var evt = entry.Event!;
                builder.AppendLine(string.Join(",",
                    CsvEscape(CaptureTimeFormatter.Format(evt.EventTime)),
                    CsvEscape(evt.RelatedFile),
                    CsvEscape("EVENT"),
                    CsvEscape(evt.EventType),
                    CsvEscape(evt.Severity.ToString().ToUpperInvariant()),
                    CsvEscape(evt.Message),
                    CsvEscape(evt.FromMode),
                    CsvEscape(evt.ToMode),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(""),
                    CsvEscape(entry.SerialNumber)));
                continue;
            }

            var obs = entry.Observation!;
            builder.AppendLine(string.Join(",",
                CsvEscape(CaptureTimeFormatter.Format(obs.CaptureInstant)),
                CsvEscape(obs.SourceFile),
                CsvEscape("CAPTURE"),
                CsvEscape(""),
                CsvEscape(""),
                CsvEscape(""),
                CsvEscape(""),
                CsvEscape(""),
                CsvEscape(DisplayExpectedMode(obs)),
                CsvEscape(obs.InferredMode),
                CsvEscape(obs.CopyrightMode),
                CsvEscape(obs.Artist),
                CsvEscape(obs.CopyrightText),
                CsvEscape(obs.CopyrightTimeHhmmss),
                CsvEscape(obs.CopyrightVoltage),
                CsvEscape(obs.CopyrightErrorDisplay),
                CsvEscape(obs.ScheduleMatch),
                CsvEscape(obs.LoggedScheduleMatch),
                CsvEscape(obs.LoggedInferredMatch),
                CsvEscape(DisplayShutterType(obs.ShutterMode)),
                CsvEscape(obs.ShutterSpeed),
                CsvEscape(obs.Iso),
                CsvEscape(obs.FlashExposureComp),
                CsvEscape(obs.SerialNumber)));
        }

        foreach (var error in ordered.Where(o => !o.IsReadable))
        {
            builder.AppendLine();
            builder.AppendLine($"--- ERROR: {error.SourceFile} ---");
            builder.AppendLine($"  {error.ReadError}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string DisplayExpectedMode(ImageObservation obs)
    {
        if (obs.ScheduleMatch == "MISMATCH" && obs.ExpectedMode is not "" and not "UNKNOWN")
        {
            return obs.ExpectedMode + "!";
        }

        return obs.ExpectedMode;
    }

    private static string DisplayShutterType(string shutterMode)
    {
        var mode = shutterMode.ToLowerInvariant();
        if (mode.Contains("electronic first curtain"))
        {
            return "EFCS";
        }

        if (mode.Contains("electronic"))
        {
            return "Electronic";
        }

        return "Unknown";
    }

    private static string FormatCompactTimestamp(DateTime? instant) =>
        CaptureTimeFormatter.Format(instant);

    private static string CsvEscape(string? value)
    {
        var text = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{text}\"";
    }
}
