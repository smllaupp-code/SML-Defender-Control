; ============================================================================
;  Inno Setup - SML Defender Control
;  Gera um instalador (setup.exe) com desinstalador.
;  O desinstalador LIMPA o que o app cria no sistema: tarefa de autostart,
;  regras de firewall e restaura a acao padrao de saida para "Permitir".
; ============================================================================

#define AppName "SML Defender Control"
#define AppVersion "1.2.0"
#define AppPublisher "SML"
#define AppExeName "SMLDefenderControl.exe"
#define AppId "{{B7A1F4C2-1E3D-4A6B-9C8E-0D2F5A7B9C11}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=SMLDefenderControl-Setup-{#AppVersion}
SetupIconFile=..\src\PainelSeguranca\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; O app exige administrador; o instalador tambem roda elevado.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.3
; Fecha o app automaticamente se estiver aberto durante a instalacao/atualizacao.
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\src\PainelSeguranca\bin\Release\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Oferece abrir o app ao final (ja elevado).
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Estes comandos rodam ANTES de remover os arquivos, com o desinstalador elevado.
; 1) Garante que o app esteja fechado.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
; 2) Remove a tarefa de inicializacao silenciosa.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN SMLDefenderControl_Autostart /F"; Flags: runhidden; RunOnceId: "DelTask"
; 3) Restaura a acao padrao de saida do firewall para Permitir (caso o modo interativo estivesse ligado).
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""Set-NetFirewallProfile -All -DefaultOutboundAction Allow"""; Flags: runhidden; RunOnceId: "FwRestore"
; 4) Remove as regras de firewall criadas pelo app (bloqueios e permissoes do modo interativo).
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""Get-NetFirewallRule -Group 'PainelSeguranca','PainelSeguranca_Interativo' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"""; Flags: runhidden; RunOnceId: "FwRules"
; 5) Desliga a auditoria que o modo interativo pode ter ligado.
Filename: "{sys}\auditpol.exe"; Parameters: "/set /subcategory:{{0CCE9226-69AE-11D9-BED3-505054503030}} /success:disable /failure:disable"; Flags: runhidden; RunOnceId: "AuditOff"

[UninstallDelete]
; Remove dados locais do app (logs e configuracoes) do usuario que desinstala.
Type: filesandordirs; Name: "{localappdata}\SMLDefenderControl"
