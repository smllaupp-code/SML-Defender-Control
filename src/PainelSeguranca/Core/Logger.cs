using System;
using System.IO;
using System.Text;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// Log rotativo simples e thread-safe em %LOCALAPPDATA%\PainelSeguranca\logs.
    /// Nunca lanca excecao para fora (logar nao pode derrubar o app).
    /// </summary>
    public static class Logger
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 1 * 1024 * 1024; // 1 MB por arquivo
        private const int MaxFiles = 5;

        public static string LogDirectory
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "SMLDefenderControl", "logs");
            }
        }

        private static string CurrentFile
        {
            get { return Path.Combine(LogDirectory, "painelseguranca.log"); }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERRO", message); }

        public static void Error(string message, Exception ex)
        {
            Write("ERRO", message + " | " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace));
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(CurrentFile, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logar nunca pode derrubar o app.
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(CurrentFile);
                if (!fi.Exists || fi.Length < MaxBytes) return;

                // Apaga o mais antigo e desloca os indices.
                string oldest = CurrentFile + "." + MaxFiles;
                if (File.Exists(oldest)) File.Delete(oldest);
                for (int i = MaxFiles - 1; i >= 1; i--)
                {
                    string src = CurrentFile + "." + i;
                    string dst = CurrentFile + "." + (i + 1);
                    if (File.Exists(src)) File.Move(src, dst);
                }
                File.Move(CurrentFile, CurrentFile + ".1");
            }
            catch
            {
                // Se a rotacao falhar, segue gravando no mesmo arquivo.
            }
        }
    }
}
