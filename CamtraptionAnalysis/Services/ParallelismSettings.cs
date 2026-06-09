namespace CamtraptionAnalysis.Services;

public static class ParallelismSettings
{
    /// <summary>
    /// Metadata extraction worker count: logical processors minus 25% (minimum 1).
    /// </summary>
    public static int MetadataWorkerCount =>
        Math.Max(1, (int)Math.Floor(Environment.ProcessorCount * 0.75));
}
