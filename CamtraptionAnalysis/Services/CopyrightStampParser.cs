using System.Text.RegularExpressions;

namespace CamtraptionAnalysis.Services;

public static partial class CopyrightStampParser
{
    [GeneratedRegex(
        @"^(?<mode>[A-Za-z0-9]+)\s+(?<time>\d{6})(?:\s+(?<voltage>\S+))?\s+ERR:(?<error>[A-Za-z0-9]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopyrightStampPattern();

    public static Models.CopyrightStamp Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Models.CopyrightStamp();
        }

        var match = CopyrightStampPattern().Match(value.Trim());
        if (!match.Success)
        {
            return new Models.CopyrightStamp { Mode = "UNKNOWN" };
        }

        return new Models.CopyrightStamp
        {
            Mode = NormalizeModeToken(match.Groups["mode"].Value),
            TimeHhmmss = match.Groups["time"].Value,
            Voltage = match.Groups["voltage"].Value.Trim(),
            Error = match.Groups["error"].Value.Trim(),
            IsParsed = true,
        };
    }

    public static string NormalizeModeToken(string token)
    {
        var text = token.Trim().ToUpperInvariant();
        return text switch
        {
            "AUTO" => "AUTO",
            "FV" => "FV",
            "P" => "P",
            "TV" => "TV",
            "AV" => "AV",
            "M" => "MANUAL",
            "MANUAL" => "MANUAL",
            "BULB" => "BULB",
            "CUSTOM" => "C1",
            _ => text,
        };
    }
}
