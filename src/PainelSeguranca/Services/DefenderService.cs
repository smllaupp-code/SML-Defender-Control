using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>Resultado de uma operacao que altera configuracao do antivirus.</summary>
    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool TamperBlocked { get; set; }

        public static OperationResult Ok(string msg = null) { return new OperationResult { Success = true, Message = msg }; }
        public static OperationResult Fail(string msg) { return new OperationResult { Success = false, Message = msg }; }
        public static OperationResult Tamper(string msg) { return new OperationResult { Success = false, TamperBlocked = true, Message = msg }; }
    }

    /// <summary>
    /// Camada do ANTIVIRUS (Microsoft Defender).
    /// - Leitura: WMI em root\Microsoft\Windows\Defender (MSFT_MpComputerStatus / MSFT_MpPreference).
    /// - Escrita: powershell.exe oculto com Set/Add/Remove-MpPreference. Apos cada escrita,
    ///   relemos por WMI e confirmamos. Se nao aplicou e o Tamper Protection esta ativo,
    ///   reportamos isso (NUNCA tentamos contornar).
    /// - Varreduras/atualizacao: MpCmdRun.exe (com fallback Start-MpScan/Update-MpSignature).
    /// </summary>
    public static class DefenderService
    {
        private const string WmiNamespace = @"root\Microsoft\Windows\Defender";

        // ----------------------------- LEITURA (WMI) -----------------------------

        public static Task<DefenderStatus> ReadStatusAsync()
        {
            return Task.Run(() => ReadStatus());
        }

        public static DefenderStatus ReadStatus()
        {
            var st = new DefenderStatus();
            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpComputerStatus")))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        st.RealTimeProtectionEnabled = GetBool(mo, "RealTimeProtectionEnabled");
                        st.IsTamperProtected = GetBool(mo, "IsTamperProtected");
                        st.AMRunningMode = GetString(mo, "AMRunningMode");
                        st.AntivirusSignatureVersion = GetString(mo, "AntivirusSignatureVersion");
                        st.AntivirusSignatureLastUpdated = GetDate(mo, "AntivirusSignatureLastUpdated");
                        st.QuickScanAge = GetUInt(mo, "QuickScanAge");
                        st.FullScanAge = GetUInt(mo, "FullScanAge");
                        break;
                    }
                }

                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpPreference")))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        st.DisableRealtimeMonitoring = GetBool(mo, "DisableRealtimeMonitoring");
                        st.DisableBehaviorMonitoring = GetBool(mo, "DisableBehaviorMonitoring");
                        st.DisableScriptScanning = GetBool(mo, "DisableScriptScanning");
                        st.PUAProtection = (int)GetUInt(mo, "PUAProtection").GetValueOrDefault();
                        st.MAPSReporting = (int)GetUInt(mo, "MAPSReporting").GetValueOrDefault();
                        st.SubmitSamplesConsent = (int)GetUInt(mo, "SubmitSamplesConsent").GetValueOrDefault();
                        st.EnableControlledFolderAccess = (int)GetUInt(mo, "EnableControlledFolderAccess").GetValueOrDefault();
                        st.ExclusionPath = GetStringArray(mo, "ExclusionPath");
                        st.ExclusionExtension = GetStringArray(mo, "ExclusionExtension");
                        st.ExclusionProcess = GetStringArray(mo, "ExclusionProcess");
                        break;
                    }
                }

                st.Available = true;
            }
            catch (Exception ex)
            {
                // No Windows 8.1 com superficie reduzida ou se o servico estiver indisponivel,
                // o namespace/classe pode nao existir. Degrade com elegancia.
                st.Available = false;
                st.ErrorMessage = ex.Message;
                Logger.Warn("Defender WMI indisponivel: " + ex.Message);
            }
            return st;
        }

        // ----------------------------- ESCRITA (PowerShell + confirmacao) -----------------------------

        /// <summary>Liga/desliga a protecao em tempo real, confirmando por WMI.</summary>
        public static async Task<OperationResult> SetRealtimeProtectionAsync(bool enabled)
        {
            string disable = enabled ? "$false" : "$true";
            string cmd = "Set-MpPreference -DisableRealtimeMonitoring " + disable + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => s.RealtimeActive == enabled);
        }

        public static async Task<OperationResult> SetBehaviorMonitoringAsync(bool enabled)
        {
            string cmd = "Set-MpPreference -DisableBehaviorMonitoring " + (enabled ? "$false" : "$true") + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => s.DisableBehaviorMonitoring == !enabled);
        }

        public static async Task<OperationResult> SetScriptScanningAsync(bool enabled)
        {
            string cmd = "Set-MpPreference -DisableScriptScanning " + (enabled ? "$false" : "$true") + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => s.DisableScriptScanning == !enabled);
        }

        public static async Task<OperationResult> SetPuaProtectionAsync(bool enabled)
        {
            // 1 = Enabled, 0 = Disabled
            string cmd = "Set-MpPreference -PUAProtection " + (enabled ? "1" : "0") + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => (s.PUAProtection == 1) == enabled);
        }

        public static async Task<OperationResult> SetCloudMapsAsync(bool enabled)
        {
            // 2 = Advanced, 0 = Disabled
            string cmd = "Set-MpPreference -MAPSReporting " + (enabled ? "2" : "0") + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => (s.MAPSReporting > 0) == enabled);
        }

        public static async Task<OperationResult> SetSampleSubmissionAsync(bool enabled)
        {
            // 1 = Send safe samples, 2 = Never send
            string cmd = "Set-MpPreference -SubmitSamplesConsent " + (enabled ? "1" : "2") + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => (s.SubmitSamplesConsent != 2) == enabled);
        }

        public static async Task<OperationResult> SetControlledFolderAccessAsync(bool enabled)
        {
            // Enabled / Disabled
            string val = enabled ? "Enabled" : "Disabled";
            string cmd = "Set-MpPreference -EnableControlledFolderAccess " + val + " -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => (s.EnableControlledFolderAccess == 1) == enabled);
        }

        // ----- Exclusoes -----

        public static async Task<OperationResult> AddPathExclusionAsync(string path)
        {
            string cmd = "Add-MpPreference -ExclusionPath '" + Escape(path) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => ContainsPath(s.ExclusionPath, path));
        }

        public static async Task<OperationResult> RemovePathExclusionAsync(string path)
        {
            string cmd = "Remove-MpPreference -ExclusionPath '" + Escape(path) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => !ContainsPath(s.ExclusionPath, path));
        }

        public static async Task<OperationResult> AddProcessExclusionAsync(string process)
        {
            string cmd = "Add-MpPreference -ExclusionProcess '" + Escape(process) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => ContainsPath(s.ExclusionProcess, process));
        }

        public static async Task<OperationResult> RemoveProcessExclusionAsync(string process)
        {
            string cmd = "Remove-MpPreference -ExclusionProcess '" + Escape(process) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => !ContainsPath(s.ExclusionProcess, process));
        }

        public static async Task<OperationResult> AddExtensionExclusionAsync(string ext)
        {
            ext = ext.TrimStart('.', '*');
            string cmd = "Add-MpPreference -ExclusionExtension '" + Escape(ext) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => s.ExclusionExtension.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
        }

        public static async Task<OperationResult> RemoveExtensionExclusionAsync(string ext)
        {
            ext = ext.TrimStart('.', '*');
            string cmd = "Remove-MpPreference -ExclusionExtension '" + Escape(ext) + "' -ErrorAction Stop";
            return await ApplyAndConfirmAsync(cmd, s => !s.ExclusionExtension.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Executa o comando PowerShell e CONFIRMA relendo por WMI. Trata Tamper Protection:
        /// se o estado nao mudou e o Tamper esta ativo, devolve TamperBlocked.
        /// </summary>
        private static async Task<OperationResult> ApplyAndConfirmAsync(string psCommand, Func<DefenderStatus, bool> confirm)
        {
            var psResult = await ProcessRunner.RunPowerShellAsync(psCommand);
            Logger.Info("PS: " + psCommand + " | exit=" + psResult.ExitCode);

            // Da um instante para o WMI refletir e relê.
            await Task.Delay(400);
            var after = await ReadStatusAsync();

            if (!after.Available)
                return OperationResult.Fail(I18n.T("Err.Generic", after.ErrorMessage ?? "WMI indisponivel"));

            if (confirm(after))
                return OperationResult.Ok();

            // Nao aplicou: se Tamper esta ativo, esse e o motivo mais provavel.
            if (after.IsTamperProtected)
                return OperationResult.Tamper(I18n.T("Err.TamperWrite"));

            string detail = psResult.Success
                ? "A configuracao nao foi aplicada."
                : psResult.CombinedError;
            return OperationResult.Fail(I18n.T("Err.Generic", detail));
        }

        // ----------------------------- VARREDURAS / ATUALIZACAO -----------------------------

        public static Task<OperationResult> QuickScanAsync() { return ScanAsync(1, null, "QuickScan"); }
        public static Task<OperationResult> FullScanAsync() { return ScanAsync(2, null, "FullScan"); }
        public static Task<OperationResult> CustomScanAsync(string path) { return ScanAsync(3, path, "CustomScan"); }

        private static async Task<OperationResult> ScanAsync(int scanType, string path, string psScanType)
        {
            string mp = ResolveMpCmdRun();
            if (mp != null)
            {
                string args = "-Scan -ScanType " + scanType;
                if (scanType == 3 && !string.IsNullOrEmpty(path))
                    args += " -File \"" + path + "\"";
                // Varredura pode demorar; timeout generoso.
                int timeout = scanType == 2 ? 60 * 60 * 1000 : 30 * 60 * 1000;
                var r = await ProcessRunner.RunAsync(mp, args, timeout);
                // MpCmdRun retorna 0 quando nada foi encontrado e 2 quando encontrou ameaca; ambos = scan rodou.
                if (r.Launched && !r.TimedOut)
                    return OperationResult.Ok();
                Logger.Warn("MpCmdRun scan falhou, tentando fallback. " + r.CombinedError);
            }

            // Fallback: Start-MpScan
            string cmd = "Start-MpScan -ScanType " + psScanType;
            if (scanType == 3 && !string.IsNullOrEmpty(path))
                cmd += " -ScanPath '" + Escape(path) + "'";
            cmd += " -ErrorAction Stop";
            var ps = await ProcessRunner.RunPowerShellAsync(cmd, scanType == 2 ? 60 * 60 * 1000 : 30 * 60 * 1000);
            return ps.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", ps.CombinedError));
        }

        public static async Task<OperationResult> UpdateSignaturesAsync()
        {
            string mp = ResolveMpCmdRun();
            if (mp != null)
            {
                var r = await ProcessRunner.RunAsync(mp, "-SignatureUpdate", 10 * 60 * 1000);
                if (r.Launched && !r.TimedOut && r.ExitCode == 0)
                    return OperationResult.Ok();
                Logger.Warn("MpCmdRun update falhou, tentando fallback. " + r.CombinedError);
            }
            var ps = await ProcessRunner.RunPowerShellAsync("Update-MpSignature -ErrorAction Stop", 10 * 60 * 1000);
            return ps.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", ps.CombinedError));
        }

        /// <summary>
        /// Resolve o caminho do MpCmdRun.exe:
        /// 1) %ProgramData%\Microsoft\Windows Defender\Platform\&lt;versao mais nova&gt;\MpCmdRun.exe
        /// 2) %ProgramFiles%\Windows Defender\MpCmdRun.exe (fallback)
        /// </summary>
        public static string ResolveMpCmdRun()
        {
            try
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string platformDir = Path.Combine(programData, "Microsoft", "Windows Defender", "Platform");
                if (Directory.Exists(platformDir))
                {
                    var best = Directory.GetDirectories(platformDir)
                        .Select(d => new { Dir = d, Ver = ParseVersion(Path.GetFileName(d)) })
                        .Where(x => x.Ver != null)
                        .OrderByDescending(x => x.Ver)
                        .Select(x => Path.Combine(x.Dir, "MpCmdRun.exe"))
                        .FirstOrDefault(File.Exists);
                    if (best != null) return best;
                }

                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string legacy = Path.Combine(pf, "Windows Defender", "MpCmdRun.exe");
                if (File.Exists(legacy)) return legacy;
            }
            catch (Exception ex)
            {
                Logger.Warn("Falha ao resolver MpCmdRun: " + ex.Message);
            }
            return null; // chamador usa fallback PowerShell
        }

        private static Version ParseVersion(string s)
        {
            Version v;
            return Version.TryParse(s, out v) ? v : null;
        }

        // ----------------------------- helpers WMI -----------------------------

        private static bool GetBool(ManagementObject mo, string name)
        {
            try { var v = mo[name]; return v != null && Convert.ToBoolean(v); }
            catch { return false; }
        }

        private static string GetString(ManagementObject mo, string name)
        {
            try { var v = mo[name]; return v == null ? null : v.ToString(); }
            catch { return null; }
        }

        private static uint? GetUInt(ManagementObject mo, string name)
        {
            try { var v = mo[name]; return v == null ? (uint?)null : Convert.ToUInt32(v); }
            catch { return null; }
        }

        private static DateTime? GetDate(ManagementObject mo, string name)
        {
            try
            {
                var v = mo[name];
                if (v == null) return null;
                // Pode vir como DMTF string ou DateTime.
                if (v is DateTime) return (DateTime)v;
                string s = v.ToString();
                return ManagementDateTimeConverter.ToDateTime(s);
            }
            catch { return null; }
        }

        private static List<string> GetStringArray(ManagementObject mo, string name)
        {
            var list = new List<string>();
            try
            {
                var v = mo[name];
                if (v is string[] arr) list.AddRange(arr.Where(x => !string.IsNullOrEmpty(x)));
                else if (v is string s && !string.IsNullOrEmpty(s)) list.Add(s);
            }
            catch { }
            return list;
        }

        private static bool ContainsPath(List<string> list, string path)
        {
            if (list == null) return false;
            string norm = (path ?? "").TrimEnd('\\');
            return list.Any(p => string.Equals(p.TrimEnd('\\'), norm, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Escapa aspas simples para uso seguro dentro de '...' no PowerShell.</summary>
        private static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("'", "''");
        }
    }
}
