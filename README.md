# CamtraptionAnalysis

Windows desktop tool for offline analysis of Camtraption camera capture folders. Reads EXIF and Canon maker-note metadata from JPG/JPEG files (optionally CR2/CR3/ARW raw), infers camera modes (C1/C2/C3), checks schedule alignment, and builds a capture timeline with transitions, wake cycles, and firmware error events.

Report output is compatible with the Python tool in `camtraption_agent/analysis/detect_camera_modes.py`.

## Requirements

- Windows x64
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (for running published builds or the installer)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for development builds only)

## Quick start

Open the solution in Visual Studio, or from a terminal at the repo root:

```powershell
dotnet run --project CamtraptionAnalysis\CamtraptionAnalysis.csproj
```

Debug build output:

`CamtraptionAnalysis\bin\Debug\net9.0-windows\CamtraptionAnalysis.exe`

Release publish (used by the installer script):

```powershell
dotnet publish CamtraptionAnalysis\CamtraptionAnalysis.csproj -c Release -r win-x64 --self-contained false
```

## Using the app

1. **Browse…** — select a folder containing capture JPGs (typically a USB copy of a kit’s card).
2. **Run Analysis** — discovers all `.jpg`/`.jpeg` files recursively, extracts metadata in parallel, then runs the in-memory analysis phase.
3. **Summary panel** — root path, file counts, camera serial, schedule artist, mismatch totals, and saved report path.
4. **Capture timeline** — sortable grid of event rows and capture rows. Row highlighting indicates transitions, wake cycles, mode changes, notes, warnings, and errors.
5. **Show issues only** — filter the grid to notable rows (mismatches, events, read errors).
6. **Also process raw files** — when checked, also discovers `.cr2`, `.cr3`, and `.arw` files (default off).
7. **Open Reports Folder** — opens the folder where text reports are saved.

Each run writes a full text report to:

`%USERPROFILE%\Documents\CamtraptionAnalysis\<yyyy-MM-dd-HHmmss>.txt`

Reports include transition markers, CSV-style capture rows, and warnings — the same content shown in the grid, in plain text for archiving or diffing.

## What it analyzes

- **Mode inference (C1/C2/C3)** from Canon shutter mode and flash exposure compensation
- **Schedule matching** from the copyright/artist stamp (artist name + time slots, +45 s capture delay)
- **Logged vs inferred** comparison of the mode recorded in the copyright stamp
- **Capture timeline** — scheduled awake, pre-config, post-config windows, mode transitions, long gaps, stale copyright stamps, wake cycles
- **Firmware ERR codes** in copyright stamps, decoded to human-readable camera error names
- **Multi-camera scans** — groups and sorts by serial number, then capture time, then filename

Metadata extraction uses roughly 75% of logical CPU cores; the analysis phase runs single-threaded in memory after all files are read.

## Architecture

```
AnalysisPipeline
  ├── JpegFileEnumerator (streaming discovery)
  ├── parallel MetadataFieldReader (EXIF + CanonFieldDecoder)
  └── RamImageAnalysisPhase
        ├── ModeInference + ScheduleAnalyzer
        ├── CaptureTimelineBuilder
        └── CameraModeReportBuilder + AnalysisSummaryBuilder
```

## Project layout

```
CamtraptionAnalysis.sln
CamtraptionAnalysis/          WPF app (.NET 9)
  Models/                     ImageObservation, CaptureTimelineEntry, AnalysisSummary, …
  Services/                   pipeline, decoders, report builders
installer/
  build-installer.ps1         publish + Inno Setup compile
  CamtraptionAnalysis.iss     installer definition
  output/                     generated setup .exe (gitignored)
```

## Key services

| File | Role |
|------|------|
| `AnalysisPipeline.cs` | File discovery, parallel metadata read, orchestration |
| `MetadataFieldReader.cs` | EXIF extraction via MetadataExtractor |
| `CanonFieldDecoder.cs` | ShutterMode (FileInfo index 23), FlashExposureComp (Canon EV) |
| `CopyrightStampParser.cs` | Parses mode, artist schedule, and ERR segment from copyright field |
| `ModeInference.cs` | C1/C2/C3 from shutter type + flash comp |
| `ScheduleAnalyzer.cs` | Artist schedule parsing, expected mode, match evaluation |
| `CaptureTimelineBuilder.cs` | Event timeline: transitions, wake cycles, config windows, gaps |
| `CameraErrorDecoder.cs` | Maps firmware ERR integers to named error codes |
| `CameraModeReportBuilder.cs` | Text report matching the Python analysis tool |
| `ReportFileWriter.cs` | Saves timestamped reports under Documents |

## Dependencies

- **MetadataExtractor** 2.9.2 (NuGet) — EXIF and maker-note reading

## Installer

Build a Windows setup `.exe` for the app only. The installer does **not** bundle the .NET runtime.

```powershell
.\installer\build-installer.ps1
```

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php) on the build machine.

Output: `installer\output\CamtraptionAnalysis-Setup-1.0.0.exe`

On the target PC, install **.NET 9 Desktop Runtime (x64)** first if needed. The installer checks the registry and stops with a download link if the runtime is missing.

Place optional redistributables (e.g. `windowsdesktop-runtime-*.exe`) in `installer/redist/` locally — that folder is gitignored.

## Local data (not in repo)

The following are excluded via `.gitignore` and are not checked in:

- Build output (`bin/`, `obj/`, `installer/output/`)
- Test capture folders (`testdata/`, `samples/`, `captures/`, …)
- Saved analysis reports in-repo (`reports/`)
- Machine-specific notes and secrets (`cursor_notes.txt`, `.env`, …)
