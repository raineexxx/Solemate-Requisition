using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// This alias prevents an "ambiguous reference" error with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    /// <summary>Colors, fonts and styling helpers taken from the Solemate logo (red / white / black).</summary>
    public static class Theme
    {
        // ---- Palette ----
        public static readonly Color Red = Color.FromArgb(230, 0, 0);        // logo red  #E60000
        public static readonly Color RedDark = Color.FromArgb(176, 0, 0);    // hover / shadow
        public static readonly Color RedSoft = Color.FromArgb(253, 232, 232);// light tint for selection / highlight
        public static readonly Color White = Color.White;
        public static readonly Color Ink = Color.FromArgb(24, 24, 24);       // near-black text
        public static readonly Color Canvas = Color.FromArgb(244, 244, 244); // page background
        public static readonly Color Line = Color.FromArgb(222, 222, 222);
        public static readonly Color Muted = Color.FromArgb(110, 110, 110);
        public static readonly Color Ok = Color.FromArgb(22, 128, 60);       // status: approved / in stock
        public static readonly Color Warn = Color.FromArgb(196, 116, 0);     // status: pending / low stock

        // ---- Fonts (bold italic echoes the logo lettering) ----
        public static readonly Font FontBody = new Font("Segoe UI", 10f);
        public static readonly Font FontBold = new Font("Segoe UI", 10f, FontStyle.Bold);
        public static readonly Font FontButton = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 9f);
        public static readonly Font FontSection = new Font("Segoe UI", 11.5f, FontStyle.Bold | FontStyle.Italic);
        public static readonly Font FontHeading = new Font("Segoe UI", 20f, FontStyle.Bold | FontStyle.Italic);

        public static Color StatusColor(string status)
        {
            switch (status)
            {
                case Statuses.Approved: return Ok;
                case Statuses.Pending: return Warn;
                case Statuses.Rejected: return Red;
                default: return Muted;
            }
        }

        // ---- Buttons ----
        public static Button MakeButton(string text, int width, bool primary, EventHandler onClick)
        {
            Button b = new Button { Text = text, Width = width };
            StyleButton(b, primary);
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>primary = solid red, otherwise white with red outline. Disabled buttons turn grey.</summary>
        public static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.Font = FontButton;
            b.Height = 38;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 1;

            Action apply = () =>
            {
                if (!b.Enabled)
                {
                    b.BackColor = Line;
                    b.ForeColor = Muted;
                    b.FlatAppearance.BorderColor = Line;
                    b.FlatAppearance.MouseOverBackColor = Line;
                    b.FlatAppearance.MouseDownBackColor = Line;
                    b.Cursor = Cursors.Default;
                    return;
                }

                b.Cursor = Cursors.Hand;
                b.FlatAppearance.BorderColor = Red;
                if (primary)
                {
                    b.BackColor = Red;
                    b.ForeColor = White;
                    b.FlatAppearance.MouseOverBackColor = RedDark;
                    b.FlatAppearance.MouseDownBackColor = RedDark;
                }
                else
                {
                    b.BackColor = White;
                    b.ForeColor = Red;
                    b.FlatAppearance.MouseOverBackColor = RedSoft;
                    b.FlatAppearance.MouseDownBackColor = RedSoft;
                }
            };

            apply();
            b.EnabledChanged += (s, e) => apply();
        }

        /// <summary>White-outlined button for use on the red header bar.</summary>
        public static void StyleButtonOnRed(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.Font = FontButton;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Red;
            b.ForeColor = White;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = White;
            b.FlatAppearance.MouseOverBackColor = RedDark;
            b.FlatAppearance.MouseDownBackColor = RedDark;
        }

        // ---- Layout pieces ----
        /// <summary>Red top bar with the logo and a page title.</summary>
        public static Panel MakeHeader(string title, string subtitle)
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Red, Margin = new Padding(0) };

            LogoControl logo = new LogoControl { Location = new Point(20, 6), Size = new Size(64, 64) };

            Label lblTitle = new Label
            {
                Text = title.ToUpper(),
                Font = FontHeading,
                ForeColor = White,
                BackColor = Red,
                AutoSize = true,
                Location = new Point(100, 8)
            };

            Label lblSub = new Label
            {
                Text = subtitle,
                Font = FontSmall,
                ForeColor = Color.FromArgb(255, 220, 220),
                BackColor = Red,
                AutoSize = true,
                Location = new Point(104, 48)
            };

            p.Controls.Add(logo);
            p.Controls.Add(lblTitle);
            p.Controls.Add(lblSub);
            return p;
        }

        /// <summary>White "card" panel with a thin border.</summary>
        public static Panel MakeCard(Padding padding)
        {
            Panel p = new Panel { BackColor = White, Dock = DockStyle.Fill, Padding = padding, Margin = new Padding(8) };
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Line))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            p.Resize += (s, e) => p.Invalidate();
            return p;
        }

        public static Label MakeSectionTitle(string text)
        {
            return new Label
            {
                Text = text.ToUpper(),
                Font = FontSection,
                ForeColor = Red,
                Dock = DockStyle.Top,
                Height = 32,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        // ---- Grids ----
        public static void StyleGrid(DataGridView g)
        {
            g.AutoGenerateColumns = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToResizeColumns = false;
            g.MultiSelect = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.BackgroundColor = White;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Line;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 36;
            g.RowTemplate.Height = 32;
            g.Dock = DockStyle.Fill;

            g.ColumnHeadersDefaultCellStyle.BackColor = Red;
            g.ColumnHeadersDefaultCellStyle.ForeColor = White;
            g.ColumnHeadersDefaultCellStyle.Font = FontBold;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Red;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = White;
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            g.DefaultCellStyle.Font = FontBody;
            g.DefaultCellStyle.BackColor = White;
            g.DefaultCellStyle.ForeColor = Ink;
            g.DefaultCellStyle.SelectionBackColor = RedSoft;
            g.DefaultCellStyle.SelectionForeColor = Ink;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
        }

        public static DataGridViewTextBoxColumn AddColumn(DataGridView grid, string name, string header, string dataProperty,
            float fillWeight, string format = null, bool readOnly = true,
            DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleLeft)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = dataProperty,
                FillWeight = fillWeight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            col.DefaultCellStyle.Alignment = align;
            col.HeaderCell.Style.Alignment = align;
            if (format != null) col.DefaultCellStyle.Format = format;
            grid.Columns.Add(col);
            return col;
        }
    }

    /// <summary>
    /// The Solemate logo drawn with GDI+: red ring, white disc, bold italic black "Sole / mate"
    /// and the small (PH) badge. No image file needed - it scales to any square size.
    /// </summary>
    public class LogoControl : Control
    {
        public LogoControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(64, 64);
            BackColor = Theme.Red;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Theme.Red);

            float d = Math.Min(Width, Height) - 3f;
            float ox = (Width - d) / 2f;
            float oy = (Height - d) / 2f;

            // red ring (outlined in white so it shows on the red header) + white disc
            using (SolidBrush red = new SolidBrush(Theme.Red))
                g.FillEllipse(red, ox, oy, d, d);
            using (Pen rim = new Pen(Color.White, Math.Max(1.5f, d * 0.02f)))
                g.DrawEllipse(rim, ox, oy, d, d);

            float ring = d * 0.10f;
            RectangleF disc = new RectangleF(ox + ring, oy + ring, d - 2f * ring, d - 2f * ring);
            g.FillEllipse(Brushes.White, disc);

            // ---- "Sole" over "mate" (bold italic, black) ----
            StringFormat fmt = StringFormat.GenericTypographic;
            float maxWidth = disc.Width * 0.80f;
            float refPx = 100f;
            float wSole, wMate;
            using (Font refFont = new Font("Segoe UI", refPx, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
            {
                wSole = g.MeasureString("Sole", refFont, PointF.Empty, fmt).Width;
                wMate = g.MeasureString("mate", refFont, PointF.Empty, fmt).Width;
            }

            float px = Math.Min(refPx * maxWidth / Math.Max(wSole, wMate), disc.Height * 0.42f);
            using (Font f = new Font("Segoe UI", px, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Theme.Ink))
            {
                SizeF s1 = g.MeasureString("Sole", f, PointF.Empty, fmt);
                SizeF s2 = g.MeasureString("mate", f, PointF.Empty, fmt);

                float lineH = s1.Height * 0.80f;
                float top = disc.Y + (disc.Height - lineH * 2f) / 2f - lineH * 0.05f;
                float x1 = disc.X + (disc.Width - Math.Max(s1.Width, s2.Width)) / 2f;
                float x2 = disc.X + (disc.Width - s2.Width) / 2f + px * 0.04f;

                g.DrawString("Sole", f, ink, x1, top, fmt);
                g.DrawString("mate", f, ink, x2, top + lineH, fmt);

                // ---- small (PH) badge, only when the logo is big enough to read it ----
                if (d >= 120f)
                {
                    float bd = d * 0.085f;
                    float bx = x1 + s1.Width + d * 0.035f;
                    float by = top + s1.Height * 0.30f;
                    using (Pen pen = new Pen(Theme.Ink, 1.2f))
                        g.DrawEllipse(pen, bx, by, bd, bd);
                    using (Font small = new Font("Segoe UI", bd * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("PH", small, ink, new RectangleF(bx, by, bd, bd), center);
                }
            }
        }
    }
}
