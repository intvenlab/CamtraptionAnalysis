using System.Globalization;
using System.Text.RegularExpressions;

namespace CamtraptionAnalysis.Services;

public static partial class CaptureTimeParser
{
    private static readonly string[] Formats =
    [
        "yyyy:MM:dd HH:mm:ss.FFFFFFF",
        "yyyy:MM:dd HH:mm:ss.FFF",
        "yyyy:MM:dd HH:mm:ss.FF",
        "yyyy:MM:dd HH:mm:ss.F",
        "yyyy:MM:dd HH:mm:ss",
    ];

    [GeneratedRegex(@"[+-]\d{2}:\d{2}$")]
    private static partial Regex TimezoneSuffixPattern();

    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (TimezoneSuffixPattern().IsMatch(normalized))
        {
            normalized = normalized[..^3] + normalized[^2..];
        }

        string? subSecSuffix = null;
        var dotIndex = normalized.LastIndexOf('.');
        if (dotIndex > normalized.LastIndexOf(' ') && dotIndex + 1 < normalized.Length)
        {
            var fraction = normalized[(dotIndex + 1)..];
            if (fraction.All(char.IsDigit))
            {
                subSecSuffix = fraction;
                normalized = normalized[..dotIndex];
            }
        }

        if (DateTime.TryParseExact(
                normalized,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return subSecSuffix is null
                ? parsed
                : CaptureTimeFormatter.ApplyExifSubseconds(parsed, subSecSuffix);
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return subSecSuffix is null
                ? parsed
                : CaptureTimeFormatter.ApplyExifSubseconds(parsed, subSecSuffix);
        }

        return null;
    }
}
