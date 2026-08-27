#ifndef AppVersion
  #error AppVersion must be supplied by scripts/build-installer.ps1
#endif

#ifndef AppNumericVersion
  #error AppNumericVersion must be supplied by scripts/build-installer.ps1
#endif

[Setup]
AppId={{AEF6D778-50DE-4813-8CEA-F853D83AE36E}
AppName=QuietShelf
AppVersion={#AppVersion}
AppVerName=QuietShelf {#AppVersion}
AppPublisher=QuietShelf contributors
AppPublisherURL=https://github.com/Anle-He/QuietShelf
AppSupportURL=https://github.com/Anle-He/QuietShelf/issues
AppUpdatesURL=https://github.com/Anle-He/QuietShelf/releases
DefaultDirName={localappdata}\Programs\QuietShelf
DefaultGroupName=QuietShelf
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=QuietShelf-Setup-{#AppVersion}
SetupIconFile=..\src\QuietShelf.App\Assets\app-icon.ico
UninstallDisplayIcon={app}\QuietShelf.App.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppNumericVersion}
VersionInfoCompany=QuietShelf contributors
VersionInfoDescription=QuietShelf Installer
VersionInfoProductName=QuietShelf
VersionInfoProductVersion={#AppNumericVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\QuietShelf.App.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\QuietShelf"; Filename: "{app}\QuietShelf.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\QuietShelf"; Filename: "{app}\QuietShelf.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\QuietShelf.App.exe"; Description: "{cm:LaunchProgram,QuietShelf}"; Flags: nowait postinstall skipifsilent
