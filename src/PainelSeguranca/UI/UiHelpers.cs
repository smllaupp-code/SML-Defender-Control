using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PainelSeguranca.Core;

namespace PainelSeguranca.UI
{
    /// <summary>Cores do tema, seguindo claro/escuro do sistema.</summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; }
        public static Color Background { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceHover { get; private set; }
        public static Color Text { get; private set; }
        public static Color Subtle { get; private set; }
        public static Color Border { get; private set; }
        public static Color Accent { get; private set; }

        static Theme() { Detect(); }

        public static void Detect()
        {
            IsDark = ReadAppsUseLightTheme() == 0;
            if (IsDark)
            {
                Background = Color.FromArgb(32, 32, 36);
                Surface = Color.FromArgb(45, 45, 50);
                SurfaceHover = Color.FromArgb(58, 58, 64);
                Text = Color.FromArgb(240, 240, 240);
                Subtle = Color.FromArgb(170, 170, 175);
                Border = Color.FromArgb(70, 70, 76);
                Accent = Color.FromArgb(80, 160, 255);
            }
            else
            {
                Background = Color.FromArgb(245, 246, 248);
                Surface = Color.White;
                SurfaceHover = Color.FromArgb(235, 240, 248);
                Text = Color.FromArgb(28, 28, 30);
                Subtle = Color.FromArgb(95, 99, 104);
                Border = Color.FromArgb(220, 222, 226);
                Accent = Color.FromArgb(0, 102, 204);
            }
        }

        private static int ReadAppsUseLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("AppsUseLightTheme");
                        if (v is int i) return i;
                    }
                }
            }
            catch { }
            return 1; // padrao: tema claro
        }
    }

    /// <summary>
    /// Botao grande de duas linhas: titulo claro + 1 linha de explicacao.
    /// Tudo clicavel, com realce no hover. Para usuario leigo, sem jargao.
    /// </summary>
    public sealed class ActionTile : Panel
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private bool _hover;

        public event EventHandler Activated;

        public ActionTile(string title, string subtitle, string glyph = "•")
        {
            DoubleBuffered = true;
            Height = 68;
            Margin = new Padding(0, 0, 0, 10);
            Padding = new Padding(14, 8, 14, 8);
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;

            var icon = new Label
            {
                Text = glyph,
                AutoSize = false,
                Width = 34,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Emoji", 16f, FontStyle.Regular),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent
            };

            _title = new Label
            {
                Text = title,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            };

            _subtitle = new Label
            {
                Text = subtitle,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = Theme.Subtle,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            };

            var textPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            textPanel.Controls.Add(_subtitle);
            textPanel.Controls.Add(_title);

            Controls.Add(textPanel);
            Controls.Add(icon);

            WireClicks(this);
            WireClicks(icon);
            WireClicks(textPanel);
            WireClicks(_title);
            WireClicks(_subtitle);
        }

        public void SetTitle(string t) { _title.Text = t; }
        public void SetSubtitle(string s) { _subtitle.Text = s; }

        private void WireClicks(Control c)
        {
            c.Click += (s, e) => OnActivated();
            c.MouseEnter += (s, e) => SetHover(true);
            c.MouseLeave += (s, e) => SetHover(IsMouseInside());
        }

        private bool IsMouseInside()
        {
            return ClientRectangle.Contains(PointToClient(Cursor.Position));
        }

        private void SetHover(bool h)
        {
            if (_hover == h) return;
            _hover = h;
            BackColor = h ? Theme.SurfaceHover : Theme.Surface;
        }

        private void OnActivated()
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Atalhos de dialogo, sempre localizados e com icone adequado.</summary>
    public static class Dialogs
    {
        public static bool Confirm(IWin32Window owner, string message)
        {
            return MessageBox.Show(owner, message, I18n.T("Confirm.Title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static void Info(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, I18n.T("App.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Error(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, I18n.T("App.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Warn(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, I18n.T("App.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Explica o Tamper Protection e oferece ABRIR a Seguranca do Windows ja na tela certa.
        /// O Windows nao permite que apps desliguem o Tamper por codigo (por design e por seguranca);
        /// o melhor que podemos fazer e levar o usuario direto ao botao.
        /// </summary>
        public static void TamperHelp(IWin32Window owner)
        {
            var r = MessageBox.Show(owner,
                I18n.T("Tamper.Explain") + "\n\n" + I18n.T("Tamper.OpenAsk"),
                I18n.T("App.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (r == DialogResult.Yes) OpenWindowsSecurity();
        }

        /// <summary>Abre a Seguranca do Windows na pagina de Protecao contra virus e ameacas.</summary>
        public static void OpenWindowsSecurity()
        {
            // Deep-link oficial do app Seguranca do Windows (onde fica o toggle do Tamper).
            foreach (var uri in new[] { "windowsdefender://threatsettings", "windowsdefender://threat", "windowsdefender:" })
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    return;
                }
                catch { /* tenta o proximo */ }
            }
        }
    }

    /// <summary>Estilo padrao para botoes comuns (rodape).</summary>
    public static class Style
    {
        public static void FlatButton(Button b, bool primary = false)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.Font = new Font("Segoe UI", 9.75f, primary ? FontStyle.Bold : FontStyle.Regular);
            b.ForeColor = primary ? Color.White : Theme.Text;
            b.BackColor = primary ? Theme.Accent : Theme.Surface;
            b.Height = 34;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
        }
    }
}
