using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PainelSeguranca.Core;
using PainelSeguranca.Services;

namespace PainelSeguranca.UI
{
    /// <summary>
    /// TELA PRINCIPAL — simples, para usuario leigo: status grande + poucos botoes grandes,
    /// cada um com 1 linha de explicacao. Recursos avancados ficam na janela "Avancado".
    /// A UI nunca trava: toda chamada pesada (WMI/PowerShell/processos) roda em background.
    /// </summary>
    public sealed class MainForm : Form
    {
        // Callbacks definidos pela bandeja (para toasts e atualizacao do icone).
        public Action<string> ShowToast { get; set; }
        public Action<StatusColor> OnStatusColor { get; set; }

        public bool AllowClose { get; set; }

        private readonly Panel _statusPanel = new Panel();
        private readonly PictureBox _statusIcon = new PictureBox();
        private readonly Label _statusTitle = new Label();
        private readonly Label _statusSub = new Label();
        private readonly Label _infoAv = new Label();
        private readonly Label _infoFw = new Label();
        private readonly Label _infoSig = new Label();

        private readonly Panel _bannerPanel = new Panel();
        private readonly Label _bannerLabel = new Label();
        private string _bannerFull = "";
        private bool _bannerIsTamper;

        private readonly Panel _pausePanel = new Panel();
        private readonly Label _pauseLabel = new Label();

        private readonly FlowLayoutPanel _tiles = new FlowLayoutPanel();
        private ActionTile _tilePause;
        private ActionTile _tileToggle;
        private ActionTile _tileTrust;
        private ActionTile _tileFolder;
        private ActionTile _tileBlock;
        private ActionTile _tileScan;
        private ActionTile _tileUpdate;

        private readonly ContextMenuStrip _pauseMenu = new ContextMenuStrip();
        private AggregateStatus _last;
        private bool _busy;

        public MainForm()
        {
            BuildUi();
            PauseProtectionService.Instance.Changed += OnPauseChanged;
            FormClosed += (s, e) => PauseProtectionService.Instance.Changed -= OnPauseChanged;
            Shown += async (s, e) => await RefreshAsync();
        }

        // ----------------------------- Construcao da UI -----------------------------

        private void BuildUi()
        {
            Text = I18n.T("App.Title");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 720);
            MinimumSize = new Size(480, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            try { Icon = IconFactory.CreateTrayIcon(StatusColor.Green); } catch { }

            // --- Status grande (topo) ---
            _statusPanel.Dock = DockStyle.Top;
            _statusPanel.Height = 140;
            _statusPanel.BackColor = Theme.Surface;
            _statusPanel.Padding = new Padding(18, 14, 18, 12);

            _statusIcon.Size = new Size(72, 72);
            _statusIcon.Location = new Point(18, 22);
            _statusIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _statusIcon.BackColor = Color.Transparent;

            _statusTitle.Location = new Point(104, 22);
            _statusTitle.AutoSize = false;
            _statusTitle.Size = new Size(390, 38);
            _statusTitle.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            _statusTitle.Text = I18n.T("Status.Loading");
            _statusTitle.ForeColor = Theme.Text;

            _statusSub.Location = new Point(106, 64);
            _statusSub.AutoSize = false;
            _statusSub.Size = new Size(390, 22);
            _statusSub.Font = new Font("Segoe UI", 9.75f);
            _statusSub.ForeColor = Theme.Subtle;

            _infoAv.Location = new Point(106, 90);
            _infoAv.AutoSize = false;
            _infoAv.Size = new Size(390, 18);
            _infoAv.Font = new Font("Segoe UI", 8.75f);
            _infoAv.ForeColor = Theme.Subtle;

            _infoFw.Location = new Point(106, 108);
            _infoFw.AutoSize = false;
            _infoFw.Size = new Size(390, 18);
            _infoFw.Font = new Font("Segoe UI", 8.75f);
            _infoFw.ForeColor = Theme.Subtle;

            _statusPanel.Controls.Add(_statusIcon);
            _statusPanel.Controls.Add(_statusTitle);
            _statusPanel.Controls.Add(_statusSub);
            _statusPanel.Controls.Add(_infoAv);
            _statusPanel.Controls.Add(_infoFw);

            // --- Banner de alerta (Tamper / passivo) ---
            _bannerPanel.Dock = DockStyle.Top;
            _bannerPanel.Height = 40;
            _bannerPanel.BackColor = Color.FromArgb(60, 50, 20);
            _bannerPanel.Visible = false;
            _bannerPanel.Cursor = Cursors.Hand;
            _bannerLabel.Dock = DockStyle.Fill;
            _bannerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _bannerLabel.Padding = new Padding(16, 0, 16, 0);
            _bannerLabel.ForeColor = Color.FromArgb(255, 220, 120);
            _bannerLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _bannerPanel.Controls.Add(_bannerLabel);
            EventHandler bannerClick = (s, e) =>
            {
                if (_bannerIsTamper) Dialogs.TamperHelp(this);
                else Dialogs.Warn(this, _bannerFull);
            };
            _bannerPanel.Click += bannerClick;
            _bannerLabel.Click += bannerClick;

            // --- Painel de pausa (contador) ---
            _pausePanel.Dock = DockStyle.Top;
            _pausePanel.Height = 34;
            _pausePanel.BackColor = Color.FromArgb(225, 170, 30);
            _pausePanel.Visible = false;
            _pauseLabel.Dock = DockStyle.Fill;
            _pauseLabel.TextAlign = ContentAlignment.MiddleCenter;
            _pauseLabel.ForeColor = Color.Black;
            _pauseLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _pausePanel.Controls.Add(_pauseLabel);

            // --- Lista de botoes grandes ---
            _tiles.Dock = DockStyle.Fill;
            _tiles.FlowDirection = FlowDirection.TopDown;
            _tiles.WrapContents = false;
            _tiles.AutoScroll = true;
            _tiles.Padding = new Padding(16, 14, 16, 14);
            _tiles.BackColor = Theme.Background;

            _tilePause = AddTile(I18n.T("Btn.Pause"), I18n.T("Btn.Pause.Sub"), "⏸", OnPauseTile);
            _tileToggle = AddTile(I18n.T("Btn.Toggle.Off"), I18n.T("Btn.Toggle.Sub"), "🛡", OnToggleTile);
            _tileTrust = AddTile(I18n.T("Btn.Trust"), I18n.T("Btn.Trust.Sub"), "✓", OnTrustFile);
            _tileFolder = AddTile(I18n.T("Btn.ExcludeFolder"), I18n.T("Btn.ExcludeFolder.Sub"), "📁", OnExcludeFolder);
            _tileBlock = AddTile(I18n.T("Btn.BlockApp"), I18n.T("Btn.BlockApp.Sub"), "🌐", OnBlockApp);
            _tileScan = AddTile(I18n.T("Btn.QuickScan"), I18n.T("Btn.QuickScan.Sub"), "🔍", OnQuickScan);
            _tileUpdate = AddTile(I18n.T("Btn.Update"), I18n.T("Btn.Update.Sub"), "⤓", OnUpdate);

            // Menu de pausa (15 min / 1 hora / ate reiniciar)
            _pauseMenu.Items.Add(I18n.T("Btn.Pause.15"), null, (s, e) => DoPause(TimeSpan.FromMinutes(15), false));
            _pauseMenu.Items.Add(I18n.T("Btn.Pause.60"), null, (s, e) => DoPause(TimeSpan.FromHours(1), false));
            _pauseMenu.Items.Add(I18n.T("Btn.Pause.Reboot"), null, (s, e) => DoPause(TimeSpan.Zero, true));

            // --- Rodape ---
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Theme.Surface, Padding = new Padding(16, 8, 16, 8) };
            var btnAdvanced = new Button { Text = I18n.T("Btn.Advanced"), Dock = DockStyle.Left, Width = 130 };
            var btnRefresh = new Button { Text = I18n.T("Btn.Refresh"), Dock = DockStyle.Right, Width = 140 };
            Style.FlatButton(btnAdvanced);
            Style.FlatButton(btnRefresh, true);
            btnAdvanced.Click += OnOpenAdvanced;
            btnRefresh.Click += async (s, e) => await RefreshAsync();
            footer.Controls.Add(btnAdvanced);
            footer.Controls.Add(new Label { Width = 10, Dock = DockStyle.Left });
            footer.Controls.Add(btnRefresh);

            // Ordem de docking (de baixo p/ cima do z-order)
            Controls.Add(_tiles);
            Controls.Add(footer);
            Controls.Add(_pausePanel);
            Controls.Add(_bannerPanel);
            Controls.Add(_statusPanel);

            Resize += (s, e) => LayoutTiles();
            LayoutTiles();
        }

        private ActionTile AddTile(string title, string sub, string glyph, EventHandler handler)
        {
            var t = new ActionTile(title, sub, glyph);
            t.Activated += handler;
            _tiles.Controls.Add(t);
            return t;
        }

        private void LayoutTiles()
        {
            int w = _tiles.ClientSize.Width - _tiles.Padding.Horizontal - 4;
            if (w < 100) return;
            foreach (Control c in _tiles.Controls)
                c.Width = w;
        }

        // ----------------------------- Atualizacao de status -----------------------------

        public async Task RefreshAsync()
        {
            try
            {
                var defTask = DefenderService.ReadStatusAsync();
                var fwTask = FirewallService.ReadStatusAsync();
                await Task.WhenAll(defTask, fwTask);

                var agg = new AggregateStatus { Defender = defTask.Result, Firewall = fwTask.Result };
                _last = agg;
                ApplyStatus(agg);
                OnStatusColor?.Invoke(agg.Color);
            }
            catch (Exception ex)
            {
                Logger.Error("Falha ao atualizar status", ex);
            }
        }

        private void ApplyStatus(AggregateStatus agg)
        {
            var def = agg.Defender;
            var fw = agg.Firewall;

            // Icone e titulo
            try { _statusIcon.Image = IconFactory.CreateShieldBitmap(72, agg.Color); } catch { }

            switch (agg.Level)
            {
                case OverallLevel.Protected:
                    _statusTitle.Text = I18n.T("Status.Protected");
                    _statusTitle.ForeColor = IconFactory.ToColor(StatusColor.Green);
                    _statusSub.Text = I18n.T("Status.Protected.Sub");
                    break;
                case OverallLevel.Passive:
                    _statusTitle.Text = I18n.T("Status.Passive");
                    _statusTitle.ForeColor = IconFactory.ToColor(StatusColor.Yellow);
                    _statusSub.Text = I18n.T("Status.Attention.Sub");
                    break;
                default:
                    _statusTitle.Text = I18n.T("Status.Attention");
                    _statusTitle.ForeColor = IconFactory.ToColor(StatusColor.Red);
                    _statusSub.Text = I18n.T("Status.Attention.Sub");
                    break;
            }

            // Linhas de info
            string avState = def.Available
                ? (def.RealtimeActive ? I18n.T("Field.On") : I18n.T("Field.Off"))
                : "-";
            _infoAv.Text = I18n.T("Main.Av") + ": " + avState;

            string fwState = fw.Available
                ? (fw.AllEnabled ? I18n.T("Field.On") : (fw.AnyEnabled ? "parcial" : I18n.T("Field.Off")))
                : "-";
            _infoFw.Text = I18n.T("Main.Fw") + ": " + fwState;

            // Banner Tamper / passivo
            if (def.Available && def.IsTamperProtected)
            {
                _bannerIsTamper = true;
                _bannerFull = I18n.T("Tamper.Explain");
                _bannerLabel.Text = "⚠ " + I18n.T("Tamper.On") + "  —  " + (I18n.Language == "en" ? "click to fix" : "clique para resolver");
                _bannerPanel.Visible = true;
            }
            else if (def.Available && def.IsPassive)
            {
                _bannerIsTamper = false;
                _bannerFull = I18n.T("Passive.Explain");
                _bannerLabel.Text = "⚠ " + I18n.T("Passive.On");
                _bannerPanel.Visible = true;
            }
            else
            {
                _bannerIsTamper = false;
                _bannerPanel.Visible = false;
            }

            // Botao desligar/ligar reflete o estado atual
            if (def.RealtimeActive)
            {
                _tileToggle.SetTitle(I18n.T("Btn.Toggle.Off"));
            }
            else
            {
                _tileToggle.SetTitle(I18n.T("Btn.Toggle.On"));
            }

            UpdatePauseUi();
        }

        // ----------------------------- Acoes dos botoes -----------------------------

        private void OnPauseTile(object sender, EventArgs e)
        {
            if (PauseProtectionService.Instance.IsPaused)
            {
                _ = RunBusy(async () =>
                {
                    var r = await PauseProtectionService.Instance.ResumeAsync();
                    Report(r, I18n.T("Toast.Resumed"));
                    await RefreshAsync();
                });
            }
            else
            {
                _pauseMenu.Show(_tilePause, new Point(40, _tilePause.Height));
            }
        }

        private void DoPause(TimeSpan duration, bool untilReboot)
        {
            _ = RunBusy(async () =>
            {
                OperationResult r;
                if (untilReboot) r = await PauseProtectionService.Instance.PauseUntilRebootAsync();
                else r = await PauseProtectionService.Instance.PauseForAsync(duration);
                Report(r, I18n.T("Toast.Paused"));
                await RefreshAsync();
            });
        }

        private void OnToggleTile(object sender, EventArgs e)
        {
            bool currentlyActive = _last != null && _last.Defender != null && _last.Defender.RealtimeActive;
            if (currentlyActive)
            {
                if (!Dialogs.Confirm(this, I18n.T("Confirm.TurnOff"))) return;
                _ = RunBusy(async () =>
                {
                    var r = await DefenderService.SetRealtimeProtectionAsync(false);
                    Report(r, I18n.T("Toast.Paused"));
                    await RefreshAsync();
                });
            }
            else
            {
                _ = RunBusy(async () =>
                {
                    // Ligar tambem cancela qualquer pausa em andamento.
                    if (PauseProtectionService.Instance.IsPaused)
                        await PauseProtectionService.Instance.ResumeAsync();
                    else
                        await DefenderService.SetRealtimeProtectionAsync(true);
                    ShowToast?.Invoke(I18n.T("Toast.Resumed"));
                    await RefreshAsync();
                });
            }
        }

        private void OnTrustFile(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Title = I18n.T("Dlg.SelectFile"), CheckFileExists = true })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string path = ofd.FileName;
                _ = RunBusy(async () =>
                {
                    // Arquivo confiavel: se for .exe, excluimos como processo; sempre como caminho.
                    var r = await DefenderService.AddPathExclusionAsync(path);
                    if (r.Success && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        await DefenderService.AddProcessExclusionAsync(System.IO.Path.GetFileName(path));
                    Report(r, I18n.T("Btn.Trust"));
                    await RefreshAsync();
                });
            }
        }

        private void OnExcludeFolder(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog { Description = I18n.T("Dlg.SelectFolder") })
            {
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                string path = fbd.SelectedPath;
                _ = RunBusy(async () =>
                {
                    var r = await DefenderService.AddPathExclusionAsync(path);
                    Report(r, I18n.T("Btn.ExcludeFolder"));
                    await RefreshAsync();
                });
            }
        }

        /// <summary>Permite a bandeja disparar o fluxo guiado de bloquear/liberar app.</summary>
        public void InvokeBlockAppFlow()
        {
            OnBlockApp(this, EventArgs.Empty);
        }

        private void OnBlockApp(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Title = I18n.T("Dlg.SelectExe"), Filter = "Programas (*.exe)|*.exe|*.*|*.*", CheckFileExists = true })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string exe = ofd.FileName;
                _ = RunBusy(async () =>
                {
                    bool blocked = await FirewallService.IsAppBlockedAsync(exe);
                    if (blocked)
                    {
                        if (!Dialogs.Confirm(this, I18n.T("BlockApp.AlreadyBlocked"))) return;
                        var r = await FirewallService.UnblockAppAsync(exe);
                        Report(r, I18n.T("Toast.Unblocked"));
                    }
                    else
                    {
                        if (!Dialogs.Confirm(this, I18n.T("BlockApp.NotBlocked"))) return;
                        var r = await FirewallService.BlockAppAsync(exe);
                        Report(r, I18n.T("Toast.Blocked"));
                    }
                    await RefreshAsync();
                });
            }
        }

        private void OnQuickScan(object sender, EventArgs e)
        {
            ShowToast?.Invoke(I18n.T("Toast.ScanStarted"));
            _ = RunBusy(async () =>
            {
                var r = await DefenderService.QuickScanAsync();
                Report(r, I18n.T("Toast.ScanDone"));
                await RefreshAsync();
            });
        }

        private void OnUpdate(object sender, EventArgs e)
        {
            _ = RunBusy(async () =>
            {
                var r = await DefenderService.UpdateSignaturesAsync();
                Report(r, I18n.T("Toast.Updated"));
                await RefreshAsync();
            });
        }

        private void OnOpenAdvanced(object sender, EventArgs e)
        {
            if (!PasswordPromptForm.RequireUnlock(this)) return;
            using (var adv = new AdvancedForm())
            {
                adv.ShowToast = ShowToast;
                adv.ShowDialog(this);
            }
            _ = RefreshAsync();
        }

        // ----------------------------- Pausa: UI/contador -----------------------------

        private void OnPauseChanged()
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)UpdatePauseUi);
            }
            catch { }
        }

        private void UpdatePauseUi()
        {
            var p = PauseProtectionService.Instance;
            if (p.IsPaused)
            {
                _pausePanel.Visible = true;
                if (p.UntilReboot)
                    _pauseLabel.Text = I18n.T("Pause.UntilReboot");
                else
                {
                    var rem = p.TimeRemaining;
                    _pauseLabel.Text = I18n.T("Pause.Counter", string.Format("{0:00}:{1:00}", (int)rem.TotalMinutes, rem.Seconds));
                }
                _tilePause.SetTitle(I18n.T("Btn.Resume"));
                _tilePause.SetSubtitle(I18n.T("Btn.Pause.Sub"));
            }
            else
            {
                _pausePanel.Visible = false;
                _tilePause.SetTitle(I18n.T("Btn.Pause"));
                _tilePause.SetSubtitle(I18n.T("Btn.Pause.Sub"));
            }
        }

        // ----------------------------- Infra -----------------------------

        /// <summary>Executa uma acao assincrona com guarda de "ocupado" e tratamento de erro.</summary>
        private async Task RunBusy(Func<Task> action)
        {
            if (_busy) return;
            _busy = true;
            UseWaitCursor = true;
            try { await action(); }
            catch (Exception ex)
            {
                Logger.Error("Acao falhou", ex);
                Dialogs.Error(this, I18n.T("Err.Generic", ex.Message));
            }
            finally
            {
                _busy = false;
                UseWaitCursor = false;
            }
        }

        /// <summary>Mostra resultado: toast em sucesso, dialogo claro em Tamper/erro.</summary>
        private void Report(OperationResult r, string successToast)
        {
            if (r == null) return;
            if (r.Success)
            {
                ShowToast?.Invoke(successToast);
            }
            else if (r.TamperBlocked)
            {
                Dialogs.TamperHelp(this);
            }
            else
            {
                Dialogs.Error(this, r.Message ?? I18n.T("Err.Generic", ""));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Fechar (X) apenas esconde para a bandeja; sair de verdade so pelo menu da bandeja.
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
