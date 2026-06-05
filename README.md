# SML Defender Control

Um **painel simples e de código aberto** que reúne, num só lugar fácil, os controles
mais usados do **Microsoft Defender (antivírus)** e do **Firewall do Windows**. Feito
para usuário leigo: status grande, poucos botões grandes, cada um com uma linha
explicando o que faz.

> ⚠️ Este utilitário **só usa APIs oficiais da Microsoft**: WMI, os cmdlets do módulo
> Defender, `MpCmdRun.exe`, o módulo `NetSecurity` (firewall) e o Agendador de Tarefas.
> **Sem exploits, sem driver de kernel, sem técnicas não documentadas, sem contornar o
> Tamper Protection.** Toda alteração exige administrador e roda com UAC honesto.

---

## O que ele faz (tela principal)

- **Status grande** (Protegido / Atenção) cobrindo antivírus **e** firewall.
- **Pausar proteção** por 15 min, 1 hora ou até reiniciar — com contador e retomada.
- **Desligar / Ligar proteção** em tempo real (com confirmação forte e reversão).
- **Marcar arquivo como confiável** (exclusão de arquivo/processo do antivírus).
- **Excluir pasta do escaneamento** (ex.: pasta de um projeto que o AV confunde com ameaça).
- **Bloquear / Liberar um app na internet** (regra de firewall por `.exe`).
- **Verificação rápida** e **Atualizar agora**.

A aba **Avançado** traz todos os toggles, exclusões detalhadas, histórico de ameaças,
lista de regras de firewall, agendamento, o botão **Iniciar com o Windows** e a
**proteção por senha**.

---

## Requisitos

**Para usar (rodar):**
- **Windows 8.1, 10 ou 11** (x86 ou x64).
- **.NET Framework 4.8** (já vem no Windows 10/11; instalável no 8.1).
- Conta com privilégios de **administrador** (o app pede elevação).

**Para compilar (desenvolver):**
- **MSBuild** — vem com o *Visual Studio 2022* ou com o *Build Tools for Visual Studio 2022* (gratuito, sem IDE).
- **.NET Framework 4.8 Developer Pack** (os *reference assemblies* / targeting pack para mirar o 4.8). Baixe em
  `https://dotnet.microsoft.com/download/dotnet-framework/net48` → "Developer Pack".
  > Observação: o *Developer Pack 4.8.1* (oferecido pelo winget) **não** serve para mirar o 4.8 — instale o
  > Developer Pack **4.8** especificamente, senão o build falha com "reference assemblies for v4.8 were not found".

---

## Como compilar (no Windows)

Não há dependências NuGet de terceiros — apenas a BCL, `System.Management` e Interop COM.

**Visual Studio**
1. Abra `PainelSeguranca.sln`.
2. Selecione **Release / Any CPU**.
3. **Compilar → Compilar Solução**. O resultado é
   `src\PainelSeguranca\bin\Release\PainelSeguranca.exe` (um único `.exe`).

**Linha de comando (MSBuild)**
```bat
msbuild PainelSeguranca.sln /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU"
```
(Se o MSBuild não estiver no PATH, use o caminho do Build Tools, por exemplo:
`"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"`.)

O `.exe` é **AnyCPU**, **sem ofuscador, sem packer e sem single-file** — de propósito:
assim ele é pequeno, limpo, auditável e **assinável**, e não dispara heurística de
antivírus.

---

## Por que é código aberto — e o que realmente gera confiança

Este projeto é 100% aberto (licença MIT, código legível, sem binários embutidos) por
**transparência**. Mas é importante entender:

> **Código aberto NÃO é o que faz o Windows confiar no app.**

O Windows confia por dois mecanismos:

1. **Assinatura digital Authenticode** do `.exe` (feita pelo mantenedor).
2. **Reputação no SmartScreen**, que cresce com assinatura consistente, tempo e número
   de downloads.

### Como assinar o `.exe` (remove o "editor desconhecido")

Com um certificado de **assinatura de código** (Code Signing) e o `signtool.exe` (vem
com o Windows SDK):

```bat
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ^
  /n "SML" "src\PainelSeguranca\bin\Release\SMLDefenderControl.exe"
```

> **Já incluído neste projeto:** o script **`sign.ps1`** cria (na primeira vez) um certificado de
> assinatura de código **self-signed "SML"**, assina o `.exe` com carimbo de tempo e confia nele
> **nesta máquina** (Trusted Root + Trusted Publishers). Resultado: no **seu** PC o UAC mostra o
> publicador **verificado "SML"**. Em **outros** PCs ainda apareceria "editor desconhecido" até você
> usar um certificado de uma **CA** (OV/EV). Rode com:
> ```powershell
> powershell -ExecutionPolicy Bypass -File .\sign.ps1
> ```

- **Certificado EV** (Extended Validation): o SmartScreen ganha reputação muito mais
  rápido (em alguns casos, imediatamente).
- **Certificado OV** (Organization Validation): funciona, mas a reputação é construída
  **com o tempo** e o volume de downloads.

Depois de assinado, o diálogo do **UAC mostra o publicador VERIFICADO (faixa azul)** em
vez de "Editor desconhecido" (faixa amarela). A assinatura é **do mantenedor**, não do
código — por isso ela precisa ser feita por quem distribui o binário.

Antes de publicar, troque **`[SEU NOME OU MARCA]`** em `Properties/AssemblyInfo.cs` e no
`LICENSE` pelo nome real, que deve **bater com o titular do certificado**.

---

## Inicialização silenciosa (Tarefa Agendada)

O app precisa de admin. Se ele iniciasse pela **pasta Inicializar** ou pela **chave
Run**, apareceria um **prompt de UAC a cada boot**. Para evitar isso, o "Iniciar com o
Windows" é implementado como uma **Tarefa Agendada** chamada `PainelSeguranca_Autostart`,
criada/removida pelo próprio app (que já roda elevado):

- Gatilho: **ao fazer logon** do usuário atual.
- **Executar com privilégios mais altos = SIM** → é isto que abre o app elevado **sem o
  prompt do UAC** no boot.
- **Executar somente quando o usuário estiver conectado = SIM** (app de bandeja).
- Condições de bateria **desmarcadas** e **sem limite de tempo** (funciona em notebooks
  na bateria).
- Implementação principal: **API COM do Agendador de Tarefas** (`Schedule.Service`, via
  Interop, sem NuGet). **Fallback**: `schtasks.exe /rl HIGHEST /sc ONLOGON` (Windows 8.1).

**Resumo:** o lançamento **automático** (boot) é **silencioso**. Se você abrir o app
**manualmente** (duplo-clique), o **UAC aparece por design** — e, com o `.exe` assinado,
ele mostra o publicador verificado.

> ❌ Não "resolvemos" o aviso de boot desligando UAC, SmartScreen ou o Gerenciador de
> Anexos — isso baixaria a segurança do sistema. A solução correta é Tarefa Agendada
> (boot silencioso) + assinatura (publicador verificado).

---

## Tamper Protection (proteção contra adulteração)

Desde o Windows 10 1903, o **Tamper Protection** impede alterar configurações do
antivírus **programaticamente**: `Set-MpPreference` falha ou é revertido em silêncio.

O app **detecta e exibe** `IsTamperProtected` com destaque e, quando uma escrita não tem
efeito, orienta:

> *"Tamper Protection está ativo. Para alterar, desligue-o em Segurança do Windows →
> Proteção contra vírus e ameaças → Gerenciar configurações."*

**O app NUNCA tenta burlar o Tamper Protection.** Isso é intencional e é o comportamento
correto e seguro.

---

## Por que pode ser marcado como PUA/HackTool — e o que fazer

Por **controlar antivírus e firewall**, o Defender/SmartScreen **pode** classificar este
tipo de app como **PUA** (Potentially Unwanted Application) ou **HackTool**. Mitigações:

- **Assinar** o `.exe` (publicador verificado).
- Manter o projeto **aberto e auditável** (este repositório).
- **Não empacotar** (sem packer/ofuscador/single-file).
- Em caso de **falso positivo**, enviar o arquivo para análise da Microsoft:
  **Microsoft Security Intelligence – Submit a file**
  (`https://www.microsoft.com/wdsi/filesubmission`).

---

## Modo interativo do firewall (opcional, Caminho A)

Na aba **Avançado → Firewall** há um interruptor **"Modo interativo"** (desligado por padrão).
Quando ligado, ele:

1. Define a **ação padrão de saída como "Bloquear"** em todos os perfis
   (`Set-NetFirewallProfile -All -DefaultOutboundAction Block`).
2. Liga a **auditoria da Plataforma de Filtragem** (`auditpol`, subcategoria por GUID para não
   depender do idioma), o que faz o Windows registrar o **evento de Segurança 5157** quando bloqueia
   uma conexão.
3. **Monitora** o log de Segurança (`EventLogWatcher`) e, para cada app novo barrado, mostra um
   **pop-up "Permitir / Bloquear"**. *Permitir* cria uma regra de liberação para aquele `.exe`;
   *Bloquear* mantém barrado naquela sessão.

> ⚠️ **Aviso (assumido ao ligar):** "bloquear por padrão" é **agressivo** — até você aprovar, os apps
> ficam **sem internet** (pode afetar até o Windows Update por instantes). O recurso é **opt-in**, mostra
> um aviso claro antes de ligar e é **desligável em 1 clique** (na própria aba **ou** pelo menu da bandeja
> em "Desligar modo interativo"). Ao **sair** do app pelo menu, o modo é desligado automaticamente para
> você nunca ficar sem rede e sem o serviço de pop-up.

Tudo isso usa **apenas APIs oficiais** do Windows Firewall e do log de eventos — **sem driver de kernel,
sem callout próprio do WFP**.

---

## Admin, UAC e segurança

- O app **roda elevado** (UAC honesto) porque toda alteração no Defender e no Firewall
  exige administrador. Ele **nunca se esconde** do usuário.
- Toda ação que **enfraquece a segurança** (desligar firewall, pausar proteção, default
  block) pede **confirmação clara** e é **reversível em 1 clique**.
- **Proteção por senha opcional** (aba Avançado): guardamos apenas um **hash salgado
  PBKDF2 (Rfc2898)** — nunca a senha. É proteção da interface, não um contorno.
- Log rotativo em `%LOCALAPPDATA%\PainelSeguranca\logs`.

---

## Estrutura do projeto

```
PainelSeguranca.sln
src/PainelSeguranca/
  PainelSeguranca.csproj      Projeto .NET Framework 4.8, AnyCPU, sem ofuscador/packer
  app.manifest                requireAdministrator + supportedOS 8.1/10/11 + DPI aware
  app.ico                     Ícone do app (escudo, multi-resolução)
  Properties/AssemblyInfo.cs  Metadados de publicador (trocar [SEU NOME OU MARCA])
  Program.cs                  Entrada: instância única, elevação, contexto de bandeja
  Core/                       Logger, ProcessRunner, ElevationHelper, I18n, settings...
  Services/                   Defender, Firewall, Autostart, Pausa, Status agregado
  UI/                         Formulários (tela principal, avançado, bandeja, prompts)
LICENSE                       Licença MIT
README.md                     Este arquivo
```

### Lista detalhada (cada arquivo, 1 linha)

**Raiz**
- `PainelSeguranca.sln` — solução do Visual Studio com o único projeto.
- `LICENSE` — licença MIT.
- `README.md` — este documento.

**Projeto (`src/PainelSeguranca/`)**
- `PainelSeguranca.csproj` — projeto .NET Framework 4.8, AnyCPU, WinExe, sem ofuscador/packer.
- `app.manifest` — pede `requireAdministrator`, declara suporte a 8.1/10/11 e DPI awareness.
- `app.ico` — ícone do app (escudo, multi-resolução 16→256).
- `Properties/AssemblyInfo.cs` — metadados de publicador (trocar `[SEU NOME OU MARCA]`).
- `Program.cs` — ponto de entrada: instância única (Mutex), elevação, exceções globais, contexto de bandeja.

**Core (`src/PainelSeguranca/Core/`)**
- `Logger.cs` — log rotativo em `%LOCALAPPDATA%\PainelSeguranca\logs` (nunca derruba o app).
- `ProcessRunner.cs` — executa PowerShell/MpCmdRun/netsh ocultos e assíncronos, capturando ExitCode/stdout/stderr.
- `ElevationHelper.cs` — verifica se está elevado e reabre elevado (verbo `runas`).
- `AppSettings.cs` — configurações persistidas em `settings.ini` (idioma, senha) — sem NuGet/JSON.
- `I18n.cs` — internacionalização PT-BR/EN por dicionários (mantém o app em um único `.exe`).
- `IconFactory.cs` — desenha os ícones de status (verde/amarelo/vermelho) em runtime com GDI+.
- `PasswordProtection.cs` — hash salgado PBKDF2 (Rfc2898/SHA-256) da senha da interface.

**Serviços (`src/PainelSeguranca/Services/`)**
- `SecurityStatus.cs` — modelos de status do Defender, do Firewall e do agregado (define a cor).
- `DefenderService.cs` — antivírus: leitura WMI, escrita PowerShell com confirmação, Tamper/passivo, scans MpCmdRun.
- `FirewallService.cs` — firewall: estado/perfis, bloquear/liberar app, listar/remover regras (NetSecurity + netsh).
- `PauseProtectionService.cs` — pausa temporária com timer que religa sozinho e contagem regressiva.
- `AutostartService.cs` — inicialização silenciosa via Tarefa Agendada (COM `Schedule.Service`) + fallback `schtasks`.

**Interface (`src/PainelSeguranca/UI/`)**
- `UiHelpers.cs` — tema claro/escuro, botão grande de 2 linhas (`ActionTile`), diálogos e estilos.
- `MainForm.cs` — tela principal simples (status + botões grandes + banner de Tamper/passivo).
- `AdvancedForm.cs` — janela Avançado (toggles, exclusões, firewall, scans, inicialização, senha, idioma, modo interativo).
- `TextPromptForm.cs` — diálogo de entrada de texto de uma linha (extensão/processo).
- `PasswordPromptForm.cs` — diálogo de senha (verificar e definir/alterar).
- `FirewallPromptForm.cs` — pop-up "Permitir / Bloquear" do modo interativo (sempre no topo).
- `TrayApplicationContext.cs` — ícone de bandeja (cor por status), menu, toasts e fila de pop-ups; dona da janela.

**Serviço adicional**
- `Services/InteractiveFirewallService.cs` — modo interativo (Caminho A): saída bloqueada por padrão + auditoria + monitor do evento 5157.

---

## Licença

[MIT](LICENSE). Use, modifique e distribua livremente, mantendo o aviso de copyright.
