using System;
using System.Drawing;
using System.Windows.Forms;
using PainelSeguranca.Core;

namespace PainelSeguranca.UI
{
    /// <summary>
    /// Dialogo de senha. Dois modos:
    /// - Verificar: pede a senha e confere contra o hash PBKDF2 salvo.
    /// - Definir: pede nova senha + confirmacao e grava o hash.
    /// </summary>
    public sealed class PasswordPromptForm : Form
    {
        private readonly bool _setMode;
        private readonly TextBox _pwd;
        private readonly TextBox _confirm;
        private readonly Label _error;

        private PasswordPromptForm(bool setMode)
        {
            _setMode = setMode;
            Text = I18n.T("Pwd.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            ClientSize = new Size(380, setMode ? 200 : 150);

            int y = 16;
            var lbl1 = new Label { Text = setMode ? I18n.T("Pwd.New") : I18n.T("Pwd.Prompt"), Location = new Point(16, y), Size = new Size(348, 20), ForeColor = Theme.Text };
            y += 24;
            _pwd = new TextBox { UseSystemPasswordChar = true, Location = new Point(16, y), Size = new Size(348, 26), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            y += 36;

            Controls.Add(lbl1);
            Controls.Add(_pwd);

            if (setMode)
            {
                var lbl2 = new Label { Text = I18n.T("Pwd.Confirm"), Location = new Point(16, y), Size = new Size(348, 20), ForeColor = Theme.Text };
                y += 24;
                _confirm = new TextBox { UseSystemPasswordChar = true, Location = new Point(16, y), Size = new Size(348, 26), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                y += 36;
                Controls.Add(lbl2);
                Controls.Add(_confirm);
            }

            _error = new Label { Text = "", Location = new Point(16, y), Size = new Size(348, 20), ForeColor = Color.FromArgb(210, 60, 55) };
            y += 24;
            Controls.Add(_error);

            var ok = new Button { Text = I18n.T("Btn.Ok"), Location = new Point(188, y), Size = new Size(84, 32) };
            var cancel = new Button { Text = I18n.T("Btn.Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(280, y), Size = new Size(84, 32) };
            Style.FlatButton(ok, true);
            Style.FlatButton(cancel);
            ok.Click += OnOk;
            Controls.Add(ok);
            Controls.Add(cancel);
            ClientSize = new Size(380, y + 48);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_setMode)
            {
                if (_pwd.Text.Length == 0) { _error.Text = I18n.T("Pwd.Empty"); return; }
                if (_pwd.Text != _confirm.Text) { _error.Text = I18n.T("Pwd.Mismatch"); return; }
                PasswordProtection.SetPassword(_pwd.Text);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                if (PasswordProtection.Verify(_pwd.Text))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    _error.Text = I18n.T("Pwd.Wrong");
                    _pwd.SelectAll();
                    _pwd.Focus();
                }
            }
        }

        /// <summary>Pede a senha para verificar. Retorna true se conferiu (ou se nao ha senha).</summary>
        public static bool RequireUnlock(IWin32Window owner)
        {
            if (!PasswordProtection.IsEnabled) return true;
            using (var f = new PasswordPromptForm(false))
                return f.ShowDialog(owner) == DialogResult.OK;
        }

        /// <summary>Define/altera a senha. Retorna true se gravada.</summary>
        public static bool SetNewPassword(IWin32Window owner)
        {
            using (var f = new PasswordPromptForm(true))
                return f.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
