using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// This alias prevents an "ambiguous reference" error with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    /// <summary>
    /// Shows everything about one requisition (who asked, which shoes, how many, is there enough stock).
    /// The Head sees this before deciding: APPROVE or REJECT (with a reason).
    /// Everyone else (and the Head on a requisition that is no longer pending) gets a read-only view.
    /// </summary>
    public class RequisitionReviewDialog : Form
    {
        public enum Decision { None, Approve, Reject }

        /// <summary>What the Head chose (None when the dialog was just closed).</summary>
        public Decision Choice { get; private set; }

        /// <summary>The reason typed for a rejection.</summary>
        public string RejectReason { get; private set; }

        private readonly RequisitionRow _req;
        private readonly List<RequisitionLineRow> _lines;
        private DataGridView grid;

        public RequisitionReviewDialog(RequisitionRow req, List<RequisitionLineRow> lines, bool canDecide)
        {
            _req = req;
            _lines = lines;
            Choice = Decision.None;

            Text = "Solemate - Requisition " + req.RefNo;
            ClientSize = new Size(920, 640);
            MinimumSize = new Size(780, 540);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            BuildUi(canDecide);
        }

        private void BuildUi(bool canDecide)
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red };

            // ---- title: reference number + status ----
            Label lblTitle = new Label
            {
                Text = _req.RefNo,
                Font = Theme.FontHeading,
                ForeColor = Theme.Red,
                AutoSize = true,
                Location = new Point(20, 18)
            };

            Label lblStatus = new Label
            {
                Text = _req.Status.ToUpper(),
                Font = Theme.FontBold,
                ForeColor = Theme.StatusColor(_req.Status),
                AutoSize = true,
                Location = new Point(lblTitle.Right + 16, 34)
            };
            lblTitle.SizeChanged += (s, e) => lblStatus.Left = lblTitle.Right + 16;

            // ---- who / when ----
            Label lblWho = new Label
            {
                Text = string.Format("Requested by {0}   |   {1}   |   {2:MMM dd, yyyy  h:mm tt}",
                    _req.RequestedBy, _req.Branch, _req.DateCreated),
                Font = Theme.FontBold,
                ForeColor = Theme.Ink,
                AutoSize = false,
                Location = new Point(20, 66),
                Size = new Size(880, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // ---- remarks / reject reason / stock check ----
            List<string> notes = new List<string>();
            notes.Add("Remarks: " + (string.IsNullOrWhiteSpace(_req.Remarks) ? "none" : _req.Remarks));
            if (_req.Status == Statuses.Rejected && !string.IsNullOrWhiteSpace(_req.RejectReason))
                notes.Add("Rejected: " + _req.RejectReason);
            if (!string.IsNullOrWhiteSpace(_req.ReviewedBy))
                notes.Add("Reviewed by " + _req.ReviewedBy);
            if (!string.IsNullOrWhiteSpace(_req.StockCheck))
                notes.Add("Stock check: " + _req.StockCheck);

            Label lblNotes = new Label
            {
                Text = string.Join("      |      ", notes),
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Location = new Point(20, 92),
                Size = new Size(880, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // ---- the items ----
            Label lblItems = new Label
            {
                Text = "ITEMS REQUESTED",
                Font = Theme.FontSection,
                ForeColor = Theme.Red,
                AutoSize = true,
                Location = new Point(20, 132)
            };

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.None;
            grid.Location = new Point(20, 162);
            grid.Size = new Size(880, 350);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ReadOnly = true;
            Theme.AddColumn(grid, "Sku", "SKU", "Sku", 13f);
            Theme.AddColumn(grid, "Brand", "Brand", "Brand", 14f);
            Theme.AddColumn(grid, "Product", "Model", "Product", 24f);
            Theme.AddColumn(grid, "Size", "Size (US)", "Size", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Quantity", "Qty requested", "Quantity", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Available", "In stock", "Available", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Check", "Stock check", "Check", 16f);

            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string col = grid.Columns[e.ColumnIndex].Name;
                if (col == "Check")
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = Convert.ToString(e.Value) == "Sufficient" ? Theme.Ok : Theme.Red;
                }
                else if (col == "Quantity")
                {
                    e.CellStyle.Font = Theme.FontBold;
                }
            };
            grid.DataSource = _lines;
            grid.ClearSelection();

            // ---- totals ----
            int pairs = _lines.Sum(l => l.Quantity);
            int shortCount = _lines.Count(l => l.Available < l.Quantity);

            Label lblSummary = new Label
            {
                Text = string.Format("{0} item(s)   |   {1} pair(s) requested   |   {2}",
                    _lines.Count, pairs,
                    shortCount == 0 ? "stock is enough for every item" : shortCount + " item(s) short on stock"),
                Font = Theme.FontBold,
                ForeColor = shortCount == 0 ? Theme.Ok : Theme.Red,
                AutoSize = false,
                Location = new Point(20, 524),
                Size = new Size(880, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // ---- buttons ----
            Button btnClose = Theme.MakeButton("CLOSE", 110, false, (s, e) => { Choice = Decision.None; DialogResult = DialogResult.Cancel; });
            btnClose.Location = new Point(790, 578);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = btnClose;

            Controls.Add(grid);
            Controls.Add(lblTitle);
            Controls.Add(lblStatus);
            Controls.Add(lblWho);
            Controls.Add(lblNotes);
            Controls.Add(lblItems);
            Controls.Add(lblSummary);
            Controls.Add(btnClose);

            if (canDecide)
            {
                Button btnApprove = Theme.MakeButton("APPROVE", 150, true, OnApprove);
                btnApprove.Location = new Point(470, 578);
                btnApprove.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                Button btnReject = Theme.MakeButton("REJECT", 130, false, OnReject);
                btnReject.Location = new Point(630, 578);
                btnReject.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                Controls.Add(btnApprove);
                Controls.Add(btnReject);
            }

            Controls.Add(bar);
        }

        private void OnApprove(object sender, EventArgs e)
        {
            int shortCount = _lines.Count(l => l.Available < l.Quantity);

            string question = shortCount == 0
                ? "Approve requisition " + _req.RefNo + "?"
                : "Approve requisition " + _req.RefNo + "?\r\n\r\n" + shortCount +
                  " item(s) are short on stock, so a purchase order will be needed for them.";

            if (MessageBox.Show(this, question, "Approve",
                    MessageBoxButtons.YesNo, shortCount == 0 ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            Choice = Decision.Approve;
            DialogResult = DialogResult.OK;
        }

        private void OnReject(object sender, EventArgs e)
        {
            string reason = InputDialog.Ask(this, "Reject requisition",
                "Reason for rejecting " + _req.RefNo + " (the requester will see this):");
            if (reason == null) return;

            RejectReason = reason;
            Choice = Decision.Reject;
            DialogResult = DialogResult.OK;
        }
    }
}