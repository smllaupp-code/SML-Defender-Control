using System;
using System.Security.Cryptography;

namespace PainelSeguranca.Core
{
    /// <summary>
    /// Protecao por senha da interface. Guardamos APENAS um hash salgado PBKDF2
    /// (Rfc2898DeriveBytes, SHA-256). Nunca armazenamos a senha em claro.
    /// E protecao da UI (abrir Avancado/Configuracoes), nao um contorno de seguranca.
    /// </summary>
    public static class PasswordProtection
    {
        private const int SaltSize = 16;   // 128 bits
        private const int HashSize = 32;   // 256 bits
        private const int Iterations = 100000;

        public static bool IsEnabled
        {
            get { return AppSettings.Instance.PasswordEnabled && AppSettings.Instance.PasswordHash.Length > 0; }
        }

        public static void SetPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            byte[] hash = Derive(password, salt, Iterations);

            var s = AppSettings.Instance;
            s.PasswordSalt = Convert.ToBase64String(salt);
            s.PasswordHash = Convert.ToBase64String(hash);
            s.PasswordIterations = Iterations;
            s.PasswordEnabled = true;
            Logger.Info("Senha da interface definida/alterada.");
        }

        public static void Disable()
        {
            var s = AppSettings.Instance;
            s.PasswordEnabled = false;
            s.PasswordHash = "";
            s.PasswordSalt = "";
            s.PasswordIterations = 0;
            Logger.Info("Protecao por senha desativada.");
        }

        public static bool Verify(string password)
        {
            try
            {
                var s = AppSettings.Instance;
                if (s.PasswordHash.Length == 0 || s.PasswordSalt.Length == 0) return false;
                byte[] salt = Convert.FromBase64String(s.PasswordSalt);
                byte[] expected = Convert.FromBase64String(s.PasswordHash);
                int iters = s.PasswordIterations > 0 ? s.PasswordIterations : Iterations;
                byte[] actual = Derive(password, salt, iters);
                return FixedTimeEquals(expected, actual);
            }
            catch (Exception ex)
            {
                Logger.Error("Falha ao verificar senha", ex);
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            // SHA-256 disponivel no construtor a partir do .NET Framework 4.7.2+ (alvo: 4.8).
            using (var pbkdf2 = new Rfc2898DeriveBytes(password ?? string.Empty, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
