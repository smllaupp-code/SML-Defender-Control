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
                Tuple<bool, string> r;
                try
                {
                    r = enabled ? CreateTask() : DeleteTask();
                }
                catch (Exception ex)
                {
                    Logger.Error("Falha no autostart (COM). Tentando fallback schtasks.", ex);
                    r = enabled ? CreateTaskFallback() : DeleteTaskFallback();
                }

                // Persiste a preferencia quando a operacao deu certo, para o EnsureAutostart
                // respeitar a escolha do usuario nos proximos boots.
                if (r.Item1)
                {
                    AppSettings.Instance.AutostartEnabled = enabled;
                    AppSettings.Instance.AutostartInitialized = true;
                }
                return r;
            });
        }

        /// <summary>
        /// Garante que o autostart reflita a preferencia, em TODO boot:
        ///  - 1a execucao (nunca inicializado): liga por padrao — e um app de seguranca e
        ///    deve iniciar com o Windows como um dos primeiros.
        ///  - Preferencia ligada mas a tarefa sumiu ou aponta para outro caminho (ex.: .exe
        ///    movido/reinstalado): recria a tarefa.
        ///  - Preferencia desligada: nao faz nada.
        /// Exige privilegios de admin para criar a tarefa (o app ja roda elevado).
        /// </summary>
        public static Task EnsureAutostartAsync()
        {
            return Task.Run(() => EnsureAutostart());
        }

        public static void EnsureAutostart()
        {
            try
            {
                var settings = AppSettings.Instance;

                if (!settings.AutostartInitialized)
                {
                    var r = CreateTaskSafe();
                    settings.AutostartInitialized = true;
                    settings.AutostartEnabled = r.Item1;
                    if (r.Item1) Logger.Info("Autostart habilitado por padrao na primeira execucao.");
                    else Logger.Warn("Nao foi possivel habilitar autostart por padrao: " + r.Item2);
                    return;
                }

                if (settings.AutostartEnabled && !IsTaskHealthy())
                {
                    var r = CreateTaskSafe();
                    Logger.Info("Autostart re-sincronizado (tarefa ausente ou desatualizada). ok=" + r.Item1);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("EnsureAutostart falhou", ex);
            }
        }

        private static Tuple<bool, string> CreateTaskSafe()
        {
            try { return CreateTask(); }
            catch (Exception ex)
            {
                Logger.Error("CreateTask (COM) falhou em EnsureAutostart. Fallback schtasks.", ex);
                return CreateTaskFallback();
            }
        }

        /// <summary>True se a tarefa existe E sua acao aponta para o .exe atual.</summary>
        private static bool IsTaskHealthy()
        {
            try
            {
                dynamic service = CreateService();
                if (service == null) return SchtasksExists(); // fallback nao valida o caminho
                dynamic folder = service.GetFolder("\\");
                dynamic task;
                try { task = folder.GetTask(TaskName); }
                catch { return false; } // GetTask lanca se nao existir
                if (task == null) return false;
                try
                {
                    string current = (string)task.Definition.Actions.Item(1).Path;
                    return string.Equals(current, ExePath, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true; // existe; nao deu para ler o caminho — considera saudavel
                }
            }
            catch
            {
                return SchtasksExists();
            }
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
            // Prioridade alta: como e um app de seguranca, deve subir entre os primeiros no boot.
            // Escala do Agendador: 0 (mais alta) a 10 (mais baixa); padrao 7. 4 = acima do normal.
            try { def.Settings.Priority = 4; } catch { }
            try { def.Settings.DisallowStartOnRemoteAppSession = false; } catch { }

            // Gatilho: ao fazer logon do usuario atual.
            dynamic trigger = def.Triggers.Create(TASK_TRIGGER_LOGON);
            trigger.Enabled = true;
            trigger.UserId = CurrentUser;
            try { trigger.Delay = "PT0S"; } catch { } // sem atraso apos o logon

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
