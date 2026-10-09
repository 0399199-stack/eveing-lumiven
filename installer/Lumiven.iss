#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif

[Setup]
AppId={{52E4F908-97BD-4676-A857-5B296DB76DB0}
AppName=EVEING Lumiven
AppVersion={#AppVersion}
AppPublisher=EVEING
AppPublisherURL=https://github.com/0399199-stack/eveing-lumiven
AppSupportURL=https://github.com/0399199-stack/eveing-lumiven/issues
DefaultDirName={localappdata}\Programs\EVEING Lumiven
DefaultGroupName=EVEING Lumiven
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#ReleaseDir}
OutputBaseFilename=EVEING-Lumiven-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\EVEING.Lumiven.exe
LicenseFile=..\LICENSE
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\EVEING Lumiven"; Filename: "{app}\EVEING.Lumiven.exe"
Name: "{autodesktop}\EVEING Lumiven"; Filename: "{app}\EVEING.Lumiven.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EVEING.Lumiven.exe"; Description: "Launch EVEING Lumiven"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Application data and user-selected downloads are deliberately outside {app}.
; No certificate installation, proxy change, driver installation or data cleanup is run by Setup.
