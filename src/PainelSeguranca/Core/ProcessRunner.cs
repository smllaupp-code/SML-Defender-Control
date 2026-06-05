using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PainelSeguranca.Core
{
    /// <summary>Resultado de um processo: codigo de saida, stdout e stderr.</summary>
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public bool TimedOut { get; set; }
        public bool Launched { get; set; }

        public bool Success { get { return Launched && !TimedOut && ExitCode == 0; } }

        public string CombinedError
        {
            get
            {
                var sb = new StringBuilder();
                if (!Launched) sb.Append("Nao foi possivel iniciar o processo. ");
                if (TimedOut) sb.Append("Tempo limite excedido. ");
                if (!string.IsNullOrWhiteSpace(StdErr)) sb.Append(StdErr.Trim());
                else if (ExitCode != 0 && !string.IsNullOrWhiteSpace(StdOut)) sb.Append(StdOut.Trim());
                return sb.ToString().Trim();
            }
        }
    }

    /// <summary>
    /// Executa processos (powershell.exe, MpCmdRun.exe, netsh, schtasks) de forma
    /// assincrona, com janela OCULTA, capturando ExitCode/stdout/stderr. Nunca bloqueia a UI.
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>Executa um .exe arbitrario oculto e captura a saida.</summary>
        public static Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutMs = 120000)
        {
            return Task.Run(() => RunSync(fileName, arguments, timeoutMs));
        }

        /// <summary>
        /// Executa um comando PowerShell oculto e nao-interativo.
        /// -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "..."
        /// </summary>
        public static Task<ProcessResult> RunPowerShellAsync(string command, int timeoutMs = 120000)
        {
            // Codificamos o comando em Base64 (UTF-16LE) para evitar problemas de aspas/escape.
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            return RunAsync("powershell.exe", args, timeoutMs);
        }

        private static ProcessResult RunSync(string fileName, string arguments, int timeoutMs)
        {
            var result = new ProcessResult();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var proc = new Process())
                {
                    proc.StartInfo = psi;
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    using (var outDone = new ManualResetEventSlim(false))
                    using (var errDone = new ManualResetEventSlim(false))
                    {
                        proc.OutputDataReceived += (s, e) =>
                        {
                            if (e.Data == null) outDone.Set(); else sbOut.AppendLine(e.Data);
                        };
                        proc.ErrorDataReceived += (s, e) =>
                        {
                            if (e.Data == null) errDone.Set(); else sbErr.AppendLine(e.Data);
                        };

                        result.Launched = proc.Start();
                        if (!result.Launched) return result;

                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();

                        if (!proc.WaitForExit(timeoutMs))
                        {
                            result.TimedOut = true;
                            try { proc.Kill(); } catch { /* ignore */ }
                        }
                        else
                        {
                            // Garante o flush dos streams apos a saida.
                            outDone.Wait(2000);
                            errDone.Wait(2000);
                            result.ExitCode = proc.ExitCode;
                        }
                    }
                    result.StdOut = sbOut.ToString();
                    result.StdErr = sbErr.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Launched = false;
                result.StdErr = ex.Message;
                Logger.Error("Falha ao executar processo: " + fileName, ex);
            }
            return result;
        }

        /// <summary>Resolve o caminho de um executavel do sistema dentro de System32.</summary>
        public static string System32(string exe)
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string p = Path.Combine(sys, exe);
            return File.Exists(p) ? p : exe; // fallback: confia no PATH
        }
    }
}
