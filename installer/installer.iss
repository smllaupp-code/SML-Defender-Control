; ============================================================================
;  Inno Setup - SML Defender Control
;  Gera um instalador (setup.exe) com desinstalador.
;  O desinstalador LIMPA o que o app cria no sistema: tarefa de autostart,
;  regras de firewall e restaura a acao padrao de saida para "Permitir".
; ============================================================================

#define AppName "SML Defender Control"
#define AppVersion "1.3.0"
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

[Registry]
; ============================================================================
;  Menu de contexto do Windows Explorer (submenu em cascata "SML Defender").
;  Como o instalador roda elevado (PrivilegesRequired=admin), HKCR grava em
;  HKLM\Software\Classes -> vale para TODOS os usuarios da maquina.
;  O submenu usa o padrao: chave-pai com MUIVerb + valor "subcommands" vazio,
;  o que faz o shell enumerar a subchave "shell" para montar a cascata.
;  uninsdeletekey nas chaves-pai remove toda a arvore na desinstalacao.
; ============================================================================

; --- Arquivos .exe (classe exefile): Bloquear / Desbloquear / Exclusoes ---
Root: HKCR; Subkey: "exefile\shell\SMLDefender"; ValueType: string; ValueName: "MUIVerb"; ValueData: "SML Defender"; Flags: uninsdeletekey
Root: HKCR; Subkey: "exefile\shell\SMLDefender"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"
Root: HKCR; Subkey: "exefile\shell\SMLDefender"; ValueType: string; ValueName: "subcommands"; ValueData: ""
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\01block"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Bloquear na internet"
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\01block\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --block ""%1"""
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\02unblock"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Desbloquear na internet"
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\02unblock\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --unblock ""%1"""
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\03addexcl"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Adicionar as exclusoes do Defender"
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\03addexcl\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --add-excl ""%1"""
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\04remexcl"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Remover das exclusoes do Defender"
Root: HKCR; Subkey: "exefile\shell\SMLDefender\shell\04remexcl\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --rem-excl ""%1"""

; --- Pastas (classe Directory): SO exclusoes (firewall nao aceita pasta) ---
Root: HKCR; Subkey: "Directory\shell\SMLDefender"; ValueType: string; ValueName: "MUIVerb"; ValueData: "SML Defender"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\SMLDefender"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"
Root: HKCR; Subkey: "Directory\shell\SMLDefender"; ValueType: string; ValueName: "subcommands"; ValueData: ""
Root: HKCR; Subkey: "Directory\shell\SMLDefender\shell\01addexcl"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Adicionar as exclusoes do Defender"
Root: HKCR; Subkey: "Directory\shell\SMLDefender\shell\01addexcl\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --add-excl ""%1"""
Root: HKCR; Subkey: "Directory\shell\SMLDefender\shell\02remexcl"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Remover das exclusoes do Defender"
Root: HKCR; Subkey: "Directory\shell\SMLDefender\shell\02remexcl\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" --rem-excl ""%1"""

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
