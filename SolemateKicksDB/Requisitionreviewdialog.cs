using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

// This alias prevents an "ambiguous reference" error with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    /// <summary>One item of a requisition in the review window, with the Head's decision.</summary>
    public class ReviewLineRow
    {
        // read from the database (usp_GetReviewLines)
        public int RequisitionItemId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Quantity { get; set; }
        public int Available { get; set; }
        public string ItemStatus { get; set; }
        public string ItemRejectReason { get; set; }

        // chosen on the screen (not read from the database)
        public bool Approve { get; set; }
        public string Reason { get; set; }

        // Calculated (empty setter: LINQ to SQL fills every property of a result class).
        public string Check
        {
            get { return Available >= Quantity ? "Sufficient" : "Short by " + (Quantity - Available); }
            set { }
        }
    }

    /// <summary>Result of saving the Head's decisions.</summary>
    public class ReviewResult
    {
        public string RefNo { get; set; }
        public string Status { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int ShortCount { get; set; }
    }

    /// <summary>Database work for item-by-item review (calls usp_GetReviewLines and usp_ReviewRequisition).</summary>
    public static class RequisitionReviewService
    {
        public static List<ReviewLineRow> GetReviewLines(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReviewLineRow>("EXEC dbo.usp_GetReviewLines {0}", requisitionId).ToList();
            }
        }

        /// <summary>Saves the decision of every item. Head only (checked in SQL). A rejected item needs a reason.</summary>
        public static ReviewResult ReviewItems(int requisitionId, AppUser head, IEnumerable<ReviewLineRow> lines)
        {
            XElement root = new XElement("items",
                lines.Select(l => new XElement("d",
                    new XAttribute("i", l.RequisitionItemId),
                    new XAttribute("s", l.Approve ? "Approved" : "Rejected"),
                    new XAttribute("r", l.Approve ? string.Empty : (l.Reason ?? string.Empty).Trim()))));

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReviewResult>("EXEC dbo.usp_ReviewRequisition {0}, {1}, {2}",
                    requisitionId, head.UserId, root.ToString(SaveOptions.DisableFormatting)).First();
            }
        }
    }

    /// <summary>
    /// Shows everything about one requisition (who asked, which shoes, how many, is there enough stock).
    /// The Head decides ITEM BY ITEM: untick an item to reject it (with a reason), then SAVE DECISIONS.
    /// Approved items go to Purchasing; rejected items are marked with their reason for the requester.
    /// Everyone else (and the Head on a requisition that is no longer pending) gets a read-only view that shows each item's decision.
    /// The decisions are saved by this window itself, so it closes with Choice = None.
    /// </summary>
    public class RequisitionReviewDialog : Form
    {
        public enum Decision { None, Approve, Reject }

        /// <summary>Always None: the decisions are already saved when this window closes.</summary>
        public Decision Choice { get; private set; }

        /// <summary>Not used any more (each rejected item has its own reason).</summary>
        public string RejectReason { get; private set; }

        private readonly RequisitionRow _req;
        private readonly bool _canDecide;
        private List<ReviewLineRow> _items = new List<ReviewLineRow>();
        private DataGridView grid;
        private Label lblSummary;

        // 'lines' is kept so the existing caller still compiles; the items are loaded here with their decisions.
        public RequisitionReviewDialog(RequisitionRow req, List<RequisitionLineRow> lines, bool canDecide)
        {
            _req = req;
            _canDecide = canDecide;
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

            try
            {
                _items = RequisitionReviewService.GetReviewLines(req.RequisitionId);
                foreach (ReviewLineRow r in _items)
                {
                    r.Approve = r.ItemStatus != "Rejected";   // a pending item starts as approved; the Head unticks to reject
                    r.Reason = r.ItemRejectReason ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            BuildUi();
        }

        private void BuildUi()
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
                Text = _canDecide ? "ITEMS REQUESTED  -  untick an item to reject it" : "ITEMS REQUESTED",
                Font = Theme.FontSection,
                ForeColor = Theme.Red,
                AutoSize = true,
                Location = new Point(20, 132)
            };

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.AutoGenerateColumns = false;
            grid.Dock = DockStyle.None;
            grid.Location = new Point(20, 162);
            grid.Size = new Size(880, 350);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Theme.AddColumn(grid, "Sku", "SKU", "Sku", 12f);
            Theme.AddColumn(grid, "Brand", "Brand", "Brand", 12f);
            Theme.AddColumn(grid, "Product", "Model", "Product", 18f);
            Theme.AddColumn(grid, "Size", "Size (US)", "Size", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Quantity", "Qty requested", "Quantity", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Available", "In stock", "Available", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "Check", "Stock check", "Check", 13f);

            foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = true;

            if (_canDecide)
            {
                grid.ReadOnly = false;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.Columns.Add(new DataGridViewCheckBoxColumn
                {
                    Name = "Approve",
                    HeaderText = "APPROVE?",
                    DataPropertyName = "Approve",
                    ReadOnly = false,
                    FillWeight = 9f
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Reason",
                    HeaderText = "REASON IF REJECTED",
                    DataPropertyName = "Reason",
                    ReadOnly = false,
                    FillWeight = 22f
                });

                grid.CurrentCellDirtyStateChanged += OnDirty;
                grid.CellValueChanged += OnCellValueChanged;
                grid.CellBeginEdit += OnCellBeginEdit;
            }
            else
            {
                grid.ReadOnly = true;
                Theme.AddColumn(grid, "Decision", "Decision", "ItemStatus", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
                Theme.AddColumn(grid, "ItemReason", "Reason", "ItemRejectReason", 20f);
                foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = true;
            }

            grid.CellFormatting += OnCellFormatting;
            grid.DataSource = _items;
            grid.ClearSelection();

            // ---- totals ----
            lblSummary = new Label
            {
                Font = Theme.FontBold,
                AutoSize = false,
                Location = new Point(20, 524),
                Size = new Size(880, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            UpdateSummary();

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

            if (_canDecide)
            {
                Button btnSave = Theme.MakeButton("SAVE DECISIONS", 190, true, OnSave);
                btnSave.Location = new Point(590, 578);
                btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                Button btnRejectAll = Theme.MakeButton("REJECT ALL", 130, false, OnRejectAll);
                btnRejectAll.Location = new Point(450, 578);
                btnRejectAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                Button btnApproveAll = Theme.MakeButton("APPROVE ALL", 140, false, OnApproveAll);
                btnApproveAll.Location = new Point(300, 578);
                btnApproveAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                Controls.Add(btnSave);
                Controls.Add(btnRejectAll);
                Controls.Add(btnApproveAll);
            }

            Controls.Add(bar);
        }

        // ==================================================================
        //  Grid behaviour
        // ==================================================================
        private ReviewLineRow RowAt(int rowIndex)
        {
            return rowIndex < 0 ? null : grid.Rows[rowIndex].DataBoundItem as ReviewLineRow;
        }

        // a tick is saved at once, so the summary updates without leaving the cell
        private void OnDirty(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            ReviewLineRow row = RowAt(e.RowIndex);
            if (row == null) return;

            if (grid.Columns[e.ColumnIndex].Name == "Approve" && row.Approve)
                row.Reason = string.Empty;           // an approved item has no reject reason

            grid.InvalidateRow(e.RowIndex);
            UpdateSummary();
        }

        // the reason can only be typed for an item that is unticked (rejected)
        private void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            ReviewLineRow row = RowAt(e.RowIndex);
            if (row != null && grid.Columns[e.ColumnIndex].Name == "Reason" && row.Approve)
                e.Cancel = true;
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            ReviewLineRow row = RowAt(e.RowIndex);
            if (row == null) return;

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
            else if (col == "Decision")
            {
                e.CellStyle.Font = Theme.FontBold;
                e.CellStyle.ForeColor = row.ItemStatus == "Approved" ? Theme.Ok
                                      : row.ItemStatus == "Rejected" ? Theme.Red : Theme.Muted;
            }
            else if (col == "Reason" && row.Approve)
            {
                e.CellStyle.BackColor = Color.WhiteSmoke;   // greyed out: only a rejected item needs a reason
            }
        }

        private void UpdateSummary()
        {
            int pairs = _items.Sum(l => l.Quantity);
            int shortCount = _items.Count(l => l.Available < l.Quantity);

            if (_canDecide)
            {
                int approved = _items.Count(l => l.Approve);
                int rejected = _items.Count - approved;
                lblSummary.Text = string.Format("{0} item(s)   |   {1} to approve   |   {2} to reject   |   {3}",
                    _items.Count, approved, rejected,
                    shortCount == 0 ? "stock is enough for every item" : shortCount + " item(s) short on stock");
                lblSummary.ForeColor = rejected > 0 ? Theme.Red : Theme.Ok;
            }
            else
            {
                int approved = _items.Count(l => l.ItemStatus == "Approved");
                int rejected = _items.Count(l => l.ItemStatus == "Rejected");
                lblSummary.Text = string.Format("{0} item(s)   |   {1} pair(s) requested   |   {2} approved   |   {3} rejected",
                    _items.Count, pairs, approved, rejected);
                lblSummary.ForeColor = rejected > 0 ? Theme.Red : Theme.Ink;
            }
        }

        // ==================================================================
        //  Buttons
        // ==================================================================
        private void OnApproveAll(object sender, EventArgs e)
        {
            grid.EndEdit();
            foreach (ReviewLineRow r in _items) { r.Approve = true; r.Reason = string.Empty; }
            grid.Refresh();
            UpdateSummary();
        }

        private void OnRejectAll(object sender, EventArgs e)
        {
            grid.EndEdit();
            string reason = InputDialog.Ask(this, "Reject all items",
                "Reason for rejecting every item of " + _req.RefNo + " (the requester will see this):");
            if (reason == null) return;

            foreach (ReviewLineRow r in _items) { r.Approve = false; r.Reason = reason; }
            grid.Refresh();
            UpdateSummary();
        }

        private void OnSave(object sender, EventArgs e)
        {
            grid.EndEdit();

            if (_items.Count == 0) return;

            foreach (ReviewLineRow r in _items)
            {
                if (!r.Approve && string.IsNullOrWhiteSpace(r.Reason))
                {
                    MessageBox.Show(this,
                        "Type a reason for the rejected item " + r.Sku + " (" + r.Brand + " " + r.Product + ", US " + r.Size + ").",
                        "Reason needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            int approved = _items.Count(l => l.Approve);
            int rejected = _items.Count - approved;

            string question = string.Format("Save the decisions for {0}?\r\n\r\n{1} item(s) approved, {2} item(s) rejected.",
                _req.RefNo, approved, rejected);
            if (approved > 0)
                question += "\r\n\r\nApproved items can then be ordered from Purchasing.";

            if (MessageBox.Show(this, question, "Save decisions", MessageBoxButtons.YesNo,
                    rejected > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                ReviewResult res = RequisitionReviewService.ReviewItems(_req.RequisitionId, Session.CurrentUser, _items);

                string done = res.RefNo + " is now " + res.Status + ".\r\n\r\n"
                    + res.ApprovedCount + " item(s) approved, " + res.RejectedCount + " item(s) rejected.";
                if (res.ShortCount > 0)
                    done += "\r\n" + res.ShortCount + " approved item(s) are short on stock - order them from Purchasing.";
                done += "\r\n\r\nPress REFRESH on the requisition list to see the new status.";

                MessageBox.Show(this, done, "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Choice = Decision.None;
                DialogResult = DialogResult.Cancel;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}