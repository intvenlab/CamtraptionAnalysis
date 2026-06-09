namespace CamtraptionAnalysis.Services;

public static class ReportFileWriter
{
    public static string ReportsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CamtraptionAnalysis");

    public static string SaveReport(string reportText, DateTime? timestamp = null)
    {
        var instant = timestamp ?? DateTime.Now;
        var directory = ReportsDirectory;
        Directory.CreateDirectory(directory);

        var fileName = instant.ToString("yyyy-MM-dd-HHmmss") + ".txt";
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllText(filePath, reportText);
        return filePath;
    }
}
