using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// Configuracoes persistidas em %LOCALAPPDATA%\PainelSeguranca\settings.ini.
    /// Formato simples chave=valor (sem dependencia de NuGet/JSON). Thread-safe o bastante
    /// para o uso da UI. Nunca lanca para fora.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly object Gate = new object();
        private static AppSettings _instance;
        public static AppSettings Instance
        {
            get
            {
                lock (Gate)
                {
                    if (_instance == null) _instance = Load();
                    return _instance;
                }
            }
        }

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string SettingsDirectory
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "SMLDefenderControl");
            }
        }

        private static string FilePath { get { return Path.Combine(SettingsDirectory, "settings.ini"); } }

        // --- Propriedades tipadas de alto nivel ---

        /// <summary>Cultura da UI: "pt-BR" (padrao) ou "en".</summary>
        public string Language
        {
            get { return Get("Language", "pt-BR"); }
            set { Set("Language", value); }
        }

        public bool PasswordEnabled
        {
            get { return GetBool("PasswordEnabled", false); }
            set { SetBool("PasswordEnabled", value); }
        }

        /// <summary>Hash PBKDF2 salgado (Base64). Nunca guardamos a senha em claro.</summary>
        public string PasswordHash
        {
            get { return Get("PasswordHash", ""); }
            set { Set("PasswordHash", value); }
        }

        public string PasswordSalt
        {
            get { return Get("PasswordSalt", ""); }
            set { Set("PasswordSalt", value); }
        }

        public int PasswordIterations
        {
            get { return GetInt("PasswordIterations", 0); }
            set { SetInt("PasswordIterations", value); }
        }

        /// <summary>Modo interativo do firewall (Caminho A: saida bloqueada por padrao + prompts).</summary>
        public bool InteractiveFirewall
        {
            get { return GetBool("InteractiveFirewall", false); }
            set { SetBool("InteractiveFirewall", value); }
        }

        // --- Acesso generico ---

        public string Get(string key, string fallback)
        {
            lock (Gate)
            {
                string v;
                return _values.TryGetValue(key, out v) ? v : fallback;
            }
        }

        public void Set(string key, string value)
        {
            lock (Gate)
            {
                _values[key] = value ?? string.Empty;
            }
            Save();
        }

        public bool GetBool(string key, bool fallback)
        {
            bool b;
            return bool.TryParse(Get(key, fallback.ToString()), out b) ? b : fallback;
        }

        public void SetBool(string key, bool value) { Set(key, value ? "true" : "false"); }

        public int GetInt(string key, int fallback)
        {
            int i;
            return int.TryParse(Get(key, fallback.ToString()), out i) ? i : fallback;
        }

        public void SetInt(string key, int value) { Set(key, value.ToString()); }

        // --- Persistencia ---

        private static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        s._values[key] = val;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Falha ao carregar settings.ini", ex);
            }
            return s;
        }

        private void Save()
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(SettingsDirectory);
                    var sb = new StringBuilder();
                    sb.AppendLine("# Configuracoes do Painel de Seguranca. Editar a mao por sua conta e risco.");
                    foreach (var kv in _values)
                        sb.AppendLine(kv.Key + "=" + kv.Value);
                    File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Falha ao salvar settings.ini", ex);
            }
        }
    }
}
