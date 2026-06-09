; CamtraptionAnalysis setup — app only (does not bundle .NET Desktop Runtime).
; Requires .NET 9 Desktop Runtime (x64) to already be installed on the target PC.

#define AppName "Camtraption Analysis"
#define AppExe "CamtraptionAnalysis.exe"
#define AppPublisher "Camtraption"
#define AppUrl "https://github.com/camtraption/CamtraptionAnalysis"
#define DotNetDownloadUrl "https://dotnet.microsoft.com/en-us/download/dotnet/9.0"
#define PublishDir "..\CamtraptionAnalysis\bin\Release\net9.0-windows\win-x64\publish"
#define AppVersion "1.0.0"

[Setup]
AppId={{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=CamtraptionAnalysis-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=
InfoBeforeFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function HasDesktopRuntime9InKey(RootKey: Integer; const SubKey: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey, SubKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Copy(Names[I], 1, 4) = '9.0.' then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function IsDesktopRuntime9Installed: Boolean;
begin
  { x64 desktop runtimes are registered under WOW6432Node on 64-bit Windows }
  Result :=
    HasDesktopRuntime9InKey(HKLM,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
    HasDesktopRuntime9InKey(HKLM64,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if IsDesktopRuntime9Installed then
    Exit;

  if MsgBox(
    'Camtraption Analysis requires the .NET 9 Desktop Runtime (x64).' + #13#10 + #13#10 +
    'It is not installed on this computer. Install the runtime first, then run this setup again.' + #13#10 + #13#10 +
    'Direct download (x64):' + #13#10 +
    'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.16/windowsdesktop-runtime-9.0.16-win-x64.exe' + #13#10 + #13#10 +
    'Open the .NET 9 download page in your browser?',
    mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', '{#DotNetDownloadUrl}', '', '', SW_SHOW, ewNoWait, ResultCode);
  end;
  Result := False;
end;
