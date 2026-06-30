#define AppName "Fylax"
#ifndef AppVersion
#define AppVersion "0.2.6"
#endif
#ifndef ReleaseVersion
#define ReleaseVersion "v0.2.6"
#endif
#ifndef SourceDir
#define SourceDir "..\src\Fylax\bin\Release\net10.0-windows\win-x64\publish"
#endif
#ifndef OutputDir
#define OutputDir "..\dist"
#endif

[Setup]
AppId={{8C7B5D4E-2C3A-45B2-9E6F-1D2E31F91A77}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VasilisPngs
DefaultDirName={autopf}\Fylax
DefaultGroupName=Fylax
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Fylax-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Fylax
SetupLogging=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Fylax"; Filename: "{app}\Fylax.exe"
Name: "{autodesktop}\Fylax"; Filename: "{app}\Fylax.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\Fylax.exe"; Description: "Launch Fylax"; Flags: nowait postinstall skipifsilent runascurrentuser
