using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class ScheduleAnalyzer
{
    public const int ScheduleEffectiveDelaySeconds = 45;
    public const int TransitionCoverageWindowSeconds = 60;

    public static IReadOnlyList<(int MinuteOfDay, string Mode)> ParseArtistSchedule(string artist)
    {
        var schedule = new List<(int MinuteOfDay, string Mode)>();
        if (string.IsNullOrWhiteSpace(artist))
        {
            return schedule;
        }

        foreach (var part in artist.Split(','))
        {
            var item = part.Trim();
            if (string.IsNullOrEmpty(item) || !item.Contains(':'))
            {
                continue;
            }

            var colon = item.IndexOf(':');
            var hhmm = item[..colon].Trim();
            var mode = CopyrightStampParser.NormalizeModeToken(item[(colon + 1)..]);
            if (hhmm.Length != 4 ||
                !int.TryParse(hhmm[..2], out var hour) ||
                !int.TryParse(hhmm[2..], out var minute) ||
                hour is < 0 or > 23 ||
                minute is < 0 or > 59)
            {
                continue;
            }

            schedule.Add((hour * 60 + minute, mode));
        }

        schedule.Sort((a, b) => a.MinuteOfDay.CompareTo(b.MinuteOfDay));
        return schedule;
    }

    public static IReadOnlyList<(int SecondOfDay, string Mode)> ShiftedScheduleEntries(string artist)
    {
        var shifted = new List<(int SecondOfDay, string Mode)>();
        foreach (var (minuteOfDay, mode) in ParseArtistSchedule(artist))
        {
            var shiftedSeconds = (minuteOfDay * 60 + ScheduleEffectiveDelaySeconds) % (24 * 60 * 60);
            shifted.Add((shiftedSeconds, mode));
        }

        shifted.Sort((a, b) => a.SecondOfDay.CompareTo(b.SecondOfDay));
        return shifted;
    }

    public static string ExpectedModeForCapture(DateTime? captureTime, string artist)
    {
        var schedule = ShiftedScheduleEntries(artist);
        if (schedule.Count == 0)
        {
            return "UNKNOWN";
        }

        if (captureTime is null)
        {
            return "UNKNOWN";
        }

        var captureSeconds = CaptureSecondsOfDay(captureTime.Value);
        var chosenMode = schedule[^1].Mode;
        foreach (var (transitionSeconds, mode) in schedule)
        {
            if (captureSeconds >= transitionSeconds)
            {
                chosenMode = mode;
            }
            else
            {
                break;
            }
        }

        return chosenMode;
    }

    public static IReadOnlyList<(DateTime TransitionInstant, string Mode)> IterScheduleTransitionsBetween(
        DateTime startDt,
        DateTime endDt,
        string artist) =>
        IterNominalScheduleTransitionsBetween(startDt, endDt, artist)
            .Select(item => (NominalToEffectiveInstant(item.NominalInstant), item.Mode))
            .ToList();

    public static IReadOnlyList<(DateTime NominalInstant, string Mode)> IterNominalScheduleTransitionsBetween(
        DateTime startDt,
        DateTime endDt,
        string artist)
    {
        if (endDt <= startDt)
        {
            return Array.Empty<(DateTime, string)>();
        }

        var entries = ParseArtistSchedule(artist);
        if (entries.Count == 0)
        {
            return Array.Empty<(DateTime, string)>();
        }

        var transitions = new List<(DateTime, string)>();
        for (var day = startDt.Date; day <= endDt.Date; day = day.AddDays(1))
        {
            foreach (var (minuteOfDay, mode) in entries)
            {
                var nominalDt = day.AddMinutes(minuteOfDay);
                if (startDt < nominalDt && nominalDt <= endDt)
                {
                    transitions.Add((nominalDt, mode));
                }
            }
        }

        transitions.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return transitions;
    }

    public static DateTime NominalToEffectiveInstant(DateTime nominalInstant) =>
        nominalInstant.AddSeconds(ScheduleEffectiveDelaySeconds);

    public static string EvaluateScheduleMatch(string inferredMode, string expectedMode)
    {
        if (string.IsNullOrEmpty(expectedMode) || expectedMode == "UNKNOWN")
        {
            return "NO_SCHEDULE";
        }

        if (inferredMode is "UNKNOWN" or "C2/C3?")
        {
            return "INDETERMINATE";
        }

        return inferredMode == expectedMode ? "MATCH" : "MISMATCH";
    }

    public static string EvaluateLoggedScheduleMatch(string loggedMode, string expectedMode)
    {
        if (string.IsNullOrEmpty(expectedMode) || expectedMode == "UNKNOWN")
        {
            return "NO_SCHEDULE";
        }

        if (string.IsNullOrEmpty(loggedMode) || loggedMode == "UNKNOWN")
        {
            return "NO_LOGGED_MODE";
        }

        return loggedMode == expectedMode ? "MATCH" : "MISMATCH";
    }

    public static bool IsCopyrightLogWriteFailure(ImageObservation observation) =>
        observation.LoggedScheduleMatch == "MISMATCH" &&
        observation.ScheduleMatch == "MATCH" &&
        !string.IsNullOrWhiteSpace(observation.CopyrightMode) &&
        observation.CopyrightMode != "UNKNOWN" &&
        !string.IsNullOrWhiteSpace(observation.InferredMode) &&
        observation.InferredMode is not ("UNKNOWN" or "C2/C3?");

    public static string EvaluateLoggedInferredMatch(string loggedMode, string inferredMode)
    {
        if (string.IsNullOrEmpty(loggedMode) || loggedMode == "UNKNOWN")
        {
            return "NO_LOGGED_MODE";
        }

        if (inferredMode is "" or "UNKNOWN" or "C2/C3?")
        {
            return "INDETERMINATE";
        }

        return loggedMode == inferredMode ? "MATCH" : "MISMATCH";
    }

    public static Dictionary<(string Artist, DateOnly Date), List<DateTime>> BuildTransitionCoverageIndex(
        IEnumerable<ImageObservation> observations)
    {
        var index = new Dictionary<(string, DateOnly), List<DateTime>>();
        foreach (var obs in observations)
        {
            if (obs.CaptureInstant is null)
            {
                continue;
            }

            var key = (obs.Artist, DateOnly.FromDateTime(obs.CaptureInstant.Value));
            if (!index.TryGetValue(key, out var times))
            {
                times = [];
                index[key] = times;
            }

            times.Add(obs.CaptureInstant.Value);
        }

        foreach (var times in index.Values)
        {
            times.Sort();
        }

        return index;
    }

    public static bool IsCaptureInPreTransitionWindow(DateTime shotDt, DateTime effectiveTransitionDt) =>
        effectiveTransitionDt.AddSeconds(-TransitionCoverageWindowSeconds) <= shotDt &&
        shotDt < effectiveTransitionDt;

    public static bool IsCaptureInPostTransitionWindow(DateTime shotDt, DateTime effectiveTransitionDt)
    {
        var postWindowStart = effectiveTransitionDt.AddSeconds(-ScheduleEffectiveDelaySeconds);
        var postWindowEnd = effectiveTransitionDt.AddSeconds(TransitionCoverageWindowSeconds);
        return shotDt >= postWindowStart && shotDt <= postWindowEnd;
    }

    public static (bool PreOk, bool PostOk) EvaluateTransitionCoverage(
        Dictionary<(string Artist, DateOnly Date), List<DateTime>> coverageIndex,
        string artist,
        DateTime transitionDt)
    {
        if (!coverageIndex.TryGetValue((artist, DateOnly.FromDateTime(transitionDt)), out var shots) ||
            shots.Count == 0)
        {
            return (false, false);
        }

        var preOk = false;
        var postOk = false;
        foreach (var shotDt in shots)
        {
            if (IsCaptureInPreTransitionWindow(shotDt, transitionDt))
            {
                preOk = true;
            }

            if (IsCaptureInPostTransitionWindow(shotDt, transitionDt))
            {
                postOk = true;
            }

            if (preOk && postOk)
            {
                break;
            }
        }

        return (preOk, postOk);
    }

    public static IEnumerable<DateTime> ScheduledAwakeInstantsOnDate(DateOnly date, string artist)
    {
        foreach (var (minuteOfDay, _) in ParseArtistSchedule(artist))
        {
            yield return date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minuteOfDay)));
        }
    }

    public static bool IsNearScheduledAwake(DateTime instant, string artist, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return false;
        }

        var date = DateOnly.FromDateTime(instant);
        foreach (var awake in ScheduledAwakeInstantsOnDate(date, artist))
        {
            if (Math.Abs((instant - awake).TotalSeconds) <= tolerance.TotalSeconds)
            {
                return true;
            }
        }

        foreach (var awake in ScheduledAwakeInstantsOnDate(date.AddDays(-1), artist))
        {
            if (Math.Abs((instant - awake).TotalSeconds) <= tolerance.TotalSeconds)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParseCopyrightInstant(DateOnly date, string hhmmss, out DateTime copyrightInstant)
    {
        copyrightInstant = default;
        if (hhmmss.Length != 6 ||
            !int.TryParse(hhmmss[..2], out var hour) ||
            !int.TryParse(hhmmss[2..4], out var minute) ||
            !int.TryParse(hhmmss[4..], out var second) ||
            hour is < 0 or > 23 ||
            minute is < 0 or > 59 ||
            second is < 0 or > 59)
        {
            return false;
        }

        copyrightInstant = date.ToDateTime(new TimeOnly(hour, minute, second));
        return true;
    }

    public static bool IsCopyrightFresh(DateTime captureInstant, string copyrightHhmmss, TimeSpan maxAgeBeforeCapture)
    {
        if (!TryParseCopyrightInstant(DateOnly.FromDateTime(captureInstant), copyrightHhmmss, out var copyrightInstant))
        {
            return false;
        }

        var delta = captureInstant - copyrightInstant;
        return delta >= TimeSpan.FromSeconds(-5) && delta <= maxAgeBeforeCapture;
    }

    public static string ModeLabel(string mode) => mode switch
    {
        "C1" => "Daylight",
        "C2" => "Night",
        "C3" => "Twilight",
        _ => "Unknown",
    };

    private static int CaptureSecondsOfDay(DateTime captureTime) =>
        captureTime.Hour * 3600 + captureTime.Minute * 60 + captureTime.Second;
}
