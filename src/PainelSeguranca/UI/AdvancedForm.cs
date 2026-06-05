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
    /// Janela AVANCADO (separada da tela principal). Reune todos os toggles, exclusoes,
    /// regras de firewall, verificacoes, inicializacao silenciosa e seguranca da interface.
    /// Cada toggle de antivirus indica quando esta "bloqueado pelo Tamper Protection".
    /// </summary>
    public sealed class AdvancedForm : Form
    {
        public Action<string> ShowToast { get; set; }

        private bool _loading;
        private DefenderStatus _def;

        // Antivirus
        private CheckBox _cbRealtime, _cbCloud, _cbSamples, _cbPua, _cbBehavior, _cbScript, _cbCfa;
        // Exclusoes
        private ListBox _lstPaths, _lstExt, _lstProc;
        // Firewall
        private CheckBox _cbDomain, _cbPrivate, _cbPublic;
        private CheckBox _cbInteractive;
        private ListView _lvRules;
        // Conexoes (Bloqueados / Liberados)
        private ListView _lvBlocked, _lvAllowed;
        private Label _lblBlockedCount, _lblAllowedCount;
        // Inicializacao
        private CheckBox _cbStartup;
        // Seguranca da interface
        private CheckBox _cbPassword;
        private ComboBox _cmbLang;

        public AdvancedForm()
        {
            BuildUi();
            Shown += async (s, e) => await LoadAllAsync();
        }

        private void BuildUi()
        {
            Text = I18n.T("Adv.Title");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 560);
            MinimumSize = new Size(600, 500);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = IconFactory.CreateTrayIcon(StatusColor.Green); } catch { }

            // Multiline: mostra TODAS as abas de uma vez (sem setas de rolagem escondendo abas).
            var tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
            tabs.TabPages.Add(BuildAvTab());
            tabs.TabPages.Add(BuildExclusionsTab());
            tabs.TabPages.Add(BuildFwTab());
            tabs.TabPages.Add(BuildBlockedTab());
            tabs.TabPages.Add(BuildAllowedTab());
            tabs.TabPages.Add(BuildScanTab());
            tabs.TabPages.Add(BuildStartupTab());
            tabs.TabPages.Add(BuildSecurityTab());

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 8, 12, 8), BackColor = Theme.Surface };
            var btnClose = new Button { Text = I18n.T("Btn.Close"), Dock = DockStyle.Right, Width = 120 };
            Style.FlatButton(btnClose, true);
            btnClose.Click += (s, e) => Close();
            footer.Controls.Add(btnClose);

            Controls.Add(tabs);
            Controls.Add(footer);
        }

        // ----------------------------- Aba Antivirus -----------------------------

        private TabPage BuildAvTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Av")) { BackColor = Theme.Background, Padding = new Padding(16) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

            _cbRealtime = AddToggle(flow, I18n.T("Adv.Toggle.Realtime"), async on => await DefenderService.SetRealtimeProtectionAsync(on));
            _cbCloud = AddToggle(flow, I18n.T("Adv.Toggle.Cloud"), async on => await DefenderService.SetCloudMapsAsync(on));
            _cbSamples = AddToggle(flow, I18n.T("Adv.Toggle.Samples"), async on => await DefenderService.SetSampleSubmissionAsync(on));
            _cbPua = AddToggle(flow, I18n.T("Adv.Toggle.Pua"), async on => await DefenderService.SetPuaProtectionAsync(on));
            _cbBehavior = AddToggle(flow, I18n.T("Adv.Toggle.Behavior"), async on => await DefenderService.SetBehaviorMonitoringAsync(on));
            _cbScript = AddToggle(flow, I18n.T("Adv.Toggle.Script"), async on => await DefenderService.SetScriptScanningAsync(on));
            _cbCfa = AddToggle(flow, I18n.T("Adv.Toggle.Cfa"), async on => await DefenderService.SetControlledFolderAccessAsync(on));

            tab.Controls.Add(flow);
            return tab;
        }

        private CheckBox AddToggle(Control parent, string text, Func<bool, Task<OperationResult>> apply)
        {
            var cb = new CheckBox
            {
                Text = text,
                AutoSize = false,
                Width = 580,
                Height = 30,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Theme.Text,
                Margin = new Padding(4, 6, 4, 6)
            };
            cb.CheckedChanged += async (s, e) =>
            {
                if (_loading) return;
                bool want = cb.Checked;
                cb.Enabled = false;
                var r = await apply(want);
                cb.Enabled = true;
                if (!r.Success)
                {
                    // Reverte visualmente e explica (Tamper ou erro).
                    _loading = true; cb.Checked = !want; _loading = false;
                    if (r.TamperBlocked) Dialogs.TamperHelp(this);
                    else Dialogs.Error(this, r.Message ?? "");
                }
                else
                {
                    ShowToast?.Invoke(text);
                }
                await LoadAvStateAsync();
            };
            parent.Controls.Add(cb);
            return cb;
        }

        // ----------------------------- Aba Exclusoes -----------------------------

        private TabPage BuildExclusionsTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Exclusions")) { BackColor = Theme.Background, Padding = new Padding(12) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };

            _lstPaths = MakeList();
            _lstExt = MakeList();
            _lstProc = MakeList();

            table.Controls.Add(MakeListHeader(I18n.T("Adv.Exclusions.Paths"),
                I18n.T("Adv.Exclusions.AddPath"), AddPathExclusion, () => RemoveSelected(_lstPaths, DefenderService.RemovePathExclusionAsync)));
            table.Controls.Add(_lstPaths);
            table.Controls.Add(MakeListHeader(I18n.T("Adv.Exclusions.Ext"),
                I18n.T("Adv.Exclusions.AddExt"), AddExtExclusion, () => RemoveSelected(_lstExt, DefenderService.RemoveExtensionExclusionAsync)));
            table.Controls.Add(_lstExt);
            table.Controls.Add(MakeListHeader(I18n.T("Adv.Exclusions.Proc"),
                I18n.T("Adv.Exclusions.AddProc"), AddProcExclusion, () => RemoveSelected(_lstProc, DefenderService.RemoveProcessExclusionAsync)));
            table.Controls.Add(_lstProc);

            for (int i = 0; i < 6; i++)
                table.RowStyles.Add(new RowStyle(i % 2 == 0 ? SizeType.AutoSize : SizeType.Percent, i % 2 == 0 ? 0 : 33f));

            tab.Controls.Add(table);
            return tab;
        }

        private ListBox MakeList()
        {
            return new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 2, 4, 8) };
        }

        private Panel MakeListHeader(string title, string addText, Action addAction, Action removeAction)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 34 };
            var lbl = new Label { Text = title, Dock = DockStyle.Left, Width = 200, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            var add = new Button { Text = addText, Dock = DockStyle.Right, Width = 200 };
            var rem = new Button { Text = I18n.T("Btn.Remove"), Dock = DockStyle.Right, Width = 100 };
            Style.FlatButton(add); Style.FlatButton(rem);
            add.Click += (s, e) => addAction();
            rem.Click += (s, e) => removeAction();
            p.Controls.Add(lbl);
            p.Controls.Add(rem);
            p.Controls.Add(add);
            return p;
        }

        private void AddPathExclusion()
        {
            using (var fbd = new FolderBrowserDialog { Description = I18n.T("Dlg.SelectFolder") })
            {
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                _ = RunAndReloadExclusions(() => DefenderService.AddPathExclusionAsync(fbd.SelectedPath));
            }
        }

        private void AddExtExclusion()
        {
            string ext = TextPromptForm.Ask(this, I18n.T("Adv.Exclusions.AddExt"), "ex.: log, tmp, mp4");
            if (string.IsNullOrEmpty(ext)) return;
            _ = RunAndReloadExclusions(() => DefenderService.AddExtensionExclusionAsync(ext));
        }

        private void AddProcExclusion()
        {
            string proc = TextPromptForm.Ask(this, I18n.T("Adv.Exclusions.AddProc"), "ex.: meuapp.exe");
            if (string.IsNullOrEmpty(proc)) return;
            _ = RunAndReloadExclusions(() => DefenderService.AddProcessExclusionAsync(proc));
        }

        private void RemoveSelected(ListBox list, Func<string, Task<OperationResult>> remove)
        {
            if (list.SelectedItem == null) return;
            if (!Dialogs.Confirm(this, I18n.T("Confirm.RemoveExclusion"))) return;
            string val = list.SelectedItem.ToString();
            _ = RunAndReloadExclusions(() => remove(val));
        }

        private async Task RunAndReloadExclusions(Func<Task<OperationResult>> action)
        {
            var r = await action();
            if (!r.Success)
            {
                if (r.TamperBlocked) Dialogs.Warn(this, I18n.T("Tamper.Explain"));
                else Dialogs.Error(this, r.Message ?? "");
            }
            await LoadExclusionsAsync();
        }

        // ----------------------------- Aba Firewall -----------------------------

        private TabPage BuildFwTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Fw")) { BackColor = Theme.Background, Padding = new Padding(12) };

            var note = new Label { Dock = DockStyle.Top, Height = 44, Text = I18n.T("Adv.Fw.Note"), ForeColor = Theme.Subtle, Font = new Font("Segoe UI", 8.75f) };

            var profiles = new GroupBox { Dock = DockStyle.Top, Height = 76, Text = I18n.T("Adv.Fw.Profiles"), ForeColor = Theme.Text };
            _cbDomain = new CheckBox { Text = I18n.T("Adv.Fw.Domain"), Location = new Point(16, 30), AutoSize = true, ForeColor = Theme.Text };
            _cbPrivate = new CheckBox { Text = I18n.T("Adv.Fw.Private"), Location = new Point(160, 30), AutoSize = true, ForeColor = Theme.Text };
            _cbPublic = new CheckBox { Text = I18n.T("Adv.Fw.Public"), Location = new Point(320, 30), AutoSize = true, ForeColor = Theme.Text };
            _cbDomain.CheckedChanged += (s, e) => OnProfileToggle("Domain", _cbDomain);
            _cbPrivate.CheckedChanged += (s, e) => OnProfileToggle("Private", _cbPrivate);
            _cbPublic.CheckedChanged += (s, e) => OnProfileToggle("Public", _cbPublic);
            profiles.Controls.Add(_cbDomain);
            profiles.Controls.Add(_cbPrivate);
            profiles.Controls.Add(_cbPublic);

            var rulesHeader = new Panel { Dock = DockStyle.Top, Height = 34 };
            var lblRules = new Label { Text = I18n.T("Adv.Fw.Rules"), Dock = DockStyle.Left, Width = 380, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            var btnRemoveRule = new Button { Text = I18n.T("Btn.Remove"), Dock = DockStyle.Right, Width = 120 };
            Style.FlatButton(btnRemoveRule);
            btnRemoveRule.Click += (s, e) => RemoveSelectedRule();
            rulesHeader.Controls.Add(lblRules);
            rulesHeader.Controls.Add(btnRemoveRule);

            _lvRules = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = Theme.Surface, ForeColor = Theme.Text };
            _lvRules.Columns.Add(I18n.T("Adv.Fw.Col.Name"), 220);
            _lvRules.Columns.Add(I18n.T("Adv.Fw.Col.Program"), 240);
            _lvRules.Columns.Add(I18n.T("Adv.Fw.Col.Dir"), 80);
            _lvRules.Columns.Add(I18n.T("Adv.Fw.Col.Action"), 70);

            // Modo interativo (opt-in, com aviso forte). Vai no topo da aba.
            var interactivePanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 4, 0, 6) };
            _cbInteractive = new CheckBox { Text = I18n.T("Adv.Fw.Interactive"), Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Theme.Text };
            var interactiveExplain = new Label { Text = I18n.T("Adv.Fw.Interactive.Explain"), Dock = DockStyle.Fill, ForeColor = Theme.Subtle, Font = new Font("Segoe UI", 8.5f) };
            _cbInteractive.CheckedChanged += OnInteractiveToggle;
            interactivePanel.Controls.Add(interactiveExplain);
            interactivePanel.Controls.Add(_cbInteractive);

            tab.Controls.Add(_lvRules);
            tab.Controls.Add(rulesHeader);
            tab.Controls.Add(profiles);
            tab.Controls.Add(note);
            tab.Controls.Add(interactivePanel);
            return tab;
        }

        private void OnInteractiveToggle(object sender, EventArgs e)
        {
            if (_loading) return;
            bool want = _cbInteractive.Checked;
            if (want)
            {
                // Aviso forte antes de ligar (saida bloqueada por padrao).
                if (!Dialogs.Confirm(this, I18n.T("Adv.Fw.Interactive.Warn")))
                {
                    _loading = true; _cbInteractive.Checked = false; _loading = false;
                    return;
                }
            }
            _ = RunFw(async () =>
            {
                _cbInteractive.Enabled = false;
                OperationResult r = want
                    ? await InteractiveFirewallService.EnableAsync()
                    : await InteractiveFirewallService.DisableAsync();
                _cbInteractive.Enabled = true;
                if (!r.Success)
                {
                    _loading = true; _cbInteractive.Checked = !want; _loading = false;
                    Dialogs.Error(this, r.Message ?? "");
                }
                else
                {
                    ShowToast?.Invoke(want ? I18n.T("Adv.Fw.Interactive.On") : I18n.T("Adv.Fw.Interactive.Off"));
                }
                await LoadFwAsync();
            });
        }

        private void OnProfileToggle(string profile, CheckBox cb)
        {
            if (_loading) return;
            bool want = cb.Checked;
            if (!want && !Dialogs.Confirm(this, I18n.T("Confirm.FwOff")))
            {
                _loading = true; cb.Checked = true; _loading = false;
                return;
            }
            _ = RunFw(async () =>
            {
                var r = await FirewallService.SetProfileEnabledAsync(profile, want);
                if (!r.Success) { Dialogs.Error(this, r.Message ?? ""); }
                await LoadFwAsync();
            });
        }

        private void RemoveSelectedRule()
        {
            if (_lvRules.SelectedItems.Count == 0) return;
            string name = _lvRules.SelectedItems[0].Text;
            _ = RunFw(async () =>
            {
                var r = await FirewallService.RemoveRuleByNameAsync(name);
                if (!r.Success) Dialogs.Error(this, r.Message ?? "");
                await LoadFwAsync();
            });
        }

        private async Task RunFw(Func<Task> a)
        {
            try { await a(); } catch (Exception ex) { Logger.Error("Firewall (avancado)", ex); }
        }

        // ----------------------------- Abas Bloqueados / Liberados -----------------------------

        private TabPage BuildBlockedTab()
        {
            _lvBlocked = MakeConnList();
            _lblBlockedCount = MakeCountLabel();
            // Nesta aba os apps estao BLOQUEADOS -> a acao e LIBERAR.
            return BuildConnTab(I18n.T("Adv.Tab.Blocked"), I18n.T("Conn.Blocked.Hint"), _lvBlocked, _lblBlockedCount,
                I18n.T("Conn.Btn.Allow"), OnConnLiberate);
        }

        private TabPage BuildAllowedTab()
        {
            _lvAllowed = MakeConnList();
            _lblAllowedCount = MakeCountLabel();
            // Nesta aba os apps estao LIBERADOS -> a acao e BLOQUEAR.
            return BuildConnTab(I18n.T("Adv.Tab.Allowed"), I18n.T("Conn.Allowed.Hint"), _lvAllowed, _lblAllowedCount,
                I18n.T("Conn.Btn.Block"), OnConnBlock);
        }

        private ListView MakeConnList()
        {
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BackColor = Theme.Surface, ForeColor = Theme.Text };
            lv.Columns.Add(I18n.T("Conn.Col.Program"), 300);
            lv.Columns.Add(I18n.T("Adv.Fw.Col.Dir"), 80);
            lv.Columns.Add(I18n.T("Conn.Col.Profile"), 110);
            lv.Columns.Add(I18n.T("Conn.Col.Rule"), 220);
            return lv;
        }

        private Label MakeCountLabel()
        {
            return new Label { Dock = DockStyle.Left, Width = 220, ForeColor = Theme.Subtle, TextAlign = ContentAlignment.MiddleLeft };
        }

        private TabPage BuildConnTab(string title, string hint, ListView lv, Label countLabel, string actionText, Action<ListView> action)
        {
            var tab = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(12) };

            var lblHint = new Label { Text = hint, Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Subtle, Font = new Font("Segoe UI", 8.75f) };

            var bar = new Panel { Dock = DockStyle.Top, Height = 34 };
            var refresh = new Button { Text = I18n.T("Btn.Refresh"), Dock = DockStyle.Right, Width = 150 };
            Style.FlatButton(refresh);
            refresh.Click += async (s, e) => await LoadConnectionsAsync();
            bar.Controls.Add(countLabel);
            bar.Controls.Add(refresh);

            // Acao na linha selecionada (bloquear ou liberar). Duplo-clique tambem aciona.
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 8, 0, 0) };
            var btnAction = new Button { Text = actionText, Dock = DockStyle.Right, Width = 300, Height = 34 };
            Style.FlatButton(btnAction, true);
            btnAction.Click += (s, e) => action(lv);
            bottom.Controls.Add(btnAction);
            lv.DoubleClick += (s, e) => action(lv);

            tab.Controls.Add(lv);
            tab.Controls.Add(bottom);
            tab.Controls.Add(bar);
            tab.Controls.Add(lblHint);
            return tab;
        }

        private void OnConnBlock(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) { Dialogs.Info(this, I18n.T("Conn.NeedSelect")); return; }
            string program = lv.SelectedItems[0].Text;
            if (string.IsNullOrWhiteSpace(program)) return;
            if (!Dialogs.Confirm(this, I18n.T("Conn.ConfirmBlock", SafeName(program)))) return;
            _ = RunFw(async () =>
            {
                var r = await FirewallService.BlockAppAsync(program);
                if (!r.Success) Dialogs.Error(this, r.Message ?? "");
                else ShowToast?.Invoke(I18n.T("Toast.Blocked"));
                await LoadConnectionsAsync();
            });
        }

        private void OnConnLiberate(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) { Dialogs.Info(this, I18n.T("Conn.NeedSelect")); return; }
            var item = lv.SelectedItems[0];
            string program = item.Text;
            string ruleName = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
            if (!Dialogs.Confirm(this, I18n.T("Conn.ConfirmAllow", SafeName(program)))) return;
            _ = RunFw(async () =>
            {
                // Regras de bloqueio tem prioridade sobre as de permissao no Windows Firewall;
                // por isso "liberar" = REMOVER a regra de bloqueio selecionada (pelo nome).
                OperationResult r = !string.IsNullOrEmpty(ruleName)
                    ? await FirewallService.RemoveRuleByNameAsync(ruleName)
                    : await FirewallService.UnblockAppAsync(program);
                if (!r.Success) Dialogs.Error(this, r.Message ?? "");
                else ShowToast?.Invoke(I18n.T("Toast.Unblocked"));
                await LoadConnectionsAsync();
            });
        }

        private async Task LoadConnectionsAsync()
        {
            _lblBlockedCount.Text = I18n.T("Conn.Loading");
            _lblAllowedCount.Text = I18n.T("Conn.Loading");
            List<FirewallRuleInfo> all;
            try { all = await FirewallService.ListConnectionAppRulesAsync(); }
            catch (Exception ex) { Logger.Error("Listar conexoes", ex); all = new List<FirewallRuleInfo>(); }

            FillConn(_lvBlocked, all.Where(x => x.IsBlock));
            FillConn(_lvAllowed, all.Where(x => x.IsAllow));
            _lblBlockedCount.Text = I18n.T("Conn.Count", _lvBlocked.Items.Count);
            _lblAllowedCount.Text = I18n.T("Conn.Count", _lvAllowed.Items.Count);
        }

        private void FillConn(ListView lv, IEnumerable<FirewallRuleInfo> items)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var r in items.OrderBy(x => SafeName(x.Program), StringComparer.OrdinalIgnoreCase))
            {
                var it = new ListViewItem(r.Program);
                it.SubItems.Add(r.Direction);
                it.SubItems.Add(r.Profile);
                it.SubItems.Add(r.DisplayName);
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }

        private static string SafeName(string p)
        {
            try { return Path.GetFileName(p ?? ""); } catch { return p ?? ""; }
        }

        // ----------------------------- Aba Verificacoes -----------------------------

        private TabPage BuildScanTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Scan")) { BackColor = Theme.Background, Padding = new Padding(20) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

            flow.Controls.Add(MakeScanButton(I18n.T("Adv.Scan.Quick"), () => DefenderService.QuickScanAsync()));
            flow.Controls.Add(MakeScanButton(I18n.T("Adv.Scan.Full"), () => DefenderService.FullScanAsync()));
            flow.Controls.Add(MakeScanButton(I18n.T("Adv.Scan.Custom"), CustomScan));
            flow.Controls.Add(MakeScanButton(I18n.T("Adv.Scan.Update"), () => DefenderService.UpdateSignaturesAsync()));

            tab.Controls.Add(flow);
            return tab;
        }

        private Button MakeScanButton(string text, Func<Task<OperationResult>> action)
        {
            var b = new Button { Text = text, Width = 320, Height = 40, Margin = new Padding(4, 6, 4, 6) };
            Style.FlatButton(b);
            b.Click += async (s, e) =>
            {
                b.Enabled = false;
                ShowToast?.Invoke(I18n.T("Toast.ScanStarted"));
                var r = await action();
                b.Enabled = true;
                if (r.Success) ShowToast?.Invoke(I18n.T("Toast.ScanDone"));
                else Dialogs.Error(this, r.Message ?? "");
            };
            return b;
        }

        private async Task<OperationResult> CustomScan()
        {
            using (var fbd = new FolderBrowserDialog { Description = I18n.T("Dlg.SelectFolder") })
            {
                if (fbd.ShowDialog(this) != DialogResult.OK) return OperationResult.Ok();
                return await DefenderService.CustomScanAsync(fbd.SelectedPath);
            }
        }

        // ----------------------------- Aba Inicializacao -----------------------------

        private TabPage BuildStartupTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Startup")) { BackColor = Theme.Background, Padding = new Padding(20) };

            _cbStartup = new CheckBox { Text = I18n.T("Adv.Startup.Toggle"), AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Theme.Text, Location = new Point(20, 20) };
            var explain = new Label { Text = I18n.T("Adv.Startup.Explain"), Location = new Point(22, 54), Size = new Size(560, 70), ForeColor = Theme.Subtle };
            _cbStartup.CheckedChanged += async (s, e) =>
            {
                if (_loading) return;
                bool want = _cbStartup.Checked;
                _cbStartup.Enabled = false;
                var r = await AutostartService.SetEnabledAsync(want);
                _cbStartup.Enabled = true;
                if (!r.Item1)
                {
                    _loading = true; _cbStartup.Checked = !want; _loading = false;
                    Dialogs.Error(this, r.Item2 ?? "");
                }
                else ShowToast?.Invoke(I18n.T("Adv.Startup.Toggle"));
            };

            tab.Controls.Add(_cbStartup);
            tab.Controls.Add(explain);
            return tab;
        }

        // ----------------------------- Aba Seguranca da interface -----------------------------

        private TabPage BuildSecurityTab()
        {
            var tab = new TabPage(I18n.T("Adv.Tab.Security")) { BackColor = Theme.Background, Padding = new Padding(20) };

            _cbPassword = new CheckBox { Text = I18n.T("Adv.Security.Enable"), AutoSize = true, Font = new Font("Segoe UI", 10.5f), ForeColor = Theme.Text, Location = new Point(20, 20) };
            _cbPassword.CheckedChanged += OnPasswordToggle;

            var btnSet = new Button { Text = I18n.T("Adv.Security.Set"), Location = new Point(22, 54), Width = 220, Height = 34 };
            Style.FlatButton(btnSet);
            btnSet.Click += (s, e) =>
            {
                if (PasswordPromptForm.SetNewPassword(this))
                {
                    _loading = true; _cbPassword.Checked = true; _loading = false;
                    AppSettings.Instance.PasswordEnabled = true;
                    Dialogs.Info(this, I18n.T("Pwd.Set"));
                }
            };

            var explain = new Label { Text = I18n.T("Adv.Security.Explain"), Location = new Point(22, 96), Size = new Size(560, 40), ForeColor = Theme.Subtle };

            // Seletor de idioma
            var lblLang = new Label { Text = I18n.T("Lang.Label"), Location = new Point(20, 150), AutoSize = true, ForeColor = Theme.Text };
            _cmbLang = new ComboBox { Location = new Point(120, 146), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbLang.Items.Add("Portugues (Brasil)");
            _cmbLang.Items.Add("English");
            _cmbLang.SelectedIndex = I18n.Language == "en" ? 1 : 0;
            _cmbLang.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                string lang = _cmbLang.SelectedIndex == 1 ? "en" : "pt-BR";
                AppSettings.Instance.Language = lang;
                I18n.SetLanguage(lang);
                Dialogs.Info(this, I18n.Language == "en"
                    ? "Language will fully apply after reopening the windows."
                    : "O idioma sera aplicado por completo ao reabrir as janelas.");
            };

            tab.Controls.Add(_cbPassword);
            tab.Controls.Add(btnSet);
            tab.Controls.Add(explain);
            tab.Controls.Add(lblLang);
            tab.Controls.Add(_cmbLang);
            return tab;
        }

        private void OnPasswordToggle(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_cbPassword.Checked)
            {
                if (!PasswordProtection.IsEnabled)
                {
                    // Precisa definir uma senha antes de ativar.
                    if (!PasswordPromptForm.SetNewPassword(this))
                    {
                        _loading = true; _cbPassword.Checked = false; _loading = false;
                        return;
                    }
                }
                AppSettings.Instance.PasswordEnabled = true;
            }
            else
            {
                PasswordProtection.Disable();
            }
        }

        // ----------------------------- Carregamento de estado -----------------------------

        private async Task LoadAllAsync()
        {
            await LoadAvStateAsync();
            await LoadExclusionsAsync();
            await LoadFwAsync();
            await LoadConnectionsAsync();
            await LoadStartupAsync();
            LoadSecurityState();
        }

        private async Task LoadAvStateAsync()
        {
            _def = await DefenderService.ReadStatusAsync();
            _loading = true;
            try
            {
                bool avail = _def.Available;
                _cbRealtime.Checked = _def.RealtimeActive;
                _cbCloud.Checked = _def.MAPSReporting > 0;
                _cbSamples.Checked = _def.SubmitSamplesConsent != 2;
                _cbPua.Checked = _def.PUAProtection == 1;
                _cbBehavior.Checked = !_def.DisableBehaviorMonitoring;
                _cbScript.Checked = !_def.DisableScriptScanning;
                _cbCfa.Checked = _def.EnableControlledFolderAccess == 1;

                // Marca "(bloqueado pelo Tamper)" quando aplicavel.
                string suffix = _def.IsTamperProtected ? "   — " + I18n.T("Tamper.Blocked") : "";
                ApplyTamperSuffix(_cbRealtime, I18n.T("Adv.Toggle.Realtime"), suffix);
                ApplyTamperSuffix(_cbCloud, I18n.T("Adv.Toggle.Cloud"), suffix);
                ApplyTamperSuffix(_cbSamples, I18n.T("Adv.Toggle.Samples"), suffix);
                ApplyTamperSuffix(_cbPua, I18n.T("Adv.Toggle.Pua"), suffix);
                ApplyTamperSuffix(_cbBehavior, I18n.T("Adv.Toggle.Behavior"), suffix);
                ApplyTamperSuffix(_cbScript, I18n.T("Adv.Toggle.Script"), suffix);
                ApplyTamperSuffix(_cbCfa, I18n.T("Adv.Toggle.Cfa"), suffix);

                foreach (var cb in new[] { _cbRealtime, _cbCloud, _cbSamples, _cbPua, _cbBehavior, _cbScript, _cbCfa })
                    cb.Enabled = avail;
            }
            finally { _loading = false; }
        }

        private void ApplyTamperSuffix(CheckBox cb, string baseText, string suffix)
        {
            cb.Text = baseText + suffix;
            cb.ForeColor = suffix.Length > 0 ? Color.FromArgb(225, 170, 30) : Theme.Text;
        }

        private async Task LoadExclusionsAsync()
        {
            var d = await DefenderService.ReadStatusAsync();
            FillList(_lstPaths, d.ExclusionPath);
            FillList(_lstExt, d.ExclusionExtension);
            FillList(_lstProc, d.ExclusionProcess);
        }

        private void FillList(ListBox list, List<string> items)
        {
            list.BeginUpdate();
            list.Items.Clear();
            if (items != null)
                foreach (var i in items) list.Items.Add(i);
            list.EndUpdate();
        }

        private async Task LoadFwAsync()
        {
            var st = await FirewallService.ReadStatusAsync();
            _loading = true;
            _cbDomain.Checked = st.DomainEnabled;
            _cbPrivate.Checked = st.PrivateEnabled;
            _cbPublic.Checked = st.PublicEnabled;
            _cbDomain.Enabled = _cbPrivate.Enabled = _cbPublic.Enabled = st.Available;
            _cbInteractive.Checked = InteractiveFirewallService.IsActive;
            _loading = false;

            var rules = await FirewallService.ListAppRulesAsync();
            _lvRules.BeginUpdate();
            _lvRules.Items.Clear();
            foreach (var r in rules)
            {
                var item = new ListViewItem(r.DisplayName);
                item.SubItems.Add(r.Program);
                item.SubItems.Add(r.Direction);
                item.SubItems.Add(r.Action);
                _lvRules.Items.Add(item);
            }
            _lvRules.EndUpdate();
        }

        private async Task LoadStartupAsync()
        {
            bool on = await AutostartService.IsEnabledAsync();
            _loading = true;
            _cbStartup.Checked = on;
            _loading = false;
        }

        private void LoadSecurityState()
        {
            _loading = true;
            _cbPassword.Checked = PasswordProtection.IsEnabled;
            _cmbLang.SelectedIndex = I18n.Language == "en" ? 1 : 0;
            _loading = false;
        }
    }
}
