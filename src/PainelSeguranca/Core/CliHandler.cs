using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PainelSeguranca.Services;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// MODO SILENCIOSO (CLI). Acionado por Program.Main APENAS quando o .exe recebe
    /// argumentos de linha de comando. Neste modo o app NUNCA abre MainForm nem
    /// TrayApplicationContext, NUNCA cria o Mutex de instancia unica e NUNCA chama
    /// Application.Run. Ele executa uma unica operacao, mostra um MessageBox com o
    /// resultado e encerra o processo (Environment.Exit).
    ///
    /// Existe para viabilizar o menu de contexto do Windows Explorer (bloquear app,
    /// excluir do Defender etc.) chamando o proprio .exe com argumentos.
    ///
    /// Argumentos suportados:
    ///   --block    &lt;caminho.exe&gt;          Bloqueia o .exe no firewall (Inbound + Outbound).
    ///   --unblock  &lt;caminho.exe&gt;          Remove TODOS os bloqueios do firewall do .exe (qualquer origem).
    ///   --add-excl &lt;arquivo ou pasta&gt;     Adiciona exclusao no Defender.
    ///   --rem-excl &lt;arquivo ou pasta&gt;     Remove exclusao do Defender.
    /// </summary>
    public static class CliHandler
    {
        /// <summary>
        /// Ponto de entrada do modo silencioso. NUNCA retorna: sempre termina via
        /// Environment.Exit(0) em sucesso ou Environment.Exit(1) em erro.
        /// </summary>
        public static void Run(string[] args)
        {
            // Visual styles para o MessageBox renderizar corretamente (sem isso, dialogos
            // antigos). Seguro: nenhum Form gerenciado e criado no modo silencioso.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Inicializa apenas o minimo: AppSettings + idioma (e, por consequencia, o Logger
            // na primeira escrita). I18n.LoadFromSettings toca AppSettings.Instance.
            try { I18n.LoadFromSettings(); } catch { /* idioma cai no padrao pt-BR */ }

            Logger.Info("Modo silencioso (CLI) iniciado: " + string.Join(" ", args ?? new string[0]));

            // ---------- Elevacao ----------
            // Toda alteracao no Defender/Firewall exige admin. Se nao estiver elevado,
            // reabre o proprio .exe elevado (verbo runas) PASSANDO OS MESMOS ARGUMENTOS,
            // e encerra esta instancia (a elevada assume).
            if (!ElevationHelper.IsElevated())
            {
                Logger.Warn("CLI sem elevacao. Reabrindo elevado com os mesmos argumentos.");
                if (ElevationHelper.RelaunchElevated(BuildArguments(args)))
                    Environment.Exit(0);

                // Usuario recusou o UAC: nao da para concluir sem admin.
                Fail(L("Esta acao precisa de administrador.",
                       "This action requires administrator."));
            }

            // ---------- Parse ----------
            string cmd = (args != null && args.Length > 0 && args[0] != null) ? args[0].Trim() : "";
            string path = (args != null && args.Length > 1 && args[1] != null) ? args[1].Trim() : "";

            switch (cmd.ToLowerInvariant())
            {
                case "--block":
                    RequireExe(path);
                    Logger.Info("CLI --block: " + path);
                    Handle(FirewallService.BlockAppAsync(path),
                        L("Acesso a internet BLOQUEADO para:", "Internet access BLOCKED for:") + "\n" + path);
                    break;

                case "--unblock":
                    RequireExe(path);
                    Logger.Info("CLI --unblock: " + path);
                    // AllowAppAsync remove TODAS as regras de bloqueio do .exe (qualquer origem),
                    // nao apenas as criadas por este app: no menu de contexto o usuario quer
                    // "destravar de vez".
                    Handle(FirewallService.AllowAppAsync(path),
                        L("Bloqueio de internet REMOVIDO para:", "Internet block REMOVED for:") + "\n" + path);
                    break;

                case "--add-excl":
                    RequirePath(path);
                    Logger.Info("CLI --add-excl: " + path);
                    Handle(DefenderService.AddPathExclusionAsync(path),
                        L("Exclusao ADICIONADA ao Defender:", "Exclusion ADDED to Defender:") + "\n" + path);
                    break;

                case "--rem-excl":
                    RequirePath(path);
                    Logger.Info("CLI --rem-excl: " + path);
                    Handle(DefenderService.RemovePathExclusionAsync(path),
                        L("Exclusao REMOVIDA do Defender:", "Exclusion REMOVED from Defender:") + "\n" + path);
                    break;

                default:
                    Fail(L("Argumento desconhecido: ", "Unknown argument: ") + cmd + "\n\n" + UsageText());
                    break;
            }

            // Cada caminho acima termina o processo via Environment.Exit. Rede de seguranca:
            Environment.Exit(1);
        }

        // ----------------------------- Execucao / resultado -----------------------------

        /// <summary>
        /// Executa a operacao de forma SINCRONA (aceitavel no modo silencioso: nao ha UI
        /// thread para bloquear) e mostra o resultado, encerrando o processo.
        /// </summary>
        private static void Handle(Task<OperationResult> operation, string okMessage)
        {
            OperationResult r;
            try
            {
                r = operation.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Error("CLI: excecao ao executar a operacao", ex);
                Fail(I18n.T("Err.Generic", ex.Message));
                return; // inalcancavel (Fail chama Exit)
            }

            if (r == null)
            {
                Fail(I18n.T("Err.Generic", "null"));
                return;
            }

            if (r.Success) Ok(okMessage);
            else if (r.TamperBlocked) Tamper();
            else Fail(r.Message ?? I18n.T("Err.Generic", ""));
        }

        private static void Ok(string message)
        {
            Logger.Info("CLI resultado: SUCESSO - " + OneLine(message));
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Environment.Exit(0);
        }

        private static void Fail(string message)
        {
            Logger.Warn("CLI resultado: FALHA - " + OneLine(message));
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.Exit(1);
        }

        private static void Tamper()
        {
            string message = I18n.T("Tamper.Explain");
            Logger.Warn("CLI resultado: bloqueado pelo Tamper Protection.");
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.Exit(1);
        }

        // ----------------------------- Validacoes -----------------------------

        /// <summary>Exige um caminho nao vazio (arquivo ou pasta). Encerra em erro.</summary>
        private static void RequirePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                Fail(L("Caminho nao informado.", "No path provided.") + "\n\n" + UsageText());
        }

        /// <summary>Exige um caminho nao vazio terminando em .exe (case-insensitive). Encerra em erro.</summary>
        private static void RequireExe(string path)
        {
            RequirePath(path);
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Fail(L("Este comando so aceita arquivos .exe.", "This command only accepts .exe files.")
                     + "\n" + path);
        }

        // ----------------------------- Helpers -----------------------------

        private static string Title { get { return I18n.T("App.Title"); } }

        /// <summary>Seleciona PT-BR ou EN conforme o idioma ja carregado.</summary>
        private static string L(string pt, string en)
        {
            return I18n.Language == "en" ? en : pt;
        }

        private static string UsageText()
        {
            return L(
                "Uso:\n" +
                "  SMLDefenderControl.exe --block    <caminho.exe>\n" +
                "  SMLDefenderControl.exe --unblock  <caminho.exe>\n" +
                "  SMLDefenderControl.exe --add-excl <arquivo ou pasta>\n" +
                "  SMLDefenderControl.exe --rem-excl <arquivo ou pasta>",
                "Usage:\n" +
                "  SMLDefenderControl.exe --block    <path.exe>\n" +
                "  SMLDefenderControl.exe --unblock  <path.exe>\n" +
                "  SMLDefenderControl.exe --add-excl <file or folder>\n" +
                "  SMLDefenderControl.exe --rem-excl <file or folder>");
        }

        /// <summary>Reconstroi a linha de comando original, com aspas onde necessario,
        /// para reenviar ao processo elevado.</summary>
        private static string BuildArguments(string[] args)
        {
            if (args == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                string s = a ?? string.Empty;
                if (s.Length == 0 || s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }
            return sb.ToString();
        }

        private static string OneLine(string s)
        {
            return (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
