# CamtraptionAnalysis

WPF desktop tool for analyzing Camtraption camera capture folders offline.

## Current features

- Recursively processes **all** JPG/JPEG files under a selected folder
- Parallel metadata extraction (`~75%` of CPU cores)
- Single-threaded in-memory analysis phase (mode inference, schedule checks, transition markers)
- Sort order: **serial number**, then **capture time**, then filename
- Report format mirrors `camtraption_agent/analysis/detect_camera_modes.py`

### Run

```powershell
dotnet run --project CamtraptionAnalysis\CamtraptionAnalysis.csproj
```

Build output: `CamtraptionAnalysis\bin\Debug\net9.0-windows\CamtraptionAnalysis.exe`

### UI

1. **Browse…** — pick a folder (USB copy of kit captures)
2. **Run Analysis** — discovers all JPGs, extracts metadata in parallel, builds report
3. **Summary panel** — root path, serial, mismatch counts, saved report path
4. **DataGrid** — sortable table of capture results (mismatch rows highlighted)
5. **Show mismatches only** — filter the grid
6. **Open Reports Folder** — opens `%USERPROFILE%\Documents\CamtraptionAnalysis`

Each run saves the full text report (transitions, CSV rows, warnings) to:

`Documents\CamtraptionAnalysis\<yyyy-MM-dd-HHmmss>.txt`

## Architecture

```
AnalysisPipeline
  ├── parallel MetadataFieldReader (EXIF + CanonFieldDecoder)
  └── RamImageAnalysisPhase (sort, infer C1/C2/C3, schedule match, transitions)
        └── CameraModeReportBuilder
```

## Key services

| File | Role |
|------|------|
| `CanonFieldDecoder.cs` | ShutterMode (FileInfo index 23), FlashExposureComp (Canon EV) |
| `ModeInference.cs` | C1/C2/C3 from shutter type + flash comp |
| `ScheduleAnalyzer.cs` | Artist schedule parsing, +45s delay, transition coverage |
| `CameraModeReportBuilder.cs` | CSV report matching Python tool |

## Dependencies

- .NET 9 Desktop Runtime (Windows x64) — [download](https://dotnet.microsoft.com/download/dotnet/9.0)
- MetadataExtractor 2.9.2 (NuGet; included in publish output)

## Installer

Build a Windows setup `.exe` for the app only (does **not** bundle the .NET runtime):

```powershell
.\installer\build-installer.ps1
```

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php) on the build machine. Output:

`installer\output\CamtraptionAnalysis-Setup-1.0.0.exe`

On the target PC, install **.NET 9 Desktop Runtime (x64)** first if needed, then run the setup. The installer checks the registry and stops with a download link if the runtime is missing.
