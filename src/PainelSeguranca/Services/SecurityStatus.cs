using System;
using System.Collections.Generic;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>Estado lido do antivirus (Defender) via WMI.</summary>
    public sealed class DefenderStatus
    {
        public bool Available { get; set; }              // o provider WMI do Defender respondeu
        public string ErrorMessage { get; set; }

        // MSFT_MpComputerStatus
        public bool RealTimeProtectionEnabled { get; set; }
        public bool IsTamperProtected { get; set; }
        public string AMRunningMode { get; set; }        // Normal / Passive / EDR Block / SxS Passive...
        public string AntivirusSignatureVersion { get; set; }
        public DateTime? AntivirusSignatureLastUpdated { get; set; }
        public uint? QuickScanAge { get; set; }          // dias
        public uint? FullScanAge { get; set; }

        // MSFT_MpPreference
        public bool DisableRealtimeMonitoring { get; set; }
        public bool DisableBehaviorMonitoring { get; set; }
        public bool DisableScriptScanning { get; set; }
        public int PUAProtection { get; set; }           // 0 off, 1 on, 2 audit
        public int MAPSReporting { get; set; }           // 0 off, 1 basic, 2 advanced
        public int SubmitSamplesConsent { get; set; }    // 0 prompt,1 safe,2 never,3 all
        public int EnableControlledFolderAccess { get; set; } // 0 off,1 on,2 audit

        public List<string> ExclusionPath { get; set; } = new List<string>();
        public List<string> ExclusionExtension { get; set; } = new List<string>();
        public List<string> ExclusionProcess { get; set; } = new List<string>();

        public bool IsPassive
        {
            get
            {
                if (string.IsNullOrEmpty(AMRunningMode)) return false;
                string m = AMRunningMode.ToLowerInvariant();
                return m.Contains("passive") || m.Contains("edr");
            }
        }

        /// <summary>Protecao em tempo real efetivamente ativa.</summary>
        public bool RealtimeActive
        {
            get { return Available && RealTimeProtectionEnabled && !DisableRealtimeMonitoring; }
        }
    }

    /// <summary>Estado do firewall por perfil.</summary>
    public sealed class FirewallStatus
    {
        public bool Available { get; set; }
        public string ErrorMessage { get; set; }
        public bool DomainEnabled { get; set; }
        public bool PrivateEnabled { get; set; }
        public bool PublicEnabled { get; set; }

        /// <summary>Verdadeiro se todos os perfis aplicaveis estao ligados.</summary>
        public bool AllEnabled { get { return DomainEnabled && PrivateEnabled && PublicEnabled; } }

        /// <summary>Pelo menos um perfil ligado.</summary>
        public bool AnyEnabled { get { return DomainEnabled || PrivateEnabled || PublicEnabled; } }
    }

    public enum OverallLevel { Protected, Passive, Attention }

    /// <summary>Status agregado AV + Firewall, com a cor para o icone/UI.</summary>
    public sealed class AggregateStatus
    {
        public DefenderStatus Defender { get; set; }
        public FirewallStatus Firewall { get; set; }

        public OverallLevel Level
        {
            get
            {
                bool avOk = Defender != null && Defender.Available && Defender.RealtimeActive;
                bool fwOk = Firewall != null && Firewall.Available && Firewall.AllEnabled;

                // Tamper bloqueando ou modo passivo -> amarelo (atencao branda).
                bool passive = Defender != null && Defender.IsPassive;

                if (avOk && fwOk && !passive) return OverallLevel.Protected;
                if (passive && fwOk) return OverallLevel.Passive;
                return OverallLevel.Attention;
            }
        }

        public StatusColor Color
        {
            get
            {
                switch (Level)
                {
                    case OverallLevel.Protected: return StatusColor.Green;
                    case OverallLevel.Passive: return StatusColor.Yellow;
                    default: return StatusColor.Red;
                }
            }
        }
    }
}
