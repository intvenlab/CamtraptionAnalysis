using CamtraptionAnalysis.Models;

namespace CamtraptionAnalysis.Services;

public static class ObservationOrdering
{
    public static string SerialSortKey(string? serialNumber) =>
        string.IsNullOrWhiteSpace(serialNumber) ? "\uFFFF" : serialNumber.Trim();

    public static IEnumerable<ImageObservation> SelectIncludedInReport(
        IEnumerable<ImageObservation> observations) =>
        observations.Where(o => o.IsAnalyzable || !o.IsReadable);

    public static IReadOnlyList<ImageObservation> SortBySerialThenCaptureTime(
        IEnumerable<ImageObservation> observations) =>
        SelectIncludedInReport(observations)
            .OrderBy(o => SerialSortKey(o.SerialNumber), StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.CaptureInstant ?? DateTime.MaxValue)
            .ThenBy(o => o.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IEnumerable<IReadOnlyList<ImageObservation>> GroupBySerialInOrder(
        IReadOnlyList<ImageObservation> orderedBySerialThenTime)
    {
        IReadOnlyList<ImageObservation>? currentGroup = null;
        string? currentSerialKey = null;

        foreach (var observation in orderedBySerialThenTime)
        {
            var serialKey = SerialSortKey(observation.SerialNumber);
            if (currentGroup is null ||
                !string.Equals(currentSerialKey, serialKey, StringComparison.OrdinalIgnoreCase))
            {
                if (currentGroup is not null)
                {
                    yield return currentGroup;
                }

                currentSerialKey = serialKey;
                currentGroup = new List<ImageObservation>();
            }

            ((List<ImageObservation>)currentGroup).Add(observation);
        }

        if (currentGroup is not null)
        {
            yield return currentGroup;
        }
    }
}
