using System;
using System.Diagnostics;
using System.Security.Principal;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// Verifica se o processo esta elevado e oferece reabrir elevado (verbo "runas").
    /// O manifesto ja pede requireAdministrator; isto e uma rede de seguranca caso o app
    /// seja iniciado sem elevacao por algum motivo.
    /// </summary>
    public static class ElevationHelper
    {
        public static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tenta reabrir o proprio executavel elevado. Retorna true se o novo processo
        /// foi iniciado (o chamador deve entao encerrar a instancia atual).
        /// </summary>
        public static bool RelaunchElevated(string arguments = null)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = true, // necessario para o verbo runas
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                // Usuario pode ter recusado o UAC.
                Logger.Warn("Reabrir elevado cancelado/falhou: " + ex.Message);
                return false;
            }
        }
    }
}
