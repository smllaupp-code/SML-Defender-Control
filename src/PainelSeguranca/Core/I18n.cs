using System.Collections.Generic;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// Internacionalizacao PT-BR / EN baseada em dicionarios em codigo.
    ///
    /// Por que dicionarios e nao .resx satelite: a meta do projeto e UM unico .exe
    /// limpo e assinavel. Recursos de cultura .resx geram ASSEMBLIES SATELITE separados
    /// (ex.: pt-BR\PainelSeguranca.resources.dll), o que quebraria o "single .exe".
    /// Centralizar as strings aqui mantem o app num unico binario e ainda facil de traduzir.
    /// </summary>
    public static class I18n
    {
        private static string _lang = "pt-BR";

        public static string Language { get { return _lang; } }

        public static void SetLanguage(string lang)
        {
            _lang = (lang == "en") ? "en" : "pt-BR";
        }

        /// <summary>Carrega o idioma salvo nas configuracoes (chamado no startup).</summary>
        public static void LoadFromSettings()
        {
            SetLanguage(AppSettings.Instance.Language);
        }

        /// <summary>Traducao por chave. Se faltar, devolve a chave (visivel para corrigir).</summary>
        public static string T(string key)
        {
            Dictionary<string, string> dict = (_lang == "en") ? En : Pt;
            string v;
            if (dict.TryGetValue(key, out v)) return v;
            // fallback para PT se faltar no EN
            if (_lang == "en" && Pt.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>Traducao com formatacao (string.Format).</summary>
        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        private static readonly Dictionary<string, string> Pt = new Dictionary<string, string>
        {
            { "App.Title", "SML Defender Control" },
            { "App.Tray.Tooltip", "SML Defender Control" },

            { "Status.Protected", "Protegido" },
            { "Status.Attention", "Atencao" },
            { "Status.Passive", "Modo passivo" },
            { "Status.Loading", "Verificando..." },
            { "Status.Protected.Sub", "Antivirus e firewall estao ativos." },
            { "Status.Attention.Sub", "Algo precisa da sua atencao. Veja os detalhes abaixo." },

            { "Main.Heading", "Sua protecao" },
            { "Main.Av", "Antivirus (Defender)" },
            { "Main.Fw", "Firewall do Windows" },

            { "Btn.Pause", "Pausar protecao" },
            { "Btn.Pause.Sub", "Suspende o antivirus por um tempo e religa sozinho." },
            { "Btn.Pause.15", "Pausar por 15 minutos" },
            { "Btn.Pause.60", "Pausar por 1 hora" },
            { "Btn.Pause.Reboot", "Pausar ate reiniciar o PC" },
            { "Btn.Resume", "Retomar protecao agora" },
            { "Pause.Counter", "Protecao pausada. Retoma em {0}." },
            { "Pause.UntilReboot", "Protecao pausada ate reiniciar o PC." },

            { "Btn.Toggle.Off", "Desligar protecao" },
            { "Btn.Toggle.On", "Ligar protecao" },
            { "Btn.Toggle.Sub", "Use ao instalar um programa que precisa de acesso profundo ao Windows." },

            { "Btn.Trust", "Marcar arquivo como confiavel" },
            { "Btn.Trust.Sub", "Diz ao antivirus para nao analisar um arquivo/programa especifico." },

            { "Btn.ExcludeFolder", "Excluir pasta do escaneamento" },
            { "Btn.ExcludeFolder.Sub", "Ex.: a pasta do seu projeto em Python/CRM, que o antivirus pode confundir com ameaca." },

            { "Btn.BlockApp", "Bloquear / Liberar um app na internet" },
            { "Btn.BlockApp.Sub", "Escolha um programa (.exe) para impedir ou permitir o acesso a rede." },

            { "Btn.QuickScan", "Verificacao rapida" },
            { "Btn.QuickScan.Sub", "Analisa os locais onde ameacas costumam aparecer." },
            { "Btn.Update", "Atualizar agora" },
            { "Btn.Update.Sub", "Baixa as definicoes de virus mais recentes." },

            { "Btn.Advanced", "Avancado" },
            { "Btn.Settings", "Configuracoes" },
            { "Btn.Refresh", "Atualizar status" },
            { "Btn.Close", "Fechar" },
            { "Btn.Open", "Abrir painel" },
            { "Btn.Exit", "Sair" },
            { "Btn.Ok", "OK" },
            { "Btn.Cancel", "Cancelar" },
            { "Btn.Yes", "Sim" },
            { "Btn.No", "Nao" },
            { "Btn.Add", "Adicionar" },
            { "Btn.Remove", "Remover" },

            { "Tamper.On", "Tamper Protection ATIVO" },
            { "Tamper.Explain", "O Tamper Protection esta ativo. Para alterar configuracoes do antivirus, desligue-o em: Seguranca do Windows -> Protecao contra virus e ameacas -> Gerenciar configuracoes.\n\nObservacao: por seguranca, o Windows NAO permite que nenhum aplicativo (nem este) desligue o Tamper Protection automaticamente — apenas voce, na tela da Seguranca do Windows." },
            { "Tamper.OpenAsk", "Quer que eu abra a Seguranca do Windows agora, ja na tela certa, para voce desligar o Tamper Protection?" },
            { "Tamper.Blocked", "bloqueado pelo Tamper Protection" },
            { "Passive.On", "Antivirus em modo passivo" },
            { "Passive.Explain", "O Defender esta em modo passivo/EDR. Alguns botoes podem nao ter efeito porque outro antivirus esta no comando." },

            { "Confirm.Title", "Confirmar acao" },
            { "Confirm.TurnOff", "Isso vai DESLIGAR a protecao em tempo real do antivirus. Seu PC fica mais vulneravel ate voce ligar de novo.\n\nDeseja continuar?" },
            { "Confirm.FwOff", "Isso vai DESLIGAR o firewall no perfil selecionado. Seu PC fica mais exposto na rede.\n\nDeseja continuar?" },
            { "Confirm.RemoveExclusion", "Remover esta excecao? O antivirus voltara a analisar este item." },

            { "Toast.Paused", "Protecao pausada." },
            { "Toast.Resumed", "Protecao retomada." },
            { "Toast.ScanStarted", "Verificacao rapida iniciada." },
            { "Toast.ScanDone", "Verificacao concluida." },
            { "Toast.Updated", "Definicoes atualizadas." },
            { "Toast.Blocked", "App bloqueado na internet." },
            { "Toast.Unblocked", "App liberado na internet." },

            { "Err.Generic", "Nao foi possivel concluir. Detalhe: {0}" },
            { "Err.TamperWrite", "A alteracao nao teve efeito. Provavelmente o Tamper Protection esta ativo." },
            { "Err.NeedAdmin", "Esta acao precisa de administrador." },

            { "Adv.Title", "Avancado" },
            { "Adv.Tab.Av", "Antivirus" },
            { "Adv.Tab.Exclusions", "Exclusoes" },
            { "Adv.Tab.Fw", "Firewall" },
            { "Adv.Tab.Scan", "Verificacoes" },
            { "Adv.Tab.Startup", "Inicializacao" },
            { "Adv.Tab.Security", "Seguranca da interface" },

            { "Adv.Toggle.Realtime", "Protecao em tempo real" },
            { "Adv.Toggle.Cloud", "Protecao na nuvem (MAPS)" },
            { "Adv.Toggle.Samples", "Envio automatico de amostras" },
            { "Adv.Toggle.Pua", "Bloqueio de apps indesejados (PUA)" },
            { "Adv.Toggle.Behavior", "Monitoramento de comportamento" },
            { "Adv.Toggle.Script", "Verificacao de scripts" },
            { "Adv.Toggle.Cfa", "Acesso controlado a pastas" },

            { "Adv.Exclusions.Paths", "Pastas e arquivos" },
            { "Adv.Exclusions.Ext", "Extensoes" },
            { "Adv.Exclusions.Proc", "Processos" },
            { "Adv.Exclusions.AddPath", "Adicionar pasta/arquivo..." },
            { "Adv.Exclusions.AddExt", "Adicionar extensao..." },
            { "Adv.Exclusions.AddProc", "Adicionar processo..." },

            { "Adv.Fw.Profiles", "Perfis (ligar/desligar)" },
            { "Adv.Fw.Domain", "Dominio" },
            { "Adv.Fw.Private", "Privado" },
            { "Adv.Fw.Public", "Publico" },
            { "Adv.Fw.Rules", "Regras (apps bloqueados por este painel)" },
            { "Adv.Fw.Col.Name", "Nome" },
            { "Adv.Fw.Col.Program", "Programa" },
            { "Adv.Fw.Col.Dir", "Direcao" },
            { "Adv.Fw.Col.Action", "Acao" },
            { "Adv.Fw.Note", "O firewall bloqueia APP (.exe), IP ou PORTA - nao pastas. Para excluir uma PASTA, use as exclusoes do antivirus." },

            { "Adv.Scan.Quick", "Verificacao rapida" },
            { "Adv.Scan.Full", "Verificacao completa" },
            { "Adv.Scan.Custom", "Verificacao personalizada (escolher pasta)..." },
            { "Adv.Scan.Update", "Atualizar definicoes" },

            { "Adv.Startup.Toggle", "Iniciar com o Windows (silencioso)" },
            { "Adv.Startup.Explain", "Cria uma Tarefa Agendada que abre o app elevado ao fazer logon, SEM o prompt do UAC. Desmarque para remover." },

            { "Adv.Security.Enable", "Exigir senha para abrir o Avancado e acoes sensiveis" },
            { "Adv.Security.Set", "Definir/alterar senha..." },
            { "Adv.Security.Explain", "Guardamos apenas um hash salgado (PBKDF2). A senha em si nunca e armazenada." },

            { "Pwd.Title", "Senha" },
            { "Pwd.Prompt", "Digite a senha:" },
            { "Pwd.New", "Nova senha:" },
            { "Pwd.Confirm", "Confirme a senha:" },
            { "Pwd.Wrong", "Senha incorreta." },
            { "Pwd.Mismatch", "As senhas nao coincidem." },
            { "Pwd.Empty", "A senha nao pode ficar vazia." },
            { "Pwd.Set", "Senha definida." },

            { "Field.Version", "Definicoes: {0} (atualizado em {1})" },
            { "Field.LastQuick", "Ultima verificacao rapida: {0}" },
            { "Field.Never", "nunca" },
            { "Field.On", "ligado" },
            { "Field.Off", "desligado" },
            { "Lang.Label", "Idioma:" },

            { "Dlg.SelectExe", "Selecione um programa (.exe)" },
            { "Dlg.SelectFile", "Selecione um arquivo" },
            { "Dlg.SelectFolder", "Selecione uma pasta" },
            { "BlockApp.AlreadyBlocked", "Este app ja esta bloqueado. Deseja LIBERAR o acesso a internet?" },
            { "BlockApp.NotBlocked", "Deseja BLOQUEAR o acesso a internet deste app?" },

            { "Adv.Fw.Interactive", "Modo interativo (perguntar a cada app que acessa a internet)" },
            { "Adv.Fw.Interactive.Explain", "Quando ligado, o Windows passa a BLOQUEAR a saida por padrao e este painel pergunta, a cada app novo, se voce quer permitir. Desligar restaura tudo com 1 clique." },
            { "Adv.Fw.Interactive.Warn", "ATENCAO: ligar este modo faz o Windows BLOQUEAR o acesso a internet de TODOS os apps ate voce aprovar cada um. Isso pode deixar programas (e ate o Windows Update) sem rede por alguns instantes ate a aprovacao. Voce pode desligar a qualquer momento em 1 clique (aqui ou pelo menu da bandeja).\n\nDeseja ligar o modo interativo?" },
            { "Adv.Fw.Interactive.On", "Modo interativo LIGADO." },
            { "Adv.Fw.Interactive.Off", "Modo interativo desligado; saida restaurada." },
            { "Interactive.Prompt.Title", "O app \"{0}\" quer acessar a internet." },
            { "Interactive.Prompt.Dest", "Destino: {0} : {1}" },
            { "Interactive.Prompt.Hint", "Permitir cria uma regra fixa para este programa. Bloquear mantem barrado por enquanto." },
            { "Interactive.Allow", "Permitir" },
            { "Interactive.Block", "Bloquear" },
            { "Tray.InteractiveOff", "Desligar modo interativo" },
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "App.Title", "SML Defender Control" },
            { "App.Tray.Tooltip", "SML Defender Control" },

            { "Status.Protected", "Protected" },
            { "Status.Attention", "Attention" },
            { "Status.Passive", "Passive mode" },
            { "Status.Loading", "Checking..." },
            { "Status.Protected.Sub", "Antivirus and firewall are active." },
            { "Status.Attention.Sub", "Something needs your attention. See details below." },

            { "Main.Heading", "Your protection" },
            { "Main.Av", "Antivirus (Defender)" },
            { "Main.Fw", "Windows Firewall" },

            { "Btn.Pause", "Pause protection" },
            { "Btn.Pause.Sub", "Suspends the antivirus for a while and turns itself back on." },
            { "Btn.Pause.15", "Pause for 15 minutes" },
            { "Btn.Pause.60", "Pause for 1 hour" },
            { "Btn.Pause.Reboot", "Pause until restart" },
            { "Btn.Resume", "Resume protection now" },
            { "Pause.Counter", "Protection paused. Resumes in {0}." },
            { "Pause.UntilReboot", "Protection paused until you restart the PC." },

            { "Btn.Toggle.Off", "Turn protection off" },
            { "Btn.Toggle.On", "Turn protection on" },
            { "Btn.Toggle.Sub", "Use when installing a program that needs deep access to Windows." },

            { "Btn.Trust", "Mark a file as trusted" },
            { "Btn.Trust.Sub", "Tells the antivirus not to scan a specific file/program." },

            { "Btn.ExcludeFolder", "Exclude a folder from scanning" },
            { "Btn.ExcludeFolder.Sub", "E.g. your Python/CRM project folder that the antivirus may mistake for a threat." },

            { "Btn.BlockApp", "Block / Allow an app on the internet" },
            { "Btn.BlockApp.Sub", "Pick a program (.exe) to block or allow network access." },

            { "Btn.QuickScan", "Quick scan" },
            { "Btn.QuickScan.Sub", "Scans the places where threats usually appear." },
            { "Btn.Update", "Update now" },
            { "Btn.Update.Sub", "Downloads the latest virus definitions." },

            { "Btn.Advanced", "Advanced" },
            { "Btn.Settings", "Settings" },
            { "Btn.Refresh", "Refresh status" },
            { "Btn.Close", "Close" },
            { "Btn.Open", "Open panel" },
            { "Btn.Exit", "Exit" },
            { "Btn.Ok", "OK" },
            { "Btn.Cancel", "Cancel" },
            { "Btn.Yes", "Yes" },
            { "Btn.No", "No" },
            { "Btn.Add", "Add" },
            { "Btn.Remove", "Remove" },

            { "Tamper.On", "Tamper Protection ON" },
            { "Tamper.Explain", "Tamper Protection is on. To change antivirus settings, turn it off in: Windows Security -> Virus & threat protection -> Manage settings.\n\nNote: for security, Windows does NOT allow any app (not even this one) to turn off Tamper Protection automatically — only you can, in the Windows Security screen." },
            { "Tamper.OpenAsk", "Want me to open Windows Security now, on the right screen, so you can turn Tamper Protection off?" },
            { "Tamper.Blocked", "blocked by Tamper Protection" },
            { "Passive.On", "Antivirus in passive mode" },
            { "Passive.Explain", "Defender is in passive/EDR mode. Some buttons may have no effect because another antivirus is in charge." },

            { "Confirm.Title", "Confirm action" },
            { "Confirm.TurnOff", "This will TURN OFF the antivirus real-time protection. Your PC is more vulnerable until you turn it back on.\n\nContinue?" },
            { "Confirm.FwOff", "This will TURN OFF the firewall for the selected profile. Your PC is more exposed on the network.\n\nContinue?" },
            { "Confirm.RemoveExclusion", "Remove this exclusion? The antivirus will scan this item again." },

            { "Toast.Paused", "Protection paused." },
            { "Toast.Resumed", "Protection resumed." },
            { "Toast.ScanStarted", "Quick scan started." },
            { "Toast.ScanDone", "Scan finished." },
            { "Toast.Updated", "Definitions updated." },
            { "Toast.Blocked", "App blocked on the internet." },
            { "Toast.Unblocked", "App allowed on the internet." },

            { "Err.Generic", "Could not complete. Detail: {0}" },
            { "Err.TamperWrite", "The change had no effect. Tamper Protection is probably on." },
            { "Err.NeedAdmin", "This action requires administrator." },

            { "Adv.Title", "Advanced" },
            { "Adv.Tab.Av", "Antivirus" },
            { "Adv.Tab.Exclusions", "Exclusions" },
            { "Adv.Tab.Fw", "Firewall" },
            { "Adv.Tab.Scan", "Scans" },
            { "Adv.Tab.Startup", "Startup" },
            { "Adv.Tab.Security", "Interface security" },

            { "Adv.Toggle.Realtime", "Real-time protection" },
            { "Adv.Toggle.Cloud", "Cloud-delivered protection (MAPS)" },
            { "Adv.Toggle.Samples", "Automatic sample submission" },
            { "Adv.Toggle.Pua", "Potentially unwanted app (PUA) blocking" },
            { "Adv.Toggle.Behavior", "Behavior monitoring" },
            { "Adv.Toggle.Script", "Script scanning" },
            { "Adv.Toggle.Cfa", "Controlled folder access" },

            { "Adv.Exclusions.Paths", "Folders and files" },
            { "Adv.Exclusions.Ext", "Extensions" },
            { "Adv.Exclusions.Proc", "Processes" },
            { "Adv.Exclusions.AddPath", "Add folder/file..." },
            { "Adv.Exclusions.AddExt", "Add extension..." },
            { "Adv.Exclusions.AddProc", "Add process..." },

            { "Adv.Fw.Profiles", "Profiles (on/off)" },
            { "Adv.Fw.Domain", "Domain" },
            { "Adv.Fw.Private", "Private" },
            { "Adv.Fw.Public", "Public" },
            { "Adv.Fw.Rules", "Rules (apps blocked by this panel)" },
            { "Adv.Fw.Col.Name", "Name" },
            { "Adv.Fw.Col.Program", "Program" },
            { "Adv.Fw.Col.Dir", "Direction" },
            { "Adv.Fw.Col.Action", "Action" },
            { "Adv.Fw.Note", "The firewall blocks an APP (.exe), IP or PORT - not folders. To exclude a FOLDER, use the antivirus exclusions." },

            { "Adv.Scan.Quick", "Quick scan" },
            { "Adv.Scan.Full", "Full scan" },
            { "Adv.Scan.Custom", "Custom scan (choose a folder)..." },
            { "Adv.Scan.Update", "Update definitions" },

            { "Adv.Startup.Toggle", "Start with Windows (silent)" },
            { "Adv.Startup.Explain", "Creates a Scheduled Task that opens the app elevated at logon, WITHOUT the UAC prompt. Uncheck to remove." },

            { "Adv.Security.Enable", "Require a password to open Advanced and sensitive actions" },
            { "Adv.Security.Set", "Set/change password..." },
            { "Adv.Security.Explain", "We only store a salted hash (PBKDF2). The password itself is never stored." },

            { "Pwd.Title", "Password" },
            { "Pwd.Prompt", "Enter the password:" },
            { "Pwd.New", "New password:" },
            { "Pwd.Confirm", "Confirm password:" },
            { "Pwd.Wrong", "Wrong password." },
            { "Pwd.Mismatch", "Passwords do not match." },
            { "Pwd.Empty", "Password cannot be empty." },
            { "Pwd.Set", "Password set." },

            { "Field.Version", "Definitions: {0} (updated {1})" },
            { "Field.LastQuick", "Last quick scan: {0}" },
            { "Field.Never", "never" },
            { "Field.On", "on" },
            { "Field.Off", "off" },
            { "Lang.Label", "Language:" },

            { "Dlg.SelectExe", "Select a program (.exe)" },
            { "Dlg.SelectFile", "Select a file" },
            { "Dlg.SelectFolder", "Select a folder" },
            { "BlockApp.AlreadyBlocked", "This app is already blocked. Do you want to ALLOW internet access?" },
            { "BlockApp.NotBlocked", "Do you want to BLOCK this app's internet access?" },

            { "Adv.Fw.Interactive", "Interactive mode (ask for each app that accesses the internet)" },
            { "Adv.Fw.Interactive.Explain", "When on, Windows BLOCKS outbound by default and this panel asks, for each new app, whether to allow it. Turning it off restores everything with one click." },
            { "Adv.Fw.Interactive.Warn", "WARNING: turning this on makes Windows BLOCK internet access for ALL apps until you approve each one. This may leave programs (even Windows Update) without network for a moment until approval. You can turn it off anytime with one click (here or from the tray menu).\n\nTurn interactive mode on?" },
            { "Adv.Fw.Interactive.On", "Interactive mode ON." },
            { "Adv.Fw.Interactive.Off", "Interactive mode off; outbound restored." },
            { "Interactive.Prompt.Title", "The app \"{0}\" wants to access the internet." },
            { "Interactive.Prompt.Dest", "Destination: {0} : {1}" },
            { "Interactive.Prompt.Hint", "Allow creates a permanent rule for this program. Block keeps it barred for now." },
            { "Interactive.Allow", "Allow" },
            { "Interactive.Block", "Block" },
            { "Tray.InteractiveOff", "Turn off interactive mode" },
        };
    }
}
