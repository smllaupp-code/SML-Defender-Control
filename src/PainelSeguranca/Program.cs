using System;
using System.Threading;
using System.Windows.Forms;
using PainelSeguranca.Core;
using PainelSeguranca.UI;

namespace PainelSeguranca
{
    internal static class Program
    {
        // Nomes globais para instancia unica e sinalizacao entre instancias.
        private const string MutexName = "Global\\PainelSeguranca_SingleInstance_B7A1F4C2";
        private const string ShowEventName = "Global\\PainelSeguranca_ShowPanel_B7A1F4C2";

        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;
        private static TrayApplicationContext _ctx;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Idioma salvo (padrao pt-BR).
            I18n.LoadFromSettings();

            // ---------- Instancia unica ----------
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // Ja ha uma instancia: pede a ela para abrir o painel e sai.
                try
                {
                    EventWaitHandle existing;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out existing))
                        existing.Set();
                }
                catch { /* ignore */ }
                return;
            }

            // ---------- Elevacao ----------
            // O manifesto pede requireAdministrator, entao normalmente ja estamos elevados.
            // Rede de seguranca: se por algum motivo nao estiver, oferece reabrir elevado.
            if (!ElevationHelper.IsElevated())
            {
                Logger.Warn("Processo iniciado sem elevacao. Tentando reabrir elevado.");
                if (ElevationHelper.RelaunchElevated())
                {
                    ReleaseMutex();
                    return; // a instancia elevada assume
                }
                // Usuario recusou o UAC: alerta que acoes exigem admin, mas segue (modo leitura).
                MessageBox.Show(I18n.T("Err.NeedAdmin"), I18n.T("App.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Logger.Info("Painel de Seguranca iniciado. Elevado=" + ElevationHelper.IsElevated());

            // ---------- Tratamento global de excecoes (UI nunca derruba o app) ----------
            Application.ThreadException += (s, e) =>
            {
                Logger.Error("Excecao de UI nao tratada", e.Exception);
                try { Dialogs.Error(null, I18n.T("Err.Generic", e.Exception.Message)); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Error("Excecao nao tratada", e.ExceptionObject as Exception);
            };

            // ---------- Sinalizacao para abrir o painel a partir de outra instancia ----------
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(ShowEventListener) { IsBackground = true };
            listener.Start();

            _ctx = new TrayApplicationContext();
            Application.Run(_ctx);

            ReleaseMutex();
        }

        private static void ShowEventListener()
        {
            while (true)
            {
                try
                {
                    _showEvent.WaitOne();
                    _ctx?.ShowPanelFromOtherThread();
                }
                catch
                {
                    break;
                }
            }
        }

        private static void ReleaseMutex()
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }
    }
}
