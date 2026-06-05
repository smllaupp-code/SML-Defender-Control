using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PainelSeguranca.Core;
using PainelSeguranca.Services;

namespace PainelSeguranca.UI
{
    /// <summary>
    /// Pop-up do modo interativo do firewall: um app foi barrado, o usuario decide
    /// Permitir (cria regra) ou Bloquear (mantem barrado nesta sessao).
    /// Fica sempre no topo para o usuario nao perder a decisao.
    /// </summary>
    public sealed class FirewallPromptForm : Form
    {
        public enum Decision { Allow, Block }

        public Decision Result { get; private set; } = Decision.Block;

        public FirewallPromptForm(BlockedAppInfo info)
        {
            Text = I18n.T("App.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            ClientSize = new Size(460, 220);
            try { Icon = IconFactory.CreateTrayIcon(StatusColor.Yellow); } catch { }

            string fileName = SafeFileName(info.ApplicationPath);

            var icon = new PictureBox
            {
                Image = IconFactory.CreateShieldBitmap(48, StatusColor.Yellow),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48),
                Location = new Point(20, 20),
                BackColor = Color.Transparent
            };

            var title = new Label
            {
                Text = I18n.T("Interactive.Prompt.Title", fileName),
                Location = new Point(84, 20),
                Size = new Size(356, 44),
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Theme.Text
            };

            var path = new Label
            {
                Text = info.ApplicationPath,
                Location = new Point(84, 66),
                Size = new Size(356, 34),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Theme.Subtle
            };

            string dest = string.IsNullOrEmpty(info.RemoteAddress) ? "" :
                I18n.T("Interactive.Prompt.Dest", info.RemoteAddress, info.RemotePort ?? "");
            var destination = new Label
            {
                Text = dest,
                Location = new Point(84, 100),
                Size = new Size(356, 20),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Theme.Subtle
            };

            var hint = new Label
            {
                Text = I18n.T("Interactive.Prompt.Hint"),
                Location = new Point(20, 128),
                Size = new Size(420, 30),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Theme.Subtle
            };

            var btnAllow = new Button { Text = I18n.T("Interactive.Allow"), Location = new Point(140, 168), Size = new Size(150, 38) };
            var btnBlock = new Button { Text = I18n.T("Interactive.Block"), Location = new Point(298, 168), Size = new Size(142, 38) };
            Style.FlatButton(btnAllow, true);
            Style.FlatButton(btnBlock);
            btnAllow.Click += (s, e) => { Result = Decision.Allow; DialogResult = DialogResult.OK; Close(); };
            btnBlock.Click += (s, e) => { Result = Decision.Block; DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(icon);
            Controls.Add(title);
            Controls.Add(path);
            Controls.Add(destination);
            Controls.Add(hint);
            Controls.Add(btnAllow);
            Controls.Add(btnBlock);
            AcceptButton = btnAllow;
            CancelButton = btnBlock;
        }

        private static string SafeFileName(string p)
        {
            try { return Path.GetFileName(p); } catch { return p; }
        }
    }
}
