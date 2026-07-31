; Inno Setup script for PC Build Companion
;
; This builds a Setup.exe that:
;   - Installs the app to Program Files
;   - Creates a Start Menu shortcut (and optional Desktop shortcut)
;   - Registers an uninstaller (shows up in "Add or Remove Programs")
;
; HOW TO USE THIS FILE:
;   1. Install Inno Setup (free): https://jrsoftware.org/isinfo.php
;   2. First, publish the app so the .exe exists to package. In a terminal in
;      the project folder, run:
;        dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
;      (Rider users: Run > Edit Configurations > add a "Publish" run, or just
;      run the command above in Rider's built-in terminal.)
;   3. Open this file (installer.iss) in Inno Setup and click Build > Compile.
;   4. Your Setup.exe will appear in the "installer_output" folder.
;
; You only need to redo step 2 when you change the code, and step 3 (or just
; press F9 in Inno Setup) to rebuild the installer afterward.

#define MyAppName "PC Build Companion"
#define MyAppVersion "1.0.0"
#define MyAppExeName "PCBuildCompanion.exe"

; Path to the published output. Adjust if your publish folder differs.
#define PublishDir "bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B4C1F6A0-6E3D-4A2F-9C1E-7B2F4C9A1D3E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=installer_output
OutputBaseFilename=PCBuildCompanion-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall
