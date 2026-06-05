using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>Informacao de um app barrado pelo firewall (evento 5157).</summary>
    public sealed class BlockedAppInfo
    {
        public string ApplicationPath { get; set; }  // caminho amigavel (C:\...)
        public string RawPath { get; set; }          // caminho NT (\device\...)
        public string RemoteAddress { get; set; }
        public string RemotePort { get; set; }
    }

    /// <summary>
    /// MODO INTERATIVO DO FIREWALL — Caminho A (escolhido pelo usuario).
    ///
    /// Estrategia (100% APIs oficiais do Windows Firewall, SEM driver):
    /// 1) Define a acao padrao de SAIDA como "Bloquear" (Set-NetFirewallProfile -DefaultOutboundAction Block).
    /// 2) Liga a auditoria da Plataforma de Filtragem (auditpol, subcategoria por GUID p/ evitar localizacao),
    ///    o que faz o Windows registrar o evento de Seguranca 5157 quando uma conexao e bloqueada.
    /// 3) Monitora o log de Seguranca (EventLogWatcher) por 5157 de SAIDA; ao detectar um app novo,
    ///    dispara o evento AppBlocked para a UI mostrar o pop-up "Permitir / Bloquear".
    /// 4) "Permitir" cria uma regra Allow de saida para aquele .exe; "Bloquear" deixa como esta (continua barrado).
    ///
    /// AVISO (assumido pelo usuario): "bloquear por padrao" e agressivo — apps ficam sem rede ate serem
    /// aprovados. O recurso e OPT-IN e desligavel em 1 clique (restaura DefaultOutboundAction = Allow).
    /// </summary>
    public static class InteractiveFirewallService
    {
        // Subcategoria de auditoria "Filtering Platform Connection" (GUID fixo, independe de idioma).
        private const string AuditSubcategoryGuid = "{0CCE9226-69AE-11D9-BED3-505054503030}";
        public const string AllowGroup = "PainelSeguranca_Interativo";

        private static readonly object Gate = new object();
        private static EventLogWatcher _watcher;
        private static readonly HashSet<string> _handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> _denied = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Disparado (em thread de fundo) quando um app novo e barrado. A UI deve marshalar.</summary>
        public static event Action<BlockedAppInfo> AppBlocked;

        public static bool IsActive { get; private set; }

        // ----------------------------- Ligar / desligar -----------------------------

        /// <summary>Liga o modo interativo: default-block de saida + auditoria + watcher.</summary>
        public static async Task<OperationResult> EnableAsync()
        {
            // 1) Acao padrao de saida = Bloquear (em todos os perfis).
            var r1 = await ProcessRunner.RunPowerShellAsync(
                "Set-NetFirewallProfile -All -DefaultOutboundAction Block -ErrorAction Stop", 30000);
            if (!r1.Success)
                return OperationResult.Fail(I18n.T("Err.Generic", r1.CombinedError));

            // 2) Liga a auditoria da Plataforma de Filtragem (sucesso e falha -> evento 5157).
            await ProcessRunner.RunAsync("auditpol.exe",
                "/set /subcategory:" + AuditSubcategoryGuid + " /success:enable /failure:enable", 20000);

            // 3) Inicia o monitor de eventos.
            StartWatcher();

            IsActive = true;
            AppSettings.Instance.InteractiveFirewall = true;
            Logger.Info("Modo interativo do firewall LIGADO (Caminho A).");
            return OperationResult.Ok();
        }

        /// <summary>Desliga em 1 clique: restaura saida = Permitir e para o watcher.</summary>
        public static async Task<OperationResult> DisableAsync()
        {
            StopWatcher();
            IsActive = false;
            AppSettings.Instance.InteractiveFirewall = false;

            // Restaura a acao padrao de saida para Permitir (estado normal do Windows).
            var r = await ProcessRunner.RunPowerShellAsync(
                "Set-NetFirewallProfile -All -DefaultOutboundAction Allow -ErrorAction Stop", 30000);

            // Desliga a auditoria que ligamos (volta ao padrao).
            await ProcessRunner.RunAsync("auditpol.exe",
                "/set /subcategory:" + AuditSubcategoryGuid + " /success:disable /failure:disable", 20000);

            lock (Gate) { _handled.Clear(); _denied.Clear(); }
            Logger.Info("Modo interativo do firewall DESLIGADO; saida restaurada para Permitir.");
            return r.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", r.CombinedError));
        }

        /// <summary>
        /// Re-arma o watcher no startup caso o modo estivesse ligado (sem mexer no default ja aplicado).
        /// Garante tambem que a auditoria esteja ligada.
        /// </summary>
        public static async Task ReArmIfNeededAsync()
        {
            if (!AppSettings.Instance.InteractiveFirewall) return;
            await ProcessRunner.RunAsync("auditpol.exe",
                "/set /subcategory:" + AuditSubcategoryGuid + " /success:enable /failure:enable", 20000);
            StartWatcher();
            IsActive = true;
            Logger.Info("Modo interativo re-armado no startup.");
        }

        // ----------------------------- Decisao do usuario -----------------------------

        /// <summary>Permite o app: cria regra Allow de saida (e entrada) para o .exe.</summary>
        public static async Task<OperationResult> AllowAppAsync(string exePath)
        {
            string file = Path.GetFileName(exePath);
            string p = exePath.Replace("'", "''");
            string name = "SML Defender Control: Permitido - " + file;
            string cmd =
                "New-NetFirewallRule -DisplayName '" + name.Replace("'", "''") + "' -Group '" + AllowGroup +
                "' -Direction Outbound -Program '" + p + "' -Action Allow -Profile Any -ErrorAction Stop";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success)
            {
                lock (Gate) { _handled.Add(NormalizePath(exePath)); }
                Logger.Info("Modo interativo: app permitido -> " + exePath);
                return OperationResult.Ok();
            }
            return OperationResult.Fail(I18n.T("Err.Generic", r.CombinedError));
        }

        /// <summary>Mantem o app bloqueado nesta sessao (nao cria regra; continua barrado).</summary>
        public static void DenyAppForSession(string exePath)
        {
            string key = NormalizePath(exePath);
            _denied[key] = 1;
            lock (Gate) { _handled.Add(key); }
            Logger.Info("Modo interativo: app mantido bloqueado -> " + exePath);
        }

        // ----------------------------- Watcher de eventos -----------------------------

        private static void StartWatcher()
        {
            lock (Gate)
            {
                if (_watcher != null) return;
                try
                {
                    // 5157 = "A Plataforma de Filtragem do Windows bloqueou uma conexao".
                    var query = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=5157)]]");
                    _watcher = new EventLogWatcher(query);
                    _watcher.EventRecordWritten += OnEventWritten;
                    _watcher.Enabled = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("Falha ao iniciar o monitor de eventos do firewall", ex);
                    _watcher = null;
                }
            }
        }

        private static void StopWatcher()
        {
            lock (Gate)
            {
                if (_watcher == null) return;
                try { _watcher.Enabled = false; _watcher.Dispose(); }
                catch { }
                _watcher = null;
            }
        }

        private static void OnEventWritten(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord == null) return;
                string xml = e.EventRecord.ToXml();
                var data = ParseEventData(xml);

                string direction;
                data.TryGetValue("Direction", out direction);
                // So nos interessam bloqueios de SAIDA (%%14593 = Outbound).
                if (direction != null && direction.IndexOf("14593", StringComparison.Ordinal) < 0 &&
                    direction.IndexOf("Outbound", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                string rawApp;
                if (!data.TryGetValue("Application", out rawApp) || string.IsNullOrEmpty(rawApp)) return;

                string friendly = ConvertDevicePath(rawApp);
                string key = NormalizePath(friendly);

                lock (Gate)
                {
                    if (_handled.Contains(key)) return; // ja perguntamos/decidimos
                    _handled.Add(key);                  // marca como "em tratamento" p/ nao floodar
                }
                if (_denied.ContainsKey(key)) return;

                string remoteAddr, remotePort;
                data.TryGetValue("DestAddress", out remoteAddr);
                data.TryGetValue("DestPort", out remotePort);

                var info = new BlockedAppInfo
                {
                    ApplicationPath = friendly,
                    RawPath = rawApp,
                    RemoteAddress = remoteAddr,
                    RemotePort = remotePort
                };

                var h = AppBlocked;
                if (h != null) h(info);
            }
            catch (Exception ex)
            {
                Logger.Warn("Erro ao processar evento 5157: " + ex.Message);
            }
        }

        private static Dictionary<string, string> ParseEventData(string xml)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                var nodes = doc.SelectNodes("//e:EventData/e:Data", nsmgr);
                if (nodes != null)
                {
                    foreach (XmlNode n in nodes)
                    {
                        var nameAttr = n.Attributes != null ? n.Attributes["Name"] : null;
                        if (nameAttr != null) dict[nameAttr.Value] = n.InnerText;
                    }
                }
            }
            catch { }
            return dict;
        }

        // ----------------------------- Conversao de caminho NT -> letra -----------------------------

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        /// <summary>Converte "\device\harddiskvolumeN\..." para "C:\...".</summary>
        private static string ConvertDevicePath(string ntPath)
        {
            if (string.IsNullOrEmpty(ntPath)) return ntPath;
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    string letter = drive.Name.TrimEnd('\\'); // "C:"
                    var sb = new StringBuilder(260);
                    if (QueryDosDevice(letter, sb, sb.Capacity) != 0)
                    {
                        string dev = sb.ToString(); // "\Device\HarddiskVolume3"
                        if (ntPath.StartsWith(dev, StringComparison.OrdinalIgnoreCase))
                            return letter + ntPath.Substring(dev.Length);
                    }
                }
            }
            catch { }
            return ntPath; // fallback: devolve o caminho cru
        }

        private static string NormalizePath(string p)
        {
            return (p ?? string.Empty).Trim().TrimEnd('\\');
        }
    }
}
