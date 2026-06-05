using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PainelSeguranca.Core;
using PainelSeguranca.Services;

namespace PainelSeguranca.UI
{
    /// <summary>
    /// TELA PRINCIPAL — simples e direta para usuario leigo. O foco do app esta a UM clique:
    ///   1) ver/controlar quais apps o firewall esta BLOQUEANDO na internet, e
    ///   2) escolher arquivos/pastas que o Defender NAO deve verificar (exclusoes).
    /// Ambos ficam VISIVEIS na tela principal (sem senha), em abas grandes. As acoes de
    /// protecao (pausar, ligar/desligar, verificar, atualizar) ficam na aba "Protecao".
    /// A UI nunca trava: toda chamada pesada (WMI/PowerShell/firewall) roda em background.
    /// </summary>
    public sealed class MainForm : Form
    {
        // Callbacks definidos pela bandeja (para toasts e atualizacao do icone).
        public Action<string> ShowToast { get; set; }
        public Action<StatusColor> OnStatusColor { get; set; }

        public bool AllowClose { get; set; }

        // --- Status (topo) ---
        private readonly Panel _statusPanel = new Panel();
        private readonly PictureBox _statusIcon = new PictureBox();
        private readonly Label _statusTitle = new Label();
        private readonly Label _statusSub = new Label();
        private readonly Label _infoAv = new Label();
        private readonly Label _infoFw = new Label();

        private readonly Panel _bannerPanel = new Panel();
        private readonly Label _bannerLabel = new Label();
        private string _bannerFull = "";
        private bool _bannerIsTamper;

        private readonly Panel _pausePanel = new Panel();
        private readonly Label _pauseLabel = new Label();

        // --- Abas ---
        private ListView _lvBlocked;        // apps bloqueados na internet
        private Label _lblBlockedCount;
        private ListView _lvExclude;        // arquivos/pastas excluidos do Defender
        private Label _lblExcludeCount;

        // --- Aba Protecao (tiles) ---
        private FlowLayoutPanel _tiles;
        private ActionTile _tilePause;
        private ActionTile _tileToggle;
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
            ClientSize = new Size(560, 720);
            MinimumSize = new Size(520, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            try { Icon = IconFactory.CreateTrayIcon(StatusColor.Green); } catch { }

            // --- Status grande (topo) ---
            _statusPanel.Dock = DockStyle.Top;
            _statusPanel.Height = 132;
            _statusPanel.BackColor = Theme.Surface;
            _statusPanel.Padding = new Padding(18, 12, 18, 10);

            _statusIcon.Size = new Size(64, 64);
            _statusIcon.Location = new Point(18, 22);
            _statusIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _statusIcon.BackColor = Color.Transparent;

            _statusTitle.Location = new Point(96, 20);
            _statusTitle.AutoSize = false;
            _statusTitle.Size = new Size(430, 36);
            _statusTitle.Font = new Font("Segoe UI", 19f, FontStyle.Bold);
            _statusTitle.Text = I18n.T("Status.Loading");
            _statusTitle.ForeColor = Theme.Text;

            _statusSub.Location = new Point(98, 60);
            _statusSub.AutoSize = false;
            _statusSub.Size = new Size(430, 20);
            _statusSub.Font = new Font("Segoe UI", 9.5f);
            _statusSub.ForeColor = Theme.Subtle;

            _infoAv.Location = new Point(98, 84);
            _infoAv.AutoSize = false;
            _infoAv.Size = new Size(430, 18);
            _infoAv.Font = new Font("Segoe UI", 8.75f);
            _infoAv.ForeColor = Theme.Subtle;

            _infoFw.Location = new Point(98, 102);
            _infoFw.AutoSize = false;
            _infoFw.Size = new Size(430, 18);
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

            // --- Abas (foco do app) ---
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            tabs.TabPages.Add(BuildBlockedTab());
            tabs.TabPages.Add(BuildExcludeTab());
            tabs.TabPages.Add(BuildProtectTab());

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
            Controls.Add(tabs);
            Controls.Add(footer);
            Controls.Add(_pausePanel);
            Controls.Add(_bannerPanel);
            Controls.Add(_statusPanel);

            Resize += (s, e) => LayoutTiles();
        }

        // ----------------------------- Aba: Bloqueados na internet -----------------------------

        private TabPage BuildBlockedTab()
        {
            var tab = new TabPage(I18n.T("Main.Tab.Blocked")) { BackColor = Theme.Background, Padding = new Padding(12) };

            var hint = new Label { Text = I18n.T("Main.Blocked.Hint"), Dock = DockStyle.Top, Height = 38, ForeColor = Theme.Subtle, Font = new Font("Segoe UI", 8.75f) };

            _lvBlocked = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Theme.Surface, ForeColor = Theme.Text };
            _lvBlocked.Columns.Add(I18n.T("Conn.Col.Program"), 360);
            _lvBlocked.Columns.Add(I18n.T("Adv.Fw.Col.Dir"), 100);
            _lvBlocked.DoubleClick += (s, e) => OnAllowSelected();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0) };
            var btnAllow = new Button { Text = I18n.T("Main.Blocked.Allow"), Dock = DockStyle.Right, Width = 200, Height = 36 };
            var btnAdd = new Button { Text = I18n.T("Main.Blocked.Add"), Dock = DockStyle.Left, Width = 200, Height = 36 };
            Style.FlatButton(btnAllow, true);
            Style.FlatButton(btnAdd);
            btnAllow.Click += (s, e) => OnAllowSelected();
            btnAdd.Click += (s, e) => OnBlockNewApp();
            bottom.Controls.Add(btnAllow);
            bottom.Controls.Add(btnAdd);

            var countBar = new Panel { Dock = DockStyle.Top, Height = 22 };
            _lblBlockedCount = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Subtle, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.75f) };
            countBar.Controls.Add(_lblBlockedCount);

            tab.Controls.Add(_lvBlocked);
            tab.Controls.Add(bottom);
            tab.Controls.Add(countBar);
            tab.Controls.Add(hint);
            return tab;
        }

        // ----------------------------- Aba: Nao verificar (exclusoes) -----------------------------

        private TabPage BuildExcludeTab()
        {
            var tab = new TabPage(I18n.T("Main.Tab.Exclude")) { BackColor = Theme.Background, Padding = new Padding(12) };

            var hint = new Label { Text = I18n.T("Main.Exclude.Hint"), Dock = DockStyle.Top, Height = 38, ForeColor = Theme.Subtle, Font = new Font("Segoe UI", 8.75f) };

            _lvExclude = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Theme.Surface, ForeColor = Theme.Text };
            _lvExclude.Columns.Add(I18n.T("Main.Exclude.Col"), 470);
            _lvExclude.DoubleClick += (s, e) => OnRemoveExclusion();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0) };
            var btnRemove = new Button { Text = I18n.T("Main.Exclude.Remove"), Dock = DockStyle.Right, Width = 170, Height = 36 };
            var btnAddFolder = new Button { Text = I18n.T("Main.Exclude.AddFolder"), Dock = DockStyle.Left, Width = 160, Height = 36 };
            var btnAddFile = new Button { Text = I18n.T("Main.Exclude.AddFile"), Dock = DockStyle.Left, Width = 160, Height = 36 };
            Style.FlatButton(btnRemove);
            Style.FlatButton(btnAddFolder, true);
            Style.FlatButton(btnAddFile, true);
            btnRemove.Click += (s, e) => OnRemoveExclusion();
            btnAddFolder.Click += (s, e) => OnAddFolderExclusion();
            btnAddFile.Click += (s, e) => OnAddFileExclusion();
            bottom.Controls.Add(btnRemove);
            bottom.Controls.Add(btnAddFolder);
            bottom.Controls.Add(btnAddFile);

            var countBar = new Panel { Dock = DockStyle.Top, Height = 22 };
            _lblExcludeCount = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Subtle, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.75f) };
            countBar.Controls.Add(_lblExcludeCount);

            tab.Controls.Add(_lvExclude);
            tab.Controls.Add(bottom);
            tab.Controls.Add(countBar);
            tab.Controls.Add(hint);
            return tab;
        }

        // ----------------------------- Aba: Protecao (acoes) -----------------------------

        private TabPage BuildProtectTab()
        {
            var tab = new TabPage(I18n.T("Main.Tab.Protect")) { BackColor = Theme.Background, Padding = new Padding(4) };

            _tiles = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(12, 12, 12, 12),
                BackColor = Theme.Background
            };

            _tilePause = AddTile(I18n.T("Btn.Pause"), I18n.T("Btn.Pause.Sub"), "⏸", OnPauseTile);
            _tileToggle = AddTile(I18n.T("Btn.Toggle.Off"), I18n.T("Btn.Toggle.Sub"), "🛡", OnToggleTile);
            _tileScan = AddTile(I18n.T("Btn.QuickScan"), I18n.T("Btn.QuickScan.Sub"), "🔍", OnQuickScan);
            _tileUpdate = AddTile(I18n.T("Btn.Update"), I18n.T("Btn.Update.Sub"), "⤓", OnUpdate);

            tab.Controls.Add(_tiles);
            return tab;
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
            if (_tiles == null) return;
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
                FillExclusions(defTask.Result);
                OnStatusColor?.Invoke(agg.Color);
            }
            catch (Exception ex)
            {
                Logger.Error("Falha ao atualizar status", ex);
            }

            // Lista de bloqueados pode ser mais lenta (varre o firewall): atualiza em separado.
            await LoadBlockedAsync();
            LayoutTiles();
        }

        private void ApplyStatus(AggregateStatus agg)
        {
            var def = agg.Defender;
            var fw = agg.Firewall;

            try { _statusIcon.Image = IconFactory.CreateShieldBitmap(64, agg.Color); } catch { }

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
            _tileToggle.SetTitle(def.RealtimeActive ? I18n.T("Btn.Toggle.Off") : I18n.T("Btn.Toggle.On"));

            UpdatePauseUi();
        }

        // ----------------------------- Bloqueados na internet -----------------------------

        private async Task LoadBlockedAsync()
        {
            if (_lvBlocked == null) return;
            _lblBlockedCount.Text = I18n.T("Conn.Loading");
            List<FirewallRuleInfo> all;
            try { all = await FirewallService.ListConnectionAppRulesAsync(); }
            catch (Exception ex) { Logger.Error("Listar bloqueados", ex); all = new List<FirewallRuleInfo>(); }

            // Junta entrada+saida do mesmo programa numa unica linha (usuario leigo nao precisa ver as duas).
            var blocked = all.Where(x => x.IsBlock)
                             .GroupBy(x => (x.Program ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First())
                             .OrderBy(x => SafeName(x.Program), StringComparer.OrdinalIgnoreCase)
                             .ToList();

            _lvBlocked.BeginUpdate();
            _lvBlocked.Items.Clear();
            foreach (var r in blocked)
            {
                var it = new ListViewItem(r.Program) { Tag = r };
                it.SubItems.Add(r.Direction);
                _lvBlocked.Items.Add(it);
            }
            _lvBlocked.EndUpdate();

            _lblBlockedCount.Text = blocked.Count == 0
                ? I18n.T("Main.Blocked.Empty")
                : I18n.T("Conn.Count", blocked.Count);
        }

        private void OnBlockNewApp()
        {
            using (var ofd = new OpenFileDialog { Title = I18n.T("Dlg.SelectExe"), Filter = "Programas (*.exe)|*.exe|*.*|*.*", CheckFileExists = true })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string exe = ofd.FileName;
                _ = RunBusy(async () =>
                {
                    if (await FirewallService.IsAppBlockedAsync(exe))
                    {
                        ShowToast?.Invoke(I18n.T("Toast.Blocked"));
                    }
                    else
                    {
                        if (!Dialogs.Confirm(this, I18n.T("Conn.ConfirmBlock", SafeName(exe)))) return;
                        var r = await FirewallService.BlockAppAsync(exe);
                        Report(r, I18n.T("Toast.Blocked"));
                    }
                    await LoadBlockedAsync();
                });
            }
        }

        private void OnAllowSelected()
        {
            if (_lvBlocked.SelectedItems.Count == 0) { Dialogs.Info(this, I18n.T("Conn.NeedSelect")); return; }
            var item = _lvBlocked.SelectedItems[0];
            string program = item.Text;
            if (!Dialogs.Confirm(this, I18n.T("Conn.ConfirmAllow", SafeName(program)))) return;
            _ = RunBusy(async () =>
            {
                // "Liberar" = remover a(s) regra(s) de bloqueio deste programa (entrada+saida, qualquer origem).
                var r = await FirewallService.AllowAppAsync(program);
                Report(r, I18n.T("Toast.Unblocked"));
                await LoadBlockedAsync();
            });
        }

        /// <summary>Permite a bandeja disparar o fluxo guiado de bloquear/liberar app.</summary>
        public void InvokeBlockAppFlow()
        {
            OnBlockNewApp();
        }

        // ----------------------------- Nao verificar (exclusoes) -----------------------------

        private void FillExclusions(DefenderStatus def)
        {
            if (_lvExclude == null) return;
            var paths = (def != null && def.ExclusionPath != null) ? def.ExclusionPath : new List<string>();

            _lvExclude.BeginUpdate();
            _lvExclude.Items.Clear();
            foreach (var p in paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                _lvExclude.Items.Add(new ListViewItem(p));
            _lvExclude.EndUpdate();

            _lblExcludeCount.Text = paths.Count == 0
                ? I18n.T("Main.Exclude.Empty")
                : I18n.T("Conn.Count", paths.Count);
        }

        private void OnAddFileExclusion()
        {
            using (var ofd = new OpenFileDialog { Title = I18n.T("Dlg.SelectFile"), CheckFileExists = true })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string path = ofd.FileName;
                _ = RunBusy(async () =>
                {
                    var r = await DefenderService.AddPathExclusionAsync(path);
                    Report(r, I18n.T("Main.Exclude.AddFile"));
                    await RefreshAsync();
                });
            }
        }

        private void OnAddFolderExclusion()
        {
            using (var fbd = new FolderBrowserDialog { Description = I18n.T("Dlg.SelectFolder") })
            {
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                string path = fbd.SelectedPath;
                _ = RunBusy(async () =>
                {
                    var r = await DefenderService.AddPathExclusionAsync(path);
                    Report(r, I18n.T("Main.Exclude.AddFolder"));
                    await RefreshAsync();
                });
            }
        }

        private void OnRemoveExclusion()
        {
            if (_lvExclude.SelectedItems.Count == 0) { Dialogs.Info(this, I18n.T("Main.NeedSelectExcl")); return; }
            string path = _lvExclude.SelectedItems[0].Text;
            if (!Dialogs.Confirm(this, I18n.T("Confirm.RemoveExclusion"))) return;
            _ = RunBusy(async () =>
            {
                var r = await DefenderService.RemovePathExclusionAsync(path);
                Report(r, I18n.T("Main.Exclude.Remove"));
                await RefreshAsync();
            });
        }

        private static string SafeName(string p)
        {
            try { return Path.GetFileName(p ?? ""); } catch { return p ?? ""; }
        }

        // ----------------------------- Acoes de protecao -----------------------------

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
            try { BeginInvoke((Action)UpdatePauseUi); }
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
