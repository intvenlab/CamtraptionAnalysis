#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'CamtraptionAnalysis\CamtraptionAnalysis.csproj'
$PublishDir = Join-Path $RepoRoot 'CamtraptionAnalysis\bin\Release\net9.0-windows\win-x64\publish'
$IssFile = Join-Path $PSScriptRoot 'CamtraptionAnalysis.iss'
$OutputDir = Join-Path $PSScriptRoot 'output'

Write-Host 'Publishing CamtraptionAnalysis (framework-dependent, win-x64)...'
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishReadyToRun=true

if (-not (Test-Path (Join-Path $PublishDir 'CamtraptionAnalysis.exe'))) {
    throw "Publish output missing: $PublishDir\CamtraptionAnalysis.exe"
}

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Iscc) {
    Write-Host ''
    Write-Host 'Inno Setup 6 was not found. Install it from:' -ForegroundColor Yellow
    Write-Host '  https://jrsoftware.org/isdl.php' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "Publish output is ready at:`n  $PublishDir" -ForegroundColor Cyan
    Write-Host "After installing Inno Setup, compile with:`n  `"$($IsccCandidates[0])`" `"$IssFile`"" -ForegroundColor Cyan
    exit 2
}

Write-Host "Compiling installer with: $Iscc"
& $Iscc $IssFile

$SetupExe = Get-ChildItem -Path $OutputDir -Filter 'CamtraptionAnalysis-Setup-*.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($SetupExe) {
    Write-Host ''
    Write-Host "Installer created:" -ForegroundColor Green
    Write-Host "  $($SetupExe.FullName)" -ForegroundColor Green
} else {
    throw "ISCC finished but no setup exe found in $OutputDir"
}
