; Instalador do ThermoBook (Inno Setup 6). Instala só para o usuário atual, sem UAC.
; Antes: dotnet publish src/ThermoBook -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
; Depois: ISCC installer\ThermoBook.iss /DMyAppVersion=0.1.0

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define PublishDir "..\publish"

[Setup]
AppId={{6F3A2C1E-8B4D-4E7A-9C2F-1D5B7E9A3C40}
AppName=ThermoBook
AppVersion={#MyAppVersion}
AppPublisher=playertwo1
AppPublisherURL=https://github.com/playertwo1/thermobook
DefaultDirName={userpf}\ThermoBook
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=ThermoBook-Setup-{#MyAppVersion}
SetupIconFile=..\assets\thermobook.ico
UninstallDisplayIcon={app}\ThermoBook.exe
UninstallDisplayName=ThermoBook
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PublishDir}\ThermoBook.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ThermoBook"; Filename: "{app}\ThermoBook.exe"

[Run]
Filename: "{app}\ThermoBook.exe"; Description: "Abrir o ThermoBook"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Na ordem: fecha o app, volta o processador ao padrão de fábrica (perfil Normal).
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im ThermoBook.exe"; Flags: runhidden; RunOnceId: "Fechar"
Filename: "{app}\ThermoBook.exe"; Parameters: "--restaurar"; Flags: runhidden waituntilterminated; RunOnceId: "Restaurar"

[Registry]
; Não cria nada: só apaga o "Iniciar com o Windows" na desinstalação.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ThermoBook"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\ThermoBook"
