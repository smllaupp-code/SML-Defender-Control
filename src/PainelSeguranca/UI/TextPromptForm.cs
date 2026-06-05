using System;
using System.Drawing;
using System.Windows.Forms;
using PainelSeguranca.Core;

namespace PainelSeguranca.UI
{
    /// <summary>Dialogo simples de entrada de texto de uma linha (ex.: extensao, processo).</summary>
    public sealed class TextPromptForm : Form
    {
        private readonly TextBox _input;

        public string Value { get { return _input.Text.Trim(); } }

        public TextPromptForm(string title, string prompt, string initial = "")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 130);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);

            var lbl = new Label
            {
                Text = prompt,
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(388, 22),
                ForeColor = Theme.Text
            };

            _input = new TextBox
            {
                Location = new Point(16, 44),
                Size = new Size(388, 26),
                Text = initial,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };

            var ok = new Button { Text = I18n.T("Btn.Ok"), DialogResult = DialogResult.OK, Location = new Point(228, 86), Size = new Size(84, 32) };
            var cancel = new Button { Text = I18n.T("Btn.Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(320, 86), Size = new Size(84, 32) };
            Style.FlatButton(ok, true);
            Style.FlatButton(cancel);

            Controls.Add(lbl);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>Atalho: mostra o dialogo e retorna o texto, ou null se cancelado/vazio.</summary>
        public static string Ask(IWin32Window owner, string title, string prompt, string initial = "")
        {
            using (var f = new TextPromptForm(title, prompt, initial))
            {
                if (f.ShowDialog(owner) == DialogResult.OK && f.Value.Length > 0)
                    return f.Value;
                return null;
            }
        }
    }
}
