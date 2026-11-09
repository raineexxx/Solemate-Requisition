using SolemateKicks;
using System;
using System.Drawing;
using System.Windows.Forms;

// This alias prevents an "ambiguous reference" error with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    public class LoginForm : Form
    {
        private readonly TextBox txtUser = new TextBox();
        private readonly TextBox txtPass = new TextBox();
        private readonly Label lblError = new Label();
        private Button btnLogin;

        public LoginForm()
        {
            Text = "Solemate - Sign in";
            ClientSize = new Size(840, 480);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            // ---- Left: red brand panel ----
            Panel left = new Panel { Dock = DockStyle.Left, Width = 360, BackColor = Theme.Red };

            LogoControl logo = new LogoControl { Size = new Size(210, 210) };
            logo.Location = new Point((left.Width - logo.Width) / 2, 80);

            Label tagline = new Label
            {
                Text = "REQUISITION SYSTEM",
                ForeColor = Theme.White,
                BackColor = Theme.Red,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold | FontStyle.Italic),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(0, 310, 360, 30)
            };
            left.Controls.Add(logo);
            left.Controls.Add(tagline);

            // ---- Right: sign-in form ----
            Panel right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.White };

            Label lblWelcome = new Label
            {
                Text = "WELCOME BACK",
                Font = new Font("Segoe UI", 22f, FontStyle.Bold | FontStyle.Italic),
                ForeColor = Theme.Red,
                AutoSize = true,
                Location = new Point(50, 60)
            };

            Label lblSub = new Label
            {
                Text = "Sign in to create and manage stock requisitions.",
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(53, 112)
            };

            Label lblU = new Label { Text = "Username", Font = Theme.FontBold, ForeColor = Theme.Ink, AutoSize = true, Location = new Point(53, 160) };
            txtUser.Font = new Font("Segoe UI", 11f);
            txtUser.BorderStyle = BorderStyle.FixedSingle;
            txtUser.Bounds = new Rectangle(53, 184, 340, 30);

            Label lblP = new Label { Text = "Password", Font = Theme.FontBold, ForeColor = Theme.Ink, AutoSize = true, Location = new Point(53, 232) };
            txtPass.Font = new Font("Segoe UI", 11f);
            txtPass.BorderStyle = BorderStyle.FixedSingle;
            txtPass.UseSystemPasswordChar = true;
            txtPass.Bounds = new Rectangle(53, 256, 340, 30);

            btnLogin = Theme.MakeButton("SIGN IN", 340, true, OnLogin);
            btnLogin.Height = 42;
            btnLogin.Location = new Point(53, 312);

            lblError.ForeColor = Theme.Red;
            lblError.Font = Theme.FontBold;
            lblError.AutoSize = false;
            lblError.Bounds = new Rectangle(53, 362, 340, 24);

            Label lblHint = new Label
            {
                Text = "Demo accounts:  cashier / cashier123   |   manager / manager123   |   head / head123",
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                AutoSize = false,
                Bounds = new Rectangle(53, 410, 380, 36)
            };

            right.Controls.Add(lblWelcome);
            right.Controls.Add(lblSub);
            right.Controls.Add(lblU);
            right.Controls.Add(txtUser);
            right.Controls.Add(lblP);
            right.Controls.Add(txtPass);
            right.Controls.Add(btnLogin);
            right.Controls.Add(lblError);
            right.Controls.Add(lblHint);

            // Fill first, then the edge-docked panel (later-added controls are docked first)
            Controls.Add(right);
            Controls.Add(left);

            AcceptButton = btnLogin;
            Shown += (s, e) => txtUser.Focus();
        }

        private void OnLogin(object sender, EventArgs e)
        {
            lblError.Text = string.Empty;

            if (txtUser.Text.Trim().Length == 0 || txtPass.Text.Length == 0)
            {
                lblError.Text = "Enter your username and password.";
                return;
            }

            try
            {
                AppUser user = RequisitionService.Authenticate(txtUser.Text.Trim(), txtPass.Text);
                if (user == null)
                {
                    lblError.Text = "Invalid username or password.";
                    txtPass.Clear();
                    txtPass.Focus();
                    return;
                }

                Session.CurrentUser = user;
                DialogResult = DialogResult.OK;     // closes the dialog
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
