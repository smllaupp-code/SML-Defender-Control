using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>Uma regra de firewall criada por este painel.</summary>
    public sealed class FirewallRuleInfo
    {
        public string DisplayName { get; set; }
        public string Program { get; set; }
        public string Direction { get; set; }
        public string Action { get; set; }
        public string Profile { get; set; }

        public bool IsBlock { get { return (Action ?? "").IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool IsAllow { get { return (Action ?? "").IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0; } }
    }

    /// <summary>
    /// Camada do FIREWALL do Windows.
    /// - Estado: Get-NetFirewallProfile.
    /// - Regras: New/Get/Remove-NetFirewallRule (+ Get-NetFirewallApplicationFilter).
    /// - Ligar/desligar por perfil: Set-NetFirewallProfile.
    /// - Fallback netsh advfirewall (Windows 8.1 ou se faltar o cmdlet).
    ///
    /// IMPORTANTE: o firewall bloqueia APP (.exe), IP ou PORTA — NAO pastas.
    /// Exclusao de PASTA e funcao do antivirus (ver DefenderService).
    /// </summary>
    public static class FirewallService
    {
        // Todas as regras criadas aqui ficam neste grupo, para localizar/remover com seguranca.
        public const string RuleGroup = "PainelSeguranca";

        // ----------------------------- ESTADO -----------------------------

        public static Task<FirewallStatus> ReadStatusAsync()
        {
            return Task.Run(async () => await ReadStatusInternal());
        }

        private static async Task<FirewallStatus> ReadStatusInternal()
        {
            var st = new FirewallStatus();
            string cmd =
                "Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | " +
                "ForEach-Object { \"$($_.Name)=$($_.Enabled)\" }";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success)
            {
                foreach (var line in SplitLines(r.StdOut))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = line.Substring(0, eq).Trim();
                    bool on = ParseEnabled(line.Substring(eq + 1).Trim());
                    if (name.Equals("Domain", StringComparison.OrdinalIgnoreCase)) st.DomainEnabled = on;
                    else if (name.Equals("Private", StringComparison.OrdinalIgnoreCase)) st.PrivateEnabled = on;
                    else if (name.Equals("Public", StringComparison.OrdinalIgnoreCase)) st.PublicEnabled = on;
                }
                st.Available = true;
                return st;
            }

            // Fallback netsh (best-effort; saida pode ser localizada).
            Logger.Warn("Get-NetFirewallProfile falhou, usando netsh. " + r.CombinedError);
            return await ReadStatusNetsh();
        }

        private static async Task<FirewallStatus> ReadStatusNetsh()
        {
            var st = new FirewallStatus();
            var r = await ProcessRunner.RunAsync("netsh.exe", "advfirewall show allprofiles state", 30000);
            if (!r.Launched)
            {
                st.Available = false;
                st.ErrorMessage = r.CombinedError;
                return st;
            }
            // Detecta blocos por perfil. netsh imprime "Domain Profile Settings:" etc. (ou localizado).
            // Estrategia robusta: percorre linhas, lembrando o ultimo perfil citado e o ultimo estado ON/OFF.
            string lower = r.StdOut.ToLowerInvariant();
            st.DomainEnabled = NetshProfileOn(lower, "domain");
            st.PrivateEnabled = NetshProfileOn(lower, "private");
            st.PublicEnabled = NetshProfileOn(lower, "public");
            st.Available = true;
            return st;
        }

        private static bool NetshProfileOn(string lowerOutput, string profileKeyword)
        {
            int idx = lowerOutput.IndexOf(profileKeyword, StringComparison.Ordinal);
            if (idx < 0) return false;
            // Procura "on"/"off" logo apos o cabecalho do perfil.
            int stateIdx = lowerOutput.IndexOf("state", idx, StringComparison.Ordinal);
            if (stateIdx < 0) stateIdx = idx;
            string window = lowerOutput.Substring(stateIdx, Math.Min(40, lowerOutput.Length - stateIdx));
            // "on"/"off" (ingles) — em PT seria "ativado/desativado".
            if (window.Contains("off") || window.Contains("desativ")) return false;
            if (window.Contains("on") || window.Contains("ativ")) return true;
            return false;
        }

        /// <summary>Liga/desliga um ou todos os perfis. profile = "Domain","Private","Public" ou "All".</summary>
        public static async Task<OperationResult> SetProfileEnabledAsync(string profile, bool enabled)
        {
            string name = profile.Equals("All", StringComparison.OrdinalIgnoreCase) ? "Domain,Public,Private" : profile;
            string cmd = "Set-NetFirewallProfile -Name " + name + " -Enabled " + (enabled ? "True" : "False") + " -ErrorAction Stop";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success) return OperationResult.Ok();

            // Fallback netsh
            string scope = profile.Equals("All", StringComparison.OrdinalIgnoreCase) ? "allprofiles" :
                           profile.Equals("Domain", StringComparison.OrdinalIgnoreCase) ? "domainprofile" :
                           profile.Equals("Private", StringComparison.OrdinalIgnoreCase) ? "privateprofile" : "publicprofile";
            var r2 = await ProcessRunner.RunAsync("netsh.exe", "advfirewall set " + scope + " state " + (enabled ? "on" : "off"), 30000);
            return r2.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", r2.CombinedError));
        }

        // ----------------------------- REGRAS DE APP -----------------------------

        public static async Task<bool> IsAppBlockedAsync(string exePath)
        {
            var rules = await ListAppRulesAsync();
            string norm = (exePath ?? "").Trim();
            return rules.Any(r =>
                string.Equals((r.Program ?? "").Trim(), norm, StringComparison.OrdinalIgnoreCase) &&
                (r.Action ?? "").IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Cria regras Inbound + Outbound de bloqueio para o .exe informado.</summary>
        public static async Task<OperationResult> BlockAppAsync(string exePath)
        {
            string file = Path.GetFileName(exePath);
            string outName = "SML Defender Control: Bloqueio - " + file + " (Saida)";
            string inName = "SML Defender Control: Bloqueio - " + file + " (Entrada)";
            string p = Escape(exePath);

            string cmd =
                "New-NetFirewallRule -DisplayName '" + Escape(outName) + "' -Group '" + RuleGroup + "' -Direction Outbound -Program '" + p + "' -Action Block -Profile Any -ErrorAction Stop; " +
                "New-NetFirewallRule -DisplayName '" + Escape(inName) + "' -Group '" + RuleGroup + "' -Direction Inbound -Program '" + p + "' -Action Block -Profile Any -ErrorAction Stop";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success)
            {
                Logger.Info("App bloqueado no firewall: " + exePath);
                return OperationResult.Ok();
            }

            // Fallback netsh
            var rOut = await ProcessRunner.RunAsync("netsh.exe",
                "advfirewall firewall add rule name=\"" + outName + "\" dir=out action=block program=\"" + exePath + "\" enable=yes", 30000);
            var rIn = await ProcessRunner.RunAsync("netsh.exe",
                "advfirewall firewall add rule name=\"" + inName + "\" dir=in action=block program=\"" + exePath + "\" enable=yes", 30000);
            return (rOut.Success || rIn.Success) ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", rOut.CombinedError + " " + rIn.CombinedError));
        }

        /// <summary>Remove as regras de bloqueio criadas para o .exe informado.</summary>
        public static async Task<OperationResult> UnblockAppAsync(string exePath)
        {
            string p = Escape(exePath);
            string cmd =
                "Get-NetFirewallRule -Group '" + RuleGroup + "' -ErrorAction SilentlyContinue | " +
                "Where-Object { ($_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue).Program -eq '" + p + "' } | " +
                "Remove-NetFirewallRule -ErrorAction Stop";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success)
            {
                Logger.Info("App liberado no firewall: " + exePath);
                return OperationResult.Ok();
            }

            // Fallback netsh: remove por programa (nome=all para abranger entrada e saida).
            var r2 = await ProcessRunner.RunAsync("netsh.exe",
                "advfirewall firewall delete rule name=all program=\"" + exePath + "\"", 30000);
            return r2.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", r2.CombinedError));
        }

        /// <summary>Lista as regras de app criadas por este painel (grupo PainelSeguranca).</summary>
        public static async Task<List<FirewallRuleInfo>> ListAppRulesAsync()
        {
            var list = new List<FirewallRuleInfo>();
            string cmd =
                "Get-NetFirewallRule -Group '" + RuleGroup + "' -ErrorAction SilentlyContinue | ForEach-Object { " +
                "$f = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue; " +
                "\"$($_.DisplayName)`t$($f.Program)`t$($_.Direction)`t$($_.Action)\" }";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (!r.Success) return list;

            foreach (var line in SplitLines(r.StdOut))
            {
                var parts = line.Split('\t');
                if (parts.Length < 4) continue;
                list.Add(new FirewallRuleInfo
                {
                    DisplayName = parts[0].Trim(),
                    Program = parts[1].Trim(),
                    Direction = DirectionText(parts[2].Trim()),
                    Action = parts[3].Trim()
                });
            }
            return list;
        }

        /// <summary>
        /// Lista TODAS as regras de firewall ATIVAS que tem um programa (.exe) associado,
        /// com sua acao (Block/Allow). Usado pelas abas "Bloqueados" e "Liberados".
        ///
        /// Eficiente: faz apenas DUAS consultas em massa (filtros de aplicativo + regras) e
        /// junta pela InstanceID em memoria, em vez de uma chamada por regra.
        /// </summary>
        public static async Task<List<FirewallRuleInfo>> ListConnectionAppRulesAsync()
        {
            var list = new List<FirewallRuleInfo>();
            string cmd =
                "$m=@{}; " +
                "Get-NetFirewallApplicationFilter -PolicyStore ActiveStore -ErrorAction SilentlyContinue | " +
                "ForEach-Object { if ($_.Program) { $m[$_.InstanceID]=$_.Program } }; " +
                "Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -ErrorAction SilentlyContinue | " +
                "Where-Object { $m.ContainsKey($_.InstanceID) } | ForEach-Object { " +
                "\"$($_.DisplayName)`t$($m[$_.InstanceID])`t$($_.Direction)`t$($_.Action)`t$($_.Profile)\" }";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 120000);
            if (!r.Success) return list;

            foreach (var line in SplitLines(r.StdOut))
            {
                var parts = line.Split('\t');
                if (parts.Length < 5) continue;
                string action = parts[3].Trim();
                // So nos interessam regras de permitir/bloquear (ignora outras acoes).
                if (action.IndexOf("Block", StringComparison.OrdinalIgnoreCase) < 0 &&
                    action.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(new FirewallRuleInfo
                {
                    DisplayName = parts[0].Trim(),
                    Program = parts[1].Trim(),
                    Direction = DirectionText(parts[2].Trim()),
                    Action = action,
                    Profile = parts[4].Trim()
                });
            }
            return list;
        }

        public static async Task<OperationResult> RemoveRuleByNameAsync(string displayName)
        {
            string cmd = "Remove-NetFirewallRule -DisplayName '" + Escape(displayName) + "' -ErrorAction Stop";
            var r = await ProcessRunner.RunPowerShellAsync(cmd, 30000);
            if (r.Success) return OperationResult.Ok();
            var r2 = await ProcessRunner.RunAsync("netsh.exe", "advfirewall firewall delete rule name=\"" + displayName + "\"", 30000);
            return r2.Success ? OperationResult.Ok() : OperationResult.Fail(I18n.T("Err.Generic", r2.CombinedError));
        }

        // ----------------------------- helpers -----------------------------

        private static string DirectionText(string dir)
        {
            // Direction vem como "Inbound"/"Outbound" ou "1"/"2".
            if (dir == "1" || dir.Equals("Inbound", StringComparison.OrdinalIgnoreCase)) return I18n.Language == "en" ? "Inbound" : "Entrada";
            if (dir == "2" || dir.Equals("Outbound", StringComparison.OrdinalIgnoreCase)) return I18n.Language == "en" ? "Outbound" : "Saida";
            return dir;
        }

        private static bool ParseEnabled(string s)
        {
            // Enabled vem como "True"/"False" ou "1"/"0".
            return s.Equals("True", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        private static IEnumerable<string> SplitLines(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (var line in s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("'", "''");
        }
    }
}
