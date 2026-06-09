using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class CaptureTimelineBuilder
{
    public static readonly TimeSpan ScheduledAwakeTolerance = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PreConfigWindowAfterAwake = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan PostConfigWindowAfterPreConfig = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CopyrightFreshnessWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan WakeCycleGap = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CaptureGapThreshold = TimeSpan.FromHours(3);
    public static readonly TimeSpan StaleCopyrightThreshold = TimeSpan.FromHours(4);

    public static IReadOnlyList<CaptureTimelineEntry> Build(IReadOnlyList<ImageObservation> ordered)
    {
        var timeline = new List<CaptureTimelineEntry>();

        foreach (var serialGroup in ObservationOrdering.GroupBySerialInOrder(ordered))
        {
            var readable = serialGroup
                .Where(o => o.IsReadable && o.CaptureInstant is not null)
                .ToList();
            if (readable.Count == 0)
            {
                continue;
            }

            timeline.AddRange(BuildSerialTimeline(readable[0].SerialNumber, readable));
        }

        return timeline;
    }

    private static IEnumerable<CaptureTimelineEntry> BuildSerialTimeline(
        string serialNumber,
        IReadOnlyList<ImageObservation> readable)
    {
        var entries = new List<CaptureTimelineEntry>();

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            entries.Add(CreateEvent(
                serialNumber,
                "Camera Session",
                AnalysisEventSeverity.Info,
                readable[0].CaptureInstant,
                $"Analysis begins for camera serial {serialNumber}.",
                relatedFile: readable[0].SourceFile));
        }

        ImageObservation? previous = null;
        foreach (var obs in readable)
        {
            if (previous is not null)
            {
                entries.AddRange(BuildBetweenObservations(serialNumber, previous, obs));
            }

            if (obs.HasConfigError)
            {
                entries.Add(CreateEvent(
                    serialNumber,
                    "Config Error",
                    AnalysisEventSeverity.Error,
                    obs.CaptureInstant,
                    $"Firmware reported {CameraErrorDecoder.FormatErrSegment(obs.CopyrightError)}.",
                    relatedFile: obs.SourceFile));
            }

            entries.Add(CaptureTimelineEntry.FromObservation(obs));
            previous = obs;
        }

        entries.AddRange(BuildWakeCycleEvents(serialNumber, readable));

        return SortSerialTimelineEntries(entries);
    }

    private static List<CaptureTimelineEntry> SortSerialTimelineEntries(IEnumerable<CaptureTimelineEntry> entries) =>
        entries
            .OrderBy(e => e.SortTime)
            .ThenBy(e => e.SortOrder)
            .ThenBy(e => e.IsEventRow)
            .ThenBy(e => e.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<CaptureTimelineEntry> BuildBetweenObservations(
        string serialNumber,
        ImageObservation previous,
        ImageObservation current)
    {
        var events = new List<CaptureTimelineEntry>();
        var prevTime = previous.CaptureInstant!.Value;
        var currTime = current.CaptureInstant!.Value;

        if (!string.Equals(previous.Artist, current.Artist, StringComparison.Ordinal))
        {
            var atScheduledWake = ScheduleAnalyzer.IsNearScheduledAwake(
                currTime,
                previous.Artist,
                ScheduledAwakeTolerance);
            events.Add(CreateEvent(
                serialNumber,
                "Artist Change",
                atScheduledWake ? AnalysisEventSeverity.Note : AnalysisEventSeverity.Info,
                currTime,
                atScheduledWake
                    ? "Artist schedule string updated at scheduled wake (expected)."
                    : $"Artist schedule string changed to: {current.Artist}",
                relatedFile: current.SourceFile));
        }

        var gap = currTime - prevTime;
        if (gap >= CaptureGapThreshold)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Capture Gap",
                AnalysisEventSeverity.Note,
                currTime,
                $"No captures for {gap.TotalHours:F1} hours before this file.",
                relatedFile: current.SourceFile));
        }

        if (string.Equals(previous.Artist, current.Artist, StringComparison.Ordinal))
        {
            foreach (var (nominalDt, transitionMode) in ScheduleAnalyzer.IterNominalScheduleTransitionsBetween(
                         prevTime,
                         currTime,
                         current.Artist))
            {
                var effectiveDt = ScheduleAnalyzer.NominalToEffectiveInstant(nominalDt);
                var priorMode = PreviousScheduleMode(current.Artist, nominalDt);
                events.Add(CreateEvent(
                    serialNumber,
                    "Schedule Transition",
                    AnalysisEventSeverity.Info,
                    nominalDt,
                    $"Scheduled transition to {transitionMode} ({ScheduleAnalyzer.ModeLabel(transitionMode)}).",
                    fromMode: priorMode,
                    toMode: transitionMode));

                var coverageIndex = ScheduleAnalyzer.BuildTransitionCoverageIndex([previous, current]);
                var (preOk, postOk) = ScheduleAnalyzer.EvaluateTransitionCoverage(
                    coverageIndex,
                    current.Artist,
                    effectiveDt);
                if (!preOk)
                {
                    events.Add(CreateEvent(
                        serialNumber,
                        "Missing Pre-Transition Shots",
                        AnalysisEventSeverity.Error,
                        nominalDt,
                        "No capture in the 60s before the scheduled mode-effective transition.",
                        fromMode: priorMode,
                        toMode: transitionMode));
                }

                if (!postOk)
                {
                    events.Add(CreateEvent(
                        serialNumber,
                        "Missing Post-Transition Shots",
                        AnalysisEventSeverity.Error,
                        nominalDt,
                        "No capture in the post-transition window (45s nominal padding through 60s after mode-effective time).",
                        fromMode: priorMode,
                        toMode: transitionMode));
                }
            }
        }

        var isScheduledModeChange = IsScheduledModeChangeContext(previous, current);
        var inferredChanged = previous.InferredMode != current.InferredMode &&
                              previous.InferredMode is not ("UNKNOWN" or "C2/C3?" or "") &&
                              current.InferredMode is not ("UNKNOWN" or "C2/C3?" or "");
        var loggedChanged = !string.IsNullOrWhiteSpace(previous.CopyrightMode) &&
                            !string.IsNullOrWhiteSpace(current.CopyrightMode) &&
                            previous.CopyrightMode != "UNKNOWN" &&
                            current.CopyrightMode != "UNKNOWN" &&
                            previous.CopyrightMode != current.CopyrightMode;

        if (isScheduledModeChange && (inferredChanged || loggedChanged))
        {
            var messageParts = new List<string>();
            if (inferredChanged)
            {
                messageParts.Add($"Inferred {previous.InferredMode}→{current.InferredMode}");
            }

            if (loggedChanged)
            {
                messageParts.Add($"Logged {previous.CopyrightMode}→{current.CopyrightMode}");
            }

            events.Add(CreateEvent(
                serialNumber,
                "Mode Change",
                AnalysisEventSeverity.Info,
                currTime,
                string.Join("; ", messageParts) + " at scheduled transition.",
                fromMode: inferredChanged ? previous.InferredMode : previous.CopyrightMode,
                toMode: inferredChanged ? current.InferredMode : current.CopyrightMode,
                relatedFile: current.SourceFile));
        }
        else
        {
            var copyrightLogWriteFailure = ScheduleAnalyzer.IsCopyrightLogWriteFailure(current);
            if (inferredChanged && !copyrightLogWriteFailure)
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Manual Override",
                    AnalysisEventSeverity.Error,
                    currTime,
                    "Inferred mode changed without a scheduled transition.",
                    fromMode: previous.InferredMode,
                    toMode: current.InferredMode,
                    relatedFile: current.SourceFile));
            }

            if (loggedChanged && !copyrightLogWriteFailure)
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Manual Override",
                    AnalysisEventSeverity.Error,
                    currTime,
                    "Logged mode changed without a scheduled transition.",
                    fromMode: previous.CopyrightMode,
                    toMode: current.CopyrightMode,
                    relatedFile: current.SourceFile));
            }
        }

        var isCopyrightLogWriteFailure = ScheduleAnalyzer.IsCopyrightLogWriteFailure(current);
        var isFirstCopyrightLogWriteFailure =
            isCopyrightLogWriteFailure && !ScheduleAnalyzer.IsCopyrightLogWriteFailure(previous);
        if (isFirstCopyrightLogWriteFailure)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Copyright Log Failed",
                AnalysisEventSeverity.Warning,
                currTime,
                $"Copyright stamp still reads {current.CopyrightMode} but camera mode is {current.InferredMode} (matches schedule). Mode change likely succeeded; EXIF copyright write may have failed.",
                fromMode: current.CopyrightMode,
                toMode: current.InferredMode,
                relatedFile: current.SourceFile));

            if (inferredChanged &&
                current.InferredMode == current.ExpectedMode &&
                previous.InferredMode != current.InferredMode)
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Mode Change",
                    AnalysisEventSeverity.Info,
                    currTime,
                    $"Inferred {previous.InferredMode}→{current.InferredMode} at scheduled transition; copyright log not updated.",
                    fromMode: previous.InferredMode,
                    toMode: current.InferredMode,
                    relatedFile: current.SourceFile));
            }
        }
        else if (current.LoggedScheduleMatch == "MISMATCH" && !isCopyrightLogWriteFailure)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Manual Override",
                AnalysisEventSeverity.Error,
                currTime,
                $"Logged mode {current.CopyrightMode} does not match expected schedule mode {current.ExpectedMode}.",
                fromMode: current.CopyrightMode,
                toMode: current.ExpectedMode,
                relatedFile: current.SourceFile));
        }

        return events;
    }

    private static IEnumerable<CaptureTimelineEntry> BuildWakeCycleEvents(
        string serialNumber,
        IReadOnlyList<ImageObservation> readable)
    {
        var events = new List<CaptureTimelineEntry>();
        var isFirstCycleForSerial = true;

        foreach (var cycle in GroupWakeCycles(readable))
        {
            if (cycle.Count == 0)
            {
                continue;
            }

            var first = cycle[0];
            var firstTime = first.CaptureInstant!.Value;
            var priorCapture = FindPriorCapture(readable, first);

            var preConfig = first;
            var postConfig = TryIdentifyWakePostConfig(cycle);
            var lastPreConfig = postConfig is null
                ? cycle[^1]
                : cycle[Math.Max(0, cycle.IndexOf(postConfig) - 1)];

            var context = new WakeCycleContext(
                PreConfig: preConfig,
                PostConfig: postConfig,
                LastPreConfig: lastPreConfig,
                PriorCapture: priorCapture,
                IsFirstCycleForSerial: isFirstCycleForSerial,
                FirstCaptureTime: firstTime);

            isFirstCycleForSerial = false;

            if (!LooksLikeAgentWake(context))
            {
                continue;
            }

            var alignedAwake = FindAlignedScheduledAwake(firstTime, first.Artist);
            var isScheduledAligned = alignedAwake is not null;
            var isOffSchedule = !ScheduleAnalyzer.IsNearScheduledAwake(
                firstTime,
                first.Artist,
                ScheduledAwakeTolerance);

            if (isScheduledAligned)
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Scheduled Wake",
                    AnalysisEventSeverity.Info,
                    firstTime,
                    FormatScheduledWakeMessage(alignedAwake!.Value, first.Artist, preConfig, postConfig),
                    relatedFile: preConfig.SourceFile));
                events.AddRange(BuildScheduledWakeValidation(
                    serialNumber,
                    preConfig,
                    lastPreConfig,
                    postConfig,
                    alignedAwake!.Value));
            }
            else if (!isOffSchedule)
            {
                var nearestAwake = FindNearestScheduledAwake(firstTime, first.Artist);
                events.Add(CreateEvent(
                    serialNumber,
                    "Scheduled Wake",
                    AnalysisEventSeverity.Note,
                    firstTime,
                    nearestAwake is null
                        ? "Near Artist wake time with updated copyright, but no matching wake slot was found."
                        : $"Near Artist wake {nearestAwake:HH:mm} but pre-config capture outside the 0-45s after-wake window.",
                    relatedFile: preConfig.SourceFile));
                if (nearestAwake is not null)
                {
                    events.AddRange(BuildScheduledWakeValidation(
                        serialNumber,
                        preConfig,
                        lastPreConfig,
                        postConfig,
                        nearestAwake.Value));
                }
            }
            else if (IsHighConfidenceInitialization(context))
            {
                var initObservation = postConfig ?? preConfig;
                events.Add(CreateEvent(
                    serialNumber,
                    "Initialization",
                    AnalysisEventSeverity.Info,
                    initObservation.CaptureInstant,
                    "Initialization: off-schedule wake with stale/missing prior copyright or long capture gap.",
                    fromMode: preConfig.InferredMode,
                    toMode: initObservation.CopyrightMode,
                    relatedFile: initObservation.SourceFile));
                events.AddRange(BuildPreConfigNotes(serialNumber, preConfig, initObservation, isInitialization: true));
            }
            else
            {
                var wakeObservation = postConfig ?? preConfig;
                events.Add(CreateEvent(
                    serialNumber,
                    "Unscheduled Wake",
                    AnalysisEventSeverity.Note,
                    wakeObservation.CaptureInstant,
                    "Unscheduled wake with copyright update; not aligned to Artist wake times (manual/test?).",
                    fromMode: preConfig.InferredMode,
                    toMode: wakeObservation.CopyrightMode,
                    relatedFile: wakeObservation.SourceFile));
                events.AddRange(BuildPreConfigNotes(serialNumber, preConfig, wakeObservation, isInitialization: false));
            }
        }

        return events;
    }

    private static IEnumerable<CaptureTimelineEntry> BuildScheduledWakeValidation(
        string serialNumber,
        ImageObservation preConfig,
        ImageObservation lastPreConfig,
        ImageObservation? postConfig,
        DateTime alignedAwake)
    {
        var events = new List<CaptureTimelineEntry>();
        var firstTime = preConfig.CaptureInstant!.Value;
        var lastPreTime = lastPreConfig.CaptureInstant!.Value;

        if (firstTime - alignedAwake > PreConfigWindowAfterAwake)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Timing Violation",
                AnalysisEventSeverity.Error,
                firstTime,
                $"Pre-config capture was {(firstTime - alignedAwake).TotalSeconds:F0}s after scheduled wake (limit {PreConfigWindowAfterAwake.TotalSeconds:F0}s).",
                relatedFile: preConfig.SourceFile));
        }

        if (postConfig is null)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Timing Violation",
                AnalysisEventSeverity.Error,
                firstTime,
                "Scheduled wake missing post-config reference capture within 30s of pre-config shot.",
                relatedFile: preConfig.SourceFile));
        }
        else if (postConfig.CaptureInstant!.Value - lastPreTime > PostConfigWindowAfterPreConfig)
        {
            events.Add(CreateEvent(
                serialNumber,
                "Timing Violation",
                AnalysisEventSeverity.Error,
                postConfig.CaptureInstant,
                $"Post-config capture was {(postConfig.CaptureInstant.Value - lastPreTime).TotalSeconds:F0}s after last pre-config shot (limit {PostConfigWindowAfterPreConfig.TotalSeconds:F0}s).",
                relatedFile: postConfig.SourceFile));
        }

        if (postConfig is not null)
        {
            var postCaptureInstant = postConfig.CaptureInstant!.Value;
            if (postConfig.HasMissingCopyrightLog)
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Transition Failure",
                    AnalysisEventSeverity.Error,
                    postConfig.CaptureInstant,
                    "Post-config reference capture has no copyright log entry after scheduled wake.",
                    relatedFile: postConfig.SourceFile));
            }
            else if (!ScheduleAnalyzer.IsCopyrightFresh(
                         postCaptureInstant,
                         postConfig.CopyrightTimeHhmmss,
                         CopyrightFreshnessWindow))
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Transition Failure",
                    AnalysisEventSeverity.Error,
                    postConfig.CaptureInstant,
                    $"Post-config reference capture has stale copyright log ({postConfig.CopyrightTimeHhmmss}); expected fresh stamp.",
                    relatedFile: postConfig.SourceFile));
            }
            else if (postConfig.LoggedScheduleMatch == "MISMATCH")
            {
                events.Add(CreateEvent(
                    serialNumber,
                    "Transition Failure",
                    AnalysisEventSeverity.Error,
                    postConfig.CaptureInstant,
                    $"Post-config reference capture logged {postConfig.CopyrightMode} but schedule expected {postConfig.ExpectedMode}.",
                    fromMode: postConfig.CopyrightMode,
                    toMode: postConfig.ExpectedMode,
                    relatedFile: postConfig.SourceFile));
            }
        }

        events.AddRange(BuildPreConfigNotes(serialNumber, preConfig, postConfig ?? preConfig, isInitialization: false));
        return events;
    }

    private static IEnumerable<CaptureTimelineEntry> BuildPreConfigNotes(
        string serialNumber,
        ImageObservation preConfig,
        ImageObservation referenceObservation,
        bool isInitialization)
    {
        if (preConfig.ScheduleMatch != "MISMATCH")
        {
            return [];
        }

        var contextHint = isInitialization
            ? "common on first setup"
            : "before scheduled init";
        var message = preConfig == referenceObservation
            ? $"Capture inferred {preConfig.InferredMode}; expected {preConfig.ExpectedMode}."
            : $"Pre-config capture inferred {preConfig.InferredMode} before wake; expected {preConfig.ExpectedMode} ({contextHint}).";

        return
        [
            CreateEvent(
                serialNumber,
                "Pre-Config Mode Mismatch",
                AnalysisEventSeverity.Note,
                preConfig.CaptureInstant,
                message,
                fromMode: preConfig.InferredMode,
                toMode: preConfig.ExpectedMode,
                relatedFile: preConfig.SourceFile),
        ];
    }

    private static bool LooksLikeAgentWake(WakeCycleContext context)
    {
        var pre = context.PreConfig;
        var post = context.PostConfig;

        if (post is not null)
        {
            if (CopyrightTextUnchanged(pre, post))
            {
                return false;
            }

            if (pre.HasMissingCopyrightLog && post.HasMissingCopyrightLog)
            {
                return false;
            }

            if (!post.HasMissingCopyrightLog &&
                ScheduleAnalyzer.IsCopyrightFresh(
                    post.CaptureInstant!.Value,
                    post.CopyrightTimeHhmmss,
                    CopyrightFreshnessWindow))
            {
                return true;
            }

            return !CopyrightTextUnchanged(pre, post);
        }

        if (pre.HasMissingCopyrightLog ||
            pre.CaptureInstant is null ||
            !ScheduleAnalyzer.IsCopyrightFresh(
                pre.CaptureInstant.Value,
                pre.CopyrightTimeHhmmss,
                CopyrightFreshnessWindow))
        {
            return false;
        }

        if (context.PriorCapture is null)
        {
            return true;
        }

        return !CopyrightTextUnchanged(context.PriorCapture, pre);
    }

    private static bool IsHighConfidenceInitialization(WakeCycleContext context)
    {
        if (context.IsFirstCycleForSerial)
        {
            return true;
        }

        if (context.PreConfig.HasMissingCopyrightLog)
        {
            return true;
        }

        if (context.PriorCapture?.CaptureInstant is not null &&
            context.FirstCaptureTime - context.PriorCapture.CaptureInstant.Value >= CaptureGapThreshold)
        {
            return true;
        }

        return TryGetCopyrightAgeBeforeCapture(context.PreConfig, out var age) &&
               age >= StaleCopyrightThreshold;
    }

    private static bool CopyrightTextUnchanged(ImageObservation left, ImageObservation right) =>
        string.Equals(
            left.CopyrightText.Trim(),
            right.CopyrightText.Trim(),
            StringComparison.Ordinal);

    private static bool TryGetCopyrightAgeBeforeCapture(ImageObservation observation, out TimeSpan age)
    {
        age = default;
        if (observation.CaptureInstant is null ||
            observation.HasMissingCopyrightLog ||
            !ScheduleAnalyzer.TryParseCopyrightInstant(
                DateOnly.FromDateTime(observation.CaptureInstant.Value),
                observation.CopyrightTimeHhmmss,
                out var copyrightInstant))
        {
            return false;
        }

        age = observation.CaptureInstant.Value - copyrightInstant;
        if (age < TimeSpan.Zero)
        {
            age += TimeSpan.FromDays(1);
        }

        return true;
    }

    private static DateTime? FindAlignedScheduledAwake(DateTime firstCapture, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var date = DateOnly.FromDateTime(firstCapture);
        foreach (var awake in ScheduleAnalyzer.ScheduledAwakeInstantsOnDate(date, artist)
                     .Concat(ScheduleAnalyzer.ScheduledAwakeInstantsOnDate(date.AddDays(-1), artist)))
        {
            if (firstCapture >= awake && firstCapture <= awake + PreConfigWindowAfterAwake)
            {
                return awake;
            }
        }

        return null;
    }

    private static DateTime? FindNearestScheduledAwake(DateTime firstCapture, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var date = DateOnly.FromDateTime(firstCapture);
        var candidate = ScheduleAnalyzer.ScheduledAwakeInstantsOnDate(date, artist)
            .Concat(ScheduleAnalyzer.ScheduledAwakeInstantsOnDate(date.AddDays(-1), artist))
            .OrderBy(awake => Math.Abs((firstCapture - awake).TotalSeconds))
            .FirstOrDefault();

        return candidate == default ? null : candidate;
    }

    private static ImageObservation? FindPriorCapture(
        IReadOnlyList<ImageObservation> readable,
        ImageObservation current)
    {
        var index = -1;
        for (var i = 0; i < readable.Count; i++)
        {
            if (ReferenceEquals(readable[i], current))
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
        {
            return null;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            var candidate = readable[i];
            if (string.Equals(candidate.SerialNumber, current.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record WakeCycleContext(
        ImageObservation PreConfig,
        ImageObservation? PostConfig,
        ImageObservation LastPreConfig,
        ImageObservation? PriorCapture,
        bool IsFirstCycleForSerial,
        DateTime FirstCaptureTime);

    private static List<List<ImageObservation>> GroupWakeCycles(IReadOnlyList<ImageObservation> readable)
    {
        var cycles = new List<List<ImageObservation>>();
        List<ImageObservation>? current = null;

        foreach (var obs in readable)
        {
            if (current is null ||
                obs.CaptureInstant!.Value - current[^1].CaptureInstant!.Value > WakeCycleGap ||
                !string.Equals(obs.SerialNumber, current[^1].SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                current = [obs];
                cycles.Add(current);
            }
            else
            {
                current.Add(obs);
            }
        }

        return cycles;
    }

    private static bool HadScheduleTransitionBetween(DateTime start, DateTime end, string artist) =>
        ScheduleAnalyzer.IterNominalScheduleTransitionsBetween(start, end, artist).Count > 0;

    private static bool IsScheduledModeChangeContext(ImageObservation previous, ImageObservation current)
    {
        var prevTime = previous.CaptureInstant!.Value;
        var currTime = current.CaptureInstant!.Value;
        var artist = current.Artist;

        if (HadScheduleTransitionBetween(prevTime, currTime, artist))
        {
            return true;
        }

        if (IsWakeCompletionModeChange(prevTime, currTime, artist))
        {
            return true;
        }

        return IsCoverageSpanModeChange(prevTime, currTime, artist);
    }

    private static bool IsWakeCompletionModeChange(DateTime prevTime, DateTime currTime, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist) || currTime <= prevTime)
        {
            return false;
        }

        if (currTime - prevTime > PostConfigWindowAfterPreConfig)
        {
            return false;
        }

        var searchStart = prevTime.AddHours(-6);
        foreach (var (nominalDt, _) in ScheduleAnalyzer.IterNominalScheduleTransitionsBetween(searchStart, currTime, artist))
        {
            if (nominalDt <= prevTime &&
                prevTime <= nominalDt + PreConfigWindowAfterAwake)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCoverageSpanModeChange(DateTime prevTime, DateTime currTime, string artist)
    {
        if (string.IsNullOrWhiteSpace(artist) || currTime <= prevTime)
        {
            return false;
        }

        var searchStart = prevTime.AddHours(-6);
        foreach (var (nominalDt, _) in ScheduleAnalyzer.IterNominalScheduleTransitionsBetween(searchStart, currTime, artist))
        {
            var effectiveDt = ScheduleAnalyzer.NominalToEffectiveInstant(nominalDt);
            if (ScheduleAnalyzer.IsCaptureInPreTransitionWindow(prevTime, effectiveDt) &&
                ScheduleAnalyzer.IsCaptureInPostTransitionWindow(currTime, effectiveDt))
            {
                return true;
            }
        }

        return false;
    }

    private static ImageObservation? TryIdentifyWakePostConfig(IReadOnlyList<ImageObservation> cycle)
    {
        if (cycle.Count < 2)
        {
            return null;
        }

        var preBurstReference = cycle[0];
        for (var i = 1; i < cycle.Count; i++)
        {
            var candidate = cycle[i];
            var lastPre = cycle[i - 1];
            if (candidate.CaptureInstant!.Value - lastPre.CaptureInstant!.Value > PostConfigWindowAfterPreConfig)
            {
                break;
            }

            if (LooksLikePostConfigShot(preBurstReference, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool LooksLikePostConfigShot(ImageObservation preBurstReference, ImageObservation candidate)
    {
        if (CopyrightTextUnchanged(preBurstReference, candidate) &&
            preBurstReference.InferredMode == candidate.InferredMode &&
            preBurstReference.CopyrightMode == candidate.CopyrightMode)
        {
            return false;
        }

        if (preBurstReference.HasMissingCopyrightLog && candidate.HasMissingCopyrightLog)
        {
            return false;
        }

        var inferredChanged = preBurstReference.InferredMode != candidate.InferredMode &&
                              preBurstReference.InferredMode is not ("UNKNOWN" or "C2/C3?" or "") &&
                              candidate.InferredMode is not ("UNKNOWN" or "C2/C3?" or "");
        var loggedChanged = !string.IsNullOrWhiteSpace(preBurstReference.CopyrightMode) &&
                            !string.IsNullOrWhiteSpace(candidate.CopyrightMode) &&
                            preBurstReference.CopyrightMode != "UNKNOWN" &&
                            candidate.CopyrightMode != "UNKNOWN" &&
                            preBurstReference.CopyrightMode != candidate.CopyrightMode;
        if (inferredChanged || loggedChanged)
        {
            return true;
        }

        if (!candidate.HasMissingCopyrightLog &&
            candidate.CaptureInstant is not null &&
            ScheduleAnalyzer.IsCopyrightFresh(
                candidate.CaptureInstant.Value,
                candidate.CopyrightTimeHhmmss,
                CopyrightFreshnessWindow) &&
            !CopyrightTextUnchanged(preBurstReference, candidate))
        {
            return true;
        }

        return !CopyrightTextUnchanged(preBurstReference, candidate);
    }

    private static string FormatScheduledWakeMessage(
        DateTime alignedAwake,
        string artist,
        ImageObservation preConfig,
        ImageObservation? postConfig)
    {
        var targetMode = ScheduledModeAtAwake(alignedAwake, artist);
        if (string.IsNullOrEmpty(targetMode))
        {
            targetMode = (postConfig ?? preConfig).CopyrightMode.ToLowerInvariant();
        }

        var preStem = FileStem(preConfig.SourceFile);
        if (postConfig is null || ReferenceEquals(preConfig, postConfig))
        {
            return $"Scheduled {alignedAwake:HH:mm}: to {targetMode}: Pre-config {preStem}";
        }

        var postStem = FileStem(postConfig.SourceFile);
        return $"Scheduled {alignedAwake:HH:mm}: to {targetMode}: Pre-config {preStem} post-config {postStem}";
    }

    private static string ScheduledModeAtAwake(DateTime alignedAwake, string artist)
    {
        var minuteOfDay = alignedAwake.Hour * 60 + alignedAwake.Minute;
        foreach (var (slotMinute, mode) in ScheduleAnalyzer.ParseArtistSchedule(artist))
        {
            if (slotMinute == minuteOfDay)
            {
                return mode.ToLowerInvariant();
            }
        }

        return "";
    }

    private static string FileStem(string sourceFile) =>
        Path.GetFileNameWithoutExtension(sourceFile);

    private static string PreviousScheduleMode(string artist, DateTime nominalTransitionTime)
    {
        var prior = nominalTransitionTime.AddSeconds(-1);
        return ScheduleAnalyzer.ExpectedModeForCapture(prior, artist);
    }

    private static int EventSortOrder(string eventType) => eventType switch
    {
        "Schedule Transition" => 0,
        "Scheduled Wake" => 12,
        "Mode Change" => 12,
        "Copyright Log Failed" => 12,
        "Manual Override" => 12,
        _ => 5,
    };

    private static CaptureTimelineEntry CreateEvent(
        string serialNumber,
        string eventType,
        AnalysisEventSeverity severity,
        DateTime? eventTime,
        string message,
        string? fromMode = null,
        string? toMode = null,
        string? relatedFile = null) =>
        CaptureTimelineEntry.FromEvent(
            new AnalysisEvent
            {
                EventType = eventType,
                Severity = severity,
                EventTime = eventTime,
                Message = message,
                FromMode = fromMode,
                ToMode = toMode,
                RelatedFile = relatedFile,
            },
            serialNumber,
            EventSortOrder(eventType));
}
