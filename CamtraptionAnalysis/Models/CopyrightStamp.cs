namespace CamtraptionAnalysis.Models;

public sealed class CopyrightStamp
{
    public string Mode { get; init; } = "";
    public string TimeHhmmss { get; init; } = "";
    public string Voltage { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsParsed { get; init; }
}
