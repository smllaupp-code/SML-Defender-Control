using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PainelSeguranca.Core;
using PainelSeguranca.Services;

namespace PainelSeguranca.UI
{
    /// <summary>
    /// Contexto de aplicacao de BANDEJA. Mantem o NotifyIcon (cuja cor muda por status
    /// agregado AV+firewall), o menu e os toasts. A janela principal e criada uma vez e
    /// apenas escondida/exibida. A aplicacao so encerra pelo item "Sair".
    /// </summary>
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly MainForm _main;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private StatusColor _currentColor = StatusColor.Gray;

        // Itens de menu que mudam conforme o estado de pausa.
        private readonly ToolStripMenuItem _miPause;
        private readonly ToolStripMenuItem _miResume;
        private readonly ToolStripMenuItem _miInteractiveOff;

        // Fila de prompts do modo interativo do firewall (mostrados um de cada vez).
        private readonly Queue<BlockedAppInfo> _promptQueue = new Queue<BlockedAppInfo>();
        private bool _promptShowing;

        public TrayApplicationContext()
        {
            _main = new MainForm();
            _main.ShowToast = ShowToast;
            _main.OnStatusColor = SetIconColor;

            _tray = new NotifyIcon
            {
                Icon = IconFactory.CreateTrayIcon(StatusColor.Gray),
                Text = I18n.T("App.Tray.Tooltip"),
                Visible = true
            };
            _tray.DoubleClick += (s, e) => ShowPanel();

            var menu = new ContextMenuStrip();
            _miPause = new ToolStripMenuItem(I18n.T("Btn.Pause") + " (15 min)", null, (s, e) => PauseQuick());
            _miResume = new ToolStripMenuItem(I18n.T("Btn.Resume"), null, (s, e) => Resume()) { Visible = false };
            menu.Items.Add(_miPause);
            menu.Items.Add(_miResume);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.T("Btn.BlockApp"), null, (s, e) => BlockApp());
            menu.Items.Add(I18n.T("Btn.QuickScan"), null, (s, e) => QuickScan());
            _miInteractiveOff = new ToolStripMenuItem(I18n.T("Tray.InteractiveOff"), null, (s, e) => DisableInteractive()) { Visible = false };
            menu.Items.Add(_miInteractiveOff);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.T("Btn.Open"), null, (s, e) => ShowPanel());
            menu.Items.Add(I18n.T("Btn.Exit"), null, (s, e) => ExitApp());
            menu.Opening += (s, e) => UpdateMenuState();
            _tray.ContextMenuStrip = menu;

            // Modo interativo do firewall: recebe os bloqueios e mostra o pop-up.
            InteractiveFirewallService.AppBlocked += OnAppBlocked;
            // Re-arma o modo interativo se estava ligado (ex.: app reiniciado pela tarefa de logon).
            _ = InteractiveFirewallService.ReArmIfNeededAsync();

            PauseProtectionService.Instance.Changed += () =>
            {
                try { _main.BeginInvoke((Action)UpdateMenuState); } catch { }
            };

            // Atualiza o status (e a cor do icone) periodicamente.
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            _refreshTimer.Tick += async (s, e) => await _main.RefreshAsync();
            _refreshTimer.Start();

            // Primeira leitura em background (sem mostrar a janela no boot silencioso).
            _ = _main.RefreshAsync();
        }

        /// <summary>Exibe a janela principal (usado pelo duplo-clique e por nova instancia).</summary>
        public void ShowPanel()
        {
            if (_main.IsDisposed) return;
            if (!_main.Visible) _main.Show();
            if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
            _main.Activate();
            _main.BringToFront();
            _ = _main.RefreshAsync();
        }

        /// <summary>Chamado de outra thread (segunda instancia sinalizou).</summary>
        public void ShowPanelFromOtherThread()
        {
            try { _main.BeginInvoke((Action)ShowPanel); } catch { }
        }

        private void SetIconColor(StatusColor color)
        {
            if (color == _currentColor) return;
            _currentColor = color;
            try
            {
                var old = _tray.Icon;
                _tray.Icon = IconFactory.CreateTrayIcon(color);
                if (old != null) old.Dispose();
            }
            catch (Exception ex) { Logger.Warn("Falha ao trocar icone da bandeja: " + ex.Message); }
        }

        public void ShowToast(string message)
        {
            try
            {
                _tray.BalloonTipTitle = I18n.T("App.Title");
                _tray.BalloonTipText = message;
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(3000);
            }
            catch (Exception ex) { Logger.Warn("Falha no toast: " + ex.Message); }
        }

        private void UpdateMenuState()
        {
            bool paused = PauseProtectionService.Instance.IsPaused;
            _miPause.Visible = !paused;
            _miResume.Visible = paused;
            _miInteractiveOff.Visible = InteractiveFirewallService.IsActive;
        }

        // ----------------------------- Modo interativo do firewall -----------------------------

        private void OnAppBlocked(BlockedAppInfo info)
        {
            try { _main.BeginInvoke((Action)(() => EnqueuePrompt(info))); } catch { }
        }

        private void EnqueuePrompt(BlockedAppInfo info)
        {
            _promptQueue.Enqueue(info);
            if (!_promptShowing) ProcessNextPrompt();
        }

        private void ProcessNextPrompt()
        {
            if (_promptQueue.Count == 0) { _promptShowing = false; return; }
            _promptShowing = true;
            var info = _promptQueue.Dequeue();
            try
            {
                using (var f = new FirewallPromptForm(info))
                {
                    f.ShowDialog();
                    if (f.Result == FirewallPromptForm.Decision.Allow)
                    {
                        _ = InteractiveFirewallService.AllowAppAsync(info.ApplicationPath);
                        ShowToast(I18n.T("Toast.Unblocked"));
                    }
                    else
                    {
                        InteractiveFirewallService.DenyAppForSession(info.ApplicationPath);
                    }
                }
            }
            catch (Exception ex) { Logger.Error("Prompt interativo", ex); }
            // Proximo da fila.
            try { _main.BeginInvoke((Action)ProcessNextPrompt); } catch { _promptShowing = false; }
        }

        private async void DisableInteractive()
        {
            var r = await InteractiveFirewallService.DisableAsync();
            if (r.Success) ShowToast(I18n.T("Adv.Fw.Interactive.Off"));
            UpdateMenuState();
        }

        private async void PauseQuick()
        {
            var r = await PauseProtectionService.Instance.PauseForAsync(TimeSpan.FromMinutes(15));
            if (r.Success) ShowToast(I18n.T("Toast.Paused"));
            else if (r.TamperBlocked) ShowToast(I18n.T("Tamper.On"));
            await _main.RefreshAsync();
        }

        private async void Resume()
        {
            var r = await PauseProtectionService.Instance.ResumeAsync();
            if (r.Success) ShowToast(I18n.T("Toast.Resumed"));
            await _main.RefreshAsync();
        }

        private async void QuickScan()
        {
            ShowToast(I18n.T("Toast.ScanStarted"));
            await DefenderService.QuickScanAsync();
            ShowToast(I18n.T("Toast.ScanDone"));
        }

        private void BlockApp()
        {
            // Reaproveita o fluxo guiado da janela principal (com confirmacao).
            ShowPanel();
            _main.BeginInvoke((Action)(() =>
            {
                // Simula o clique no botao de bloqueio abrindo o seletor.
                _main.InvokeBlockAppFlow();
            }));
        }

        private void ExitApp()
        {
            Logger.Info("Encerrando pelo menu da bandeja.");
            // Seguranca: se o modo interativo estiver ligado, desliga ao sair para nao deixar
            // o usuario sem internet e sem o servico de pop-up (saida = Bloquear sem watcher).
            if (InteractiveFirewallService.IsActive)
            {
                try { InteractiveFirewallService.DisableAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) { Logger.Error("Falha ao desligar modo interativo na saida", ex); }
            }
            _refreshTimer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            _main.AllowClose = true;
            try { _main.Close(); } catch { }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _refreshTimer?.Dispose(); } catch { }
                try { _tray?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
