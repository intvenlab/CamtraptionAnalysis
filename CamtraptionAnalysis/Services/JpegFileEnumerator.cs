namespace CamtraptionAnalysis.Services;

public static class JpegFileEnumerator
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2",
        ".cr3",
        ".arw",
    };

    public static IEnumerable<string> EnumerateStreaming(string rootPath, bool includeRawFiles = false)
    {
        return Directory
            .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(path => IsAllowedExtension(path, includeRawFiles));
    }

    public static IReadOnlyList<string> Enumerate(string rootPath, int maxFiles, bool includeRawFiles = false)
    {
        if (maxFiles <= 0)
        {
            return Array.Empty<string>();
        }

        return EnumerateStreaming(rootPath, includeRawFiles)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();
    }

    public static int CountAll(string rootPath, bool includeRawFiles = false)
    {
        return Directory
            .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Count(path => IsAllowedExtension(path, includeRawFiles));
    }

    private static bool IsAllowedExtension(string path, bool includeRawFiles)
    {
        var extension = Path.GetExtension(path);
        if (JpegExtensions.Contains(extension))
        {
            return true;
        }

        return includeRawFiles && RawExtensions.Contains(extension);
    }
}
