; Inno Setup script for Echo Speaker System.
;
; Reproduces the installer that shipped v0.4.4 (built with Inno Setup 6.1.0). The AppId,
; publisher string and install directory are deliberately identical to the original so
; Windows treats this as an upgrade rather than a second product, and so the existing
; uninstall entry is replaced instead of duplicated. The publisher typo ("iblowasports")
; is preserved for that reason - do not "fix" it.
;
; Build:  "J:\InnoSetup6\ISCC.exe" EchoSpeakerSystem.iss
; Requires PayloadDir / ExtrasDir below to exist.

#define AppName        "Echo Speaker System"
#define AppVersion     "0.4.5"
#define AppPublisher   "iblowasports"
#define AppURL         "https://github.com/heisthecat31/Echo-VR-Speaker-System"

; Unity player build output
#define PayloadDir     "J:\EchoSpeakerSystem-TestBuild"
; Goal horns + the bundled Virtual Audio Cable setup, carried over from the v0.4.4 install
#define ExtrasDir      "J:\ESS-backup-v0.4.4"
; Generated file holding just the version number - Spark reads this to know what is installed
#define GeneratedDir   "J:\TMP\claude\J--EchoVR-Tools-Launcher-Echo-VR-Speaker-System\9c4e6c26-d911-4673-9322-731fe1715108\scratchpad\installer_gen"
#define OutDir         "J:\TMP\claude\J--EchoVR-Tools-Launcher-Echo-VR-Speaker-System\9c4e6c26-d911-4673-9322-731fe1715108\scratchpad\installer_out"

[Setup]
AppId={{22A1D2F5-EB67-49BB-A0C2-13B61AEA20FD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} version {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={commonpf32}\Echo Speaker System
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Original showed "Echo Speaker System version X" in Add/Remove Programs; keep that.
UninstallDisplayName={#AppName} version {#AppVersion}
UninstallDisplayIcon={app}\Echo Speaker System.exe
OutputDir={#OutDir}
OutputBaseFilename=EchoSpeakerSystemInstall_v{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Writing to Program Files (x86) needs elevation, same as the original.
PrivilegesRequired=admin
; Shut ESS down if it is running rather than failing on a locked file.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The Unity player build. run.log is a local test artefact and must not ship.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; \
    Excludes: "run.log,*.log"; \
    Flags: recursesubdirs createallsubdirs ignoreversion

; Goal horns. onlyifdoesntexist so an upgrade never destroys a custom GoalHorn.wav -
; that file is the documented way for users to supply their own horn.
Source: "{#ExtrasDir}\GoalHorn.wav";        DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#ExtrasDir}\GoalHorn_Bruins.wav"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#ExtrasDir}\GoalHorn_CBJ.wav";    DestDir: "{app}"; Flags: onlyifdoesntexist

; Bundled Virtual Audio Cable setup (7-Zip SFX), as shipped with v0.4.4.
Source: "{#ExtrasDir}\vac464full.exe"; DestDir: "{app}"; Flags: ignoreversion

; Spark reads this to determine the installed version.
Source: "{#GeneratedDir}\latestversion.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Echo Speaker System.exe"

[Run]
Filename: "{app}\vac464full.exe"; \
    Description: "Install Virtual Audio Cable (skip if you already have a virtual cable)"; \
    Flags: postinstall skipifsilent unchecked
Filename: "{app}\Echo Speaker System.exe"; \
    Description: "Launch {#AppName}"; \
    Flags: postinstall skipifsilent nowait
