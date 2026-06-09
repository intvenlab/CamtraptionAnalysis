namespace CamtraptionAnalysis.Services;

public static class JpegFileEnumerator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
    };

    public static IEnumerable<string> EnumerateStreaming(string rootPath)
    {
        return Directory
            .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)));
    }

    public static IReadOnlyList<string> Enumerate(string rootPath, int maxFiles)
    {
        if (maxFiles <= 0)
        {
            return Array.Empty<string>();
        }

        return EnumerateStreaming(rootPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();
    }

    public static int CountAll(string rootPath)
    {
        return Directory
            .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Count(path => AllowedExtensions.Contains(Path.GetExtension(path)));
    }
}
