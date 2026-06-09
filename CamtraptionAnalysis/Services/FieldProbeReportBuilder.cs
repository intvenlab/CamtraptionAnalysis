using System.Text;
using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class FieldProbeReportBuilder
{
    private sealed record FieldCoverage(string Label, Func<ImageObservation, bool> HasField);

    private static readonly FieldCoverage[] CoverageFields =
    [
        new("SerialNumber", o => !string.IsNullOrWhiteSpace(o.SerialNumber)),
        new("Artist", o => !string.IsNullOrWhiteSpace(o.Artist)),
        new("Copyright", o => !string.IsNullOrWhiteSpace(o.CopyrightText)),
        new("CaptureTime", o => o.CaptureInstant is not null),
        new("ShutterSpeed", o => !string.IsNullOrWhiteSpace(o.ShutterSpeed)),
        new("ISO", o => !string.IsNullOrWhiteSpace(o.Iso)),
        new("ShutterMode", o => !string.IsNullOrWhiteSpace(o.ShutterMode)),
        new("FlashExposureComp", o => !string.IsNullOrWhiteSpace(o.FlashExposureComp)),
        new("ShutterSpeedRange", o => !string.IsNullOrWhiteSpace(o.ShutterSpeedRange)),
    ];

    public static string Build(
        string rootPath,
        int filesRequested,
        int filesFoundTotal,
        IReadOnlyList<ImageObservation> results)
    {
        var successful = results.Where(r => r.IsReadable).ToList();
        var errors = results.Count - successful.Count;
        var builder = new StringBuilder();

        builder.AppendLine("Camtraption Field Probe Report");
        builder.AppendLine("==============================");
        builder.AppendLine($"Root: {rootPath}");
        builder.AppendLine($"Files requested: {filesRequested}");
        builder.AppendLine($"Files found (all JPG/JPEG under root): {filesFoundTotal}");
        builder.AppendLine($"Files processed: {results.Count}");
        builder.AppendLine($"Files with read errors: {errors}");
        builder.AppendLine();

        builder.AppendLine("Field coverage (across successfully read files):");
        foreach (var field in CoverageFields)
        {
            var count = successful.Count(r => field.HasField(r));
            builder.AppendLine($"  {field.Label,-22}{count}/{successful.Count}");
        }

        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("  ShutterMode is decoded from Canon FileInfo index 23 via CanonFieldDecoder.");
        builder.AppendLine("  FlashExposureComp is decoded from ShotInfo with Canon EV conversion.");
        builder.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            builder.AppendLine($"--- File {i + 1}: {result.SourceFile} ---");
            if (result.ReadError is not null)
            {
                builder.AppendLine($"  ERROR: {result.ReadError}");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine($"  Path: {result.FilePath}");
            builder.AppendLine($"  SerialNumber: {FormatValue(result.SerialNumber)}");
            builder.AppendLine($"  Artist: {FormatValue(result.Artist)}");
            builder.AppendLine($"  Copyright: {FormatValue(result.CopyrightText)}");
            AppendCopyrightParsed(builder, result.CopyrightParsed);
            builder.AppendLine($"  CaptureTime: {FormatValue(result.CaptureInstant?.ToString("yyyy:MM:dd HH:mm:ss.FF"))}");
            builder.AppendLine($"  ShutterSpeed: {FormatValue(result.ShutterSpeed)}");
            builder.AppendLine($"  ISO: {FormatValue(result.Iso)}");
            builder.AppendLine($"  ShutterMode: {FormatValue(result.ShutterMode)}");
            builder.AppendLine($"  FlashExposureComp: {FormatValue(result.FlashExposureComp)}");
            builder.AppendLine($"  ShutterSpeedRange: {FormatValue(result.ShutterSpeedRange)}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendCopyrightParsed(StringBuilder builder, CopyrightStamp stamp)
    {
        if (!stamp.IsParsed && string.IsNullOrEmpty(stamp.Mode))
        {
            builder.AppendLine("  CopyrightParsed: (missing or malformed)");
            return;
        }

        if (!stamp.IsParsed)
        {
            builder.AppendLine($"  CopyrightParsed: mode={stamp.Mode} (malformed)");
            return;
        }

        builder.AppendLine(
            $"  CopyrightParsed: mode={stamp.Mode} time={stamp.TimeHhmmss} voltage={stamp.Voltage} error={CameraErrorDecoder.FormatErrorField(stamp.Error)}");
    }

    private static string FormatValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(missing)" : value;
}
