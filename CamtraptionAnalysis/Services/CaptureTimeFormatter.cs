using System.Globalization;

namespace CamtraptionAnalysis.Services;

public static class CaptureTimeFormatter
{
    public static string Format(DateTime? instant)
    {
        if (instant is null)
        {
            return "";
        }

        var value = RoundToCentiseconds(instant.Value);
        return value.ToString("yyyy-MM-dd HH:mm:ss.ff", CultureInfo.InvariantCulture);
    }

    private static DateTime RoundToCentiseconds(DateTime value)
    {
        var ticksPerCentisecond = TimeSpan.TicksPerSecond / 100;
        var roundedTicks = ((value.Ticks + ticksPerCentisecond / 2) / ticksPerCentisecond) * ticksPerCentisecond;
        return new DateTime(roundedTicks, value.Kind);
    }

    public static DateTime ApplyExifSubseconds(DateTime baseInstant, string? subSecRaw)
    {
        if (string.IsNullOrWhiteSpace(subSecRaw))
        {
            return baseInstant;
        }

        var digits = subSecRaw.Trim();
        if (!int.TryParse(digits, out var raw) || raw < 0)
        {
            return baseInstant;
        }

        var fractionalSeconds = raw / Math.Pow(10, digits.Length);
        return baseInstant.AddTicks((long)(fractionalSeconds * TimeSpan.TicksPerSecond));
    }
}
