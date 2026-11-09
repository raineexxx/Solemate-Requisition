using System;
using System.Drawing;
using System.Windows.Forms;

// This alias prevents an "ambiguous reference" error with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    /// <summary>Simple themed dialog that asks for a required piece of text (e.g. reason for rejection).</summary>
    public class InputDialog : Form
    {
        private readonly TextBox _text = new TextBox();

        private string Value
        {
            get { return _text.Text.Trim(); }
        }

        private InputDialog(string title, string prompt)
        {
            Text = title;
            ClientSize = new Size(480, 270);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Panel bar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red };

            Label lbl = new Label
            {
                Text = prompt,
                Font = Theme.FontBold,
                ForeColor = Theme.Ink,
                AutoSize = false,
                Location = new Point(20, 24),
                Size = new Size(440, 44)
            };

            _text.Multiline = true;
            _text.MaxLength = 200;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.BorderStyle = BorderStyle.FixedSingle;
            _text.Font = Theme.FontBody;
            _text.Location = new Point(20, 74);
            _text.Size = new Size(440, 110);

            Button ok = Theme.MakeButton("CONFIRM", 130, true, OnOk);
            ok.Location = new Point(330, 205);

            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(210, 205);

            Controls.Add(lbl);
            Controls.Add(_text);
            Controls.Add(ok);
            Controls.Add(cancel);
            Controls.Add(bar);

            CancelButton = cancel;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (Value.Length == 0)
            {
                MessageBox.Show(this, "Please type a reason first.", "Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _text.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Returns the typed text, or null if the user cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string prompt)
        {
            using (InputDialog dlg = new InputDialog(title, prompt))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Value : null;
            }
        }
    }
}
