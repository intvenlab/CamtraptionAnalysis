namespace CamtraptionAnalysis.Services;

public static class CameraErrorDecoder
{
    private static readonly IReadOnlyDictionary<int, string> ErrorNames = new Dictionary<int, string>
    {
        [1] = "CAMERA_ABSENT_OR_UNRESPONSIVE",
        [2] = "CAMERA_METADATA_INVALID",
        [3] = "CAMERA_CONFIG_APPLY_FAILED",
        [4] = "USB_TRANSPORT_FAILURE",
        [5] = "RTC_I2C_COMM_FAILURE",
        [6] = "RTC_DATA_INVALID",
        [7] = "RTC_SYNC_FAILURE",
        [8] = "ALARM_PROGRAM_FAILURE",
        [9] = "ALARM_VALIDATION_FAILURE",
        [10] = "GPIO_CONTROL_FAILURE",
        [11] = "OTHER_FAILURE",
    };

    public static bool IsNone(string? errorToken) =>
        string.IsNullOrWhiteSpace(errorToken) ||
        errorToken.Equals("None", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetName(int code, out string name) =>
        ErrorNames.TryGetValue(code, out name!);

    public static string FormatErrorField(string? errorToken)
    {
        if (IsNone(errorToken))
        {
            return "None";
        }

        if (int.TryParse(errorToken, out var code) && TryGetName(code, out var name))
        {
            return $"{code} ({name})";
        }

        return errorToken!.Trim();
    }

    public static string FormatErrSegment(string? errorToken)
    {
        if (IsNone(errorToken))
        {
            return "ERR:None";
        }

        return $"ERR:{FormatErrorField(errorToken)}";
    }

    public static string EnhanceCopyrightText(string? copyrightText, string? errorToken)
    {
        if (string.IsNullOrWhiteSpace(copyrightText) || IsNone(errorToken))
        {
            return copyrightText ?? "";
        }

        var rawError = errorToken!.Trim();
        var decodedSegment = FormatErrSegment(rawError);
        var marker = $"ERR:{rawError}";
        var index = copyrightText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return copyrightText[..index] + decodedSegment + copyrightText[(index + marker.Length)..];
        }

        return copyrightText;
    }
}
