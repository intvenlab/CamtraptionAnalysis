using MetadataExtractor;
using MetadataExtractor.Formats.Exif.Makernotes;

namespace CamtraptionAnalysis.Services;

/// <summary>
/// Decodes Canon maker-note fields using ExifTool-documented layouts where
/// MetadataExtractor exposes raw values but does not decode them.
/// </summary>
public static class CanonFieldDecoder
{
    // Canon makernote tag 0x0093 (FileInfo array). ShutterMode is index 23 (ExifTool 12.33+).
    private const int TagFileInfoArray = CanonMakernoteDirectory.TagFileInfoArray;
    private const int FileInfoShutterModeIndex = 23;

    public static string? GetShutterMode(CanonMakernoteDirectory? canon)
    {
        if (canon is null || !canon.ContainsTag(TagFileInfoArray))
        {
            return null;
        }

        if (!TryReadFileInfoIndex(canon, FileInfoShutterModeIndex, out var raw))
        {
            return null;
        }

        return raw switch
        {
            0 => "Mechanical",
            1 => "Electronic First Curtain",
            2 => "Electronic",
            _ => $"Unknown ({raw})",
        };
    }

    public static string? GetFlashExposureComp(CanonMakernoteDirectory? canon)
    {
        if (canon is null)
        {
            return null;
        }

        if (!canon.ContainsTag(CanonMakernoteDirectory.ShotInfo.TagFlashExposureBracketing))
        {
            return null;
        }

        try
        {
            var raw = unchecked((short)canon.GetUInt16(CanonMakernoteDirectory.ShotInfo.TagFlashExposureBracketing));
            var stops = DecodeCanonEv(raw);
            return FormatEvFraction(stops);
        }
        catch (MetadataException)
        {
            return null;
        }
    }

    private static bool TryReadFileInfoIndex(CanonMakernoteDirectory canon, int index, out int value)
    {
        value = 0;
        try
        {
            var bytes = canon.GetByteArray(TagFileInfoArray);
            if (bytes is not null && bytes.Length > index)
            {
                value = bytes[index];
                return true;
            }
        }
        catch (MetadataException)
        {
            // Fall through to integer-array attempt.
        }

        try
        {
            var ints = canon.GetInt32Array(TagFileInfoArray);
            if (ints is not null && ints.Length > index)
            {
                value = ints[index];
                return true;
            }
        }
        catch (MetadataException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Canon hex-based EV (modulo 0x20) to real stops. Ported from ExifTool Canon.pm / MetadataExtractor.
    /// </summary>
    internal static double DecodeCanonEv(int raw)
    {
        var val = raw;
        var sign = 1;
        if (val < 0)
        {
            val = -val;
            sign = -1;
        }

        var frac = val & 0x1F;
        val -= frac;

        if (frac == 0x0C)
        {
            frac = 0x20 / 3;
        }
        else if (frac == 0x14)
        {
            frac = 0x40 / 3;
        }

        return sign * (val + frac) / 32.0;
    }

    internal static string FormatEvFraction(double stops)
    {
        if (Math.Abs(stops) < 0.001)
        {
            return "0";
        }

        var sign = stops < 0 ? "-" : "";
        var abs = Math.Abs(stops);

        if (Math.Abs(abs - Math.Round(abs)) < 0.01)
        {
            return sign + Math.Round(abs).ToString("0");
        }

        var thirds = abs * 3;
        if (Math.Abs(thirds - Math.Round(thirds)) < 0.08)
        {
            return Math.Round(thirds) switch
            {
                1 => sign + "1/3",
                2 => sign + "2/3",
                _ => stops.ToString("0.##"),
            };
        }

        return stops.ToString("0.##");
    }
}
