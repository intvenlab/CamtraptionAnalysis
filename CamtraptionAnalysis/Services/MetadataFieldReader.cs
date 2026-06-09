using CamtraptionAnalysis.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataDirectory = MetadataExtractor.Directory;

namespace CamtraptionAnalysis.Services;

public sealed class MetadataFieldReader
{
    private static readonly string[] ShutterSpeedRangeTagNames =
    [
        "Shutter Speed Range",
        "ShutterSpeedRange",
    ];

    public ImageObservation Read(string filePath)
    {
        var sourceFile = Path.GetFileName(filePath);
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath).ToList();
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var canon = directories.OfType<CanonMakernoteDirectory>().FirstOrDefault();

            var artist = GetDescription(ifd0, ExifDirectoryBase.TagArtist) ?? "";
            var copyright = GetDescription(ifd0, ExifDirectoryBase.TagCopyright) ?? "";
            var captureTimeRaw = BuildCaptureTimeRaw(subIfd);

            return new ImageObservation
            {
                FilePath = filePath,
                SourceFile = sourceFile,
                SerialNumber = GetSerial(directories, subIfd) ?? "",
                Artist = artist,
                CopyrightText = copyright,
                CopyrightParsed = CopyrightStampParser.Parse(copyright),
                CaptureInstant = CaptureTimeParser.Parse(captureTimeRaw),
                ShutterSpeed = GetDescription(subIfd, ExifDirectoryBase.TagExposureTime) ?? "",
                Iso = GetIso(subIfd) ?? "",
                ShutterMode = CanonFieldDecoder.GetShutterMode(canon) ?? "",
                FlashExposureComp = CanonFieldDecoder.GetFlashExposureComp(canon) ?? "",
                ShutterSpeedRange = FindTagValue(directories, ShutterSpeedRangeTagNames) ?? "",
            };
        }
        catch (Exception ex)
        {
            return new ImageObservation
            {
                FilePath = filePath,
                SourceFile = sourceFile,
                ReadError = ex.Message,
            };
        }
    }

    private static string? GetDescription(MetadataDirectory? directory, int tagType)
    {
        if (directory is null || !directory.ContainsTag(tagType))
        {
            return null;
        }

        return directory.GetDescription(tagType);
    }

    private static string? GetIso(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null)
        {
            return null;
        }

        if (subIfd.ContainsTag(ExifDirectoryBase.TagIsoSpeed))
        {
            return subIfd.GetDescription(ExifDirectoryBase.TagIsoSpeed);
        }

        if (subIfd.ContainsTag(ExifDirectoryBase.TagIsoEquivalent))
        {
            return subIfd.GetDescription(ExifDirectoryBase.TagIsoEquivalent);
        }

        return null;
    }

    private static string? GetSerial(IReadOnlyList<MetadataDirectory> directories, ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is not null && subIfd.ContainsTag(ExifDirectoryBase.TagBodySerialNumber))
        {
            return subIfd.GetDescription(ExifDirectoryBase.TagBodySerialNumber);
        }

        return FindTagValue(
            directories,
            "Serial Number",
            "Internal Serial Number",
            "Body Serial Number");
    }

    private static string? BuildCaptureTimeRaw(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null || !subIfd.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal))
        {
            return null;
        }

        var dateTime = subIfd.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
        if (string.IsNullOrWhiteSpace(dateTime))
        {
            return null;
        }

        var subSec = GetSubsecondTime(subIfd);
        if (!string.IsNullOrWhiteSpace(subSec))
        {
            return $"{dateTime}.{subSec}";
        }

        return dateTime;
    }

    private static string? GetSubsecondTime(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null)
        {
            return null;
        }

        int[] tags =
        [
            ExifDirectoryBase.TagSubsecondTimeOriginal,
            ExifDirectoryBase.TagSubsecondTimeDigitized,
        ];

        foreach (var tag in tags)
        {
            if (!subIfd.ContainsTag(tag))
            {
                continue;
            }

            var subSec = subIfd.GetString(tag);
            if (!string.IsNullOrWhiteSpace(subSec))
            {
                return subSec.Trim();
            }
        }

        return null;
    }

    private static string? FindTagValue(IEnumerable<MetadataDirectory> directories, params string[] tagNames)
    {
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                foreach (var name in tagNames)
                {
                    if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return tag.Description;
                    }
                }
            }
        }

        return null;
    }
}
