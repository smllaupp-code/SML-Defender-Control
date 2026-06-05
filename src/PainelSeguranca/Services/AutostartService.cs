using System;
using System.Diagnostics;
using System.Threading.Tasks;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>
    /// "Iniciar com o Windows" SILENCIOSO via Tarefa Agendada.
    ///
    /// Por que NAO usar a pasta Inicializar nem a chave Run: o app pede admin, entao
    /// esses metodos gerariam um prompt de UAC a cada boot. A Tarefa Agendada com
    /// "Executar com privilegios mais altos" abre o app JA elevado, SEM prompt.
    ///
    /// Caminho principal: API COM do Agendador de Tarefas (ProgID "Schedule.Service"),
    /// via Interop COM por late binding (dynamic) — sem NuGet, sem interop assembly.
    /// Fallback: schtasks.exe (/rl HIGHEST /sc ONLOGON), usado no Windows 8.1 ou se o
    /// COM falhar.
    /// </summary>
    public static class AutostartService
    {
        public const string TaskName = "SMLDefenderControl_Autostart";

        // Constantes da API do Agendador (TaskScheduler 2.0).
        private const int TASK_TRIGGER_LOGON = 9;
        private const int TASK_ACTION_EXEC = 0;
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
        private const int TASK_RUNLEVEL_HIGHEST = 1;

        private static string ExePath
        {
            get { return Process.GetCurrentProcess().MainModule.FileName; }
        }

        private static string CurrentUser
        {
            get { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        public static Task<bool> IsEnabledAsync()
        {
            return Task.Run(() => IsEnabled());
        }

        public static bool IsEnabled()
        {
            try
            {
                dynamic service = CreateService();
                if (service == null) return SchtasksExists();
                dynamic folder = service.GetFolder("\\");
                try
                {
                    dynamic task = folder.GetTask(TaskName);
                    return task != null;
                }
                catch
                {
                    return false; // GetTask lanca se nao existir
                }
            }
            catch
            {
                return SchtasksExists();
            }
        }

        /// <summary>Liga ou desliga o autostart. Retorna (sucesso, mensagemDeErro).</summary>
        public static Task<Tuple<bool, string>> SetEnabledAsync(bool enabled)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (enabled) return CreateTask();
                    return DeleteTask();
                }
                catch (Exception ex)
                {
                    Logger.Error("Falha no autostart (COM). Tentando fallback schtasks.", ex);
                    return enabled ? CreateTaskFallback() : DeleteTaskFallback();
                }
            });
        }

        // ---------------- Caminho principal: COM ----------------

        private static dynamic CreateService()
        {
            Type t = Type.GetTypeFromProgID("Schedule.Service");
            if (t == null) return null; // Sem Agendador COM (ex.: 8.1 com superficie reduzida)
            dynamic service = Activator.CreateInstance(t);
            // Connect() sem parametros conecta no computador local com o usuario atual.
            service.Connect();
            return service;
        }

        private static Tuple<bool, string> CreateTask()
        {
            dynamic service = CreateService();
            if (service == null) return CreateTaskFallback();

            dynamic folder = service.GetFolder("\\");
            dynamic def = service.NewTask(0);

            // Identificacao
            def.RegistrationInfo.Description =
                "Inicia o SML Defender Control elevado, ao fazer logon, sem prompt de UAC.";
            def.RegistrationInfo.Author = "SML Defender Control";

            // Principal: roda elevado e somente quando o usuario esta conectado.
            def.Principal.LogonType = TASK_LOGON_INTERACTIVE_TOKEN; // somente com usuario conectado
            def.Principal.RunLevel = TASK_RUNLEVEL_HIGHEST;         // privilegios mais altos
            def.Principal.UserId = CurrentUser;

            // Settings: condicoes de bateria desmarcadas e sem limite de tempo.
            def.Settings.DisallowStartIfOnBatteries = false; // inicia tambem na bateria
            def.Settings.StopIfGoingOnBatteries = false;     // nao para ao passar para bateria
            def.Settings.ExecutionTimeLimit = "PT0S";        // sem limite de tempo de execucao
            def.Settings.StartWhenAvailable = true;
            def.Settings.AllowHardTerminate = false;
            def.Settings.Enabled = true;
            def.Settings.Hidden = false;
            def.Settings.MultipleInstances = 2;              // TASK_INSTANCES_IGNORE_NEW
            try { def.Settings.DisallowStartOnRemoteAppSession = false; } catch { }

            // Gatilho: ao fazer logon do usuario atual.
            dynamic trigger = def.Triggers.Create(TASK_TRIGGER_LOGON);
            trigger.Enabled = true;
            trigger.UserId = CurrentUser;

            // Acao: executar o proprio .exe.
            dynamic action = def.Actions.Create(TASK_ACTION_EXEC);
            action.Path = ExePath;
            try { action.WorkingDirectory = System.IO.Path.GetDirectoryName(ExePath); } catch { }

            // Registro (cria ou atualiza). Logon interativo: sem senha.
            folder.RegisterTaskDefinition(
                TaskName,
                def,
                TASK_CREATE_OR_UPDATE,
                CurrentUser,                 // userId
                null,                        // password (nao necessario com token interativo)
                TASK_LOGON_INTERACTIVE_TOKEN // logonType
            );

            Logger.Info("Autostart criado via COM (Tarefa Agendada): " + TaskName);
            return Tuple.Create(true, (string)null);
        }

        private static Tuple<bool, string> DeleteTask()
        {
            dynamic service = CreateService();
            if (service == null) return DeleteTaskFallback();
            dynamic folder = service.GetFolder("\\");
            try
            {
                folder.DeleteTask(TaskName, 0);
                Logger.Info("Autostart removido via COM: " + TaskName);
            }
            catch
            {
                // Se nao existia, considera sucesso.
            }
            return Tuple.Create(true, (string)null);
        }

        // ---------------- Fallback: schtasks.exe (Windows 8.1) ----------------

        private static bool SchtasksExists()
        {
            try
            {
                var r = ProcessRunner.RunAsync("schtasks.exe", "/Query /TN \"" + TaskName + "\"", 15000).GetAwaiter().GetResult();
                return r.Success;
            }
            catch
            {
                return false;
            }
        }

        private static Tuple<bool, string> CreateTaskFallback()
        {
            // /rl HIGHEST = privilegios mais altos; /sc ONLOGON = ao fazer logon; /F = sobrescreve.
            // Observacao: o schtasks de linha de comando nao expoe todas as condicoes de bateria;
            // ele e usado apenas como fallback (principalmente 8.1).
            string args = string.Format(
                "/Create /TN \"{0}\" /TR \"\\\"{1}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                TaskName, ExePath);
            var r = ProcessRunner.RunAsync("schtasks.exe", args, 30000).GetAwaiter().GetResult();
            if (r.Success)
            {
                Logger.Info("Autostart criado via schtasks (fallback).");
                return Tuple.Create(true, (string)null);
            }
            Logger.Error("Fallback schtasks (criar) falhou: " + r.CombinedError);
            return Tuple.Create(false, r.CombinedError);
        }

        private static Tuple<bool, string> DeleteTaskFallback()
        {
            string args = string.Format("/Delete /TN \"{0}\" /F", TaskName);
            var r = ProcessRunner.RunAsync("schtasks.exe", args, 30000).GetAwaiter().GetResult();
            // Se a tarefa nao existir, schtasks retorna erro — tratamos como sucesso.
            Logger.Info("Autostart removido via schtasks (fallback). ExitCode=" + r.ExitCode);
            return Tuple.Create(true, (string)null);
        }
    }
}
