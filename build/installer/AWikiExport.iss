#define AppName "AWikiExport"
#define AppExeName "ExportAzureWiki.Wpf.exe"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\installer\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "AWikiExportSetup"
#endif

[Setup]
AppId={{B2445955-1C9A-4E4C-A115-1D8CF9EB7792}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Ti com Cafe
AppPublisherURL=https://github.com/marquesantero/awikiexporter
AppSupportURL=https://github.com/marquesantero/awikiexporter/issues
AppUpdatesURL=https://github.com/marquesantero/awikiexporter/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\..\ExportAzureWiki.Wpf\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
