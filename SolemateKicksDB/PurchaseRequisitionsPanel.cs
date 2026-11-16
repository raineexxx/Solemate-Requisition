using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    /// <summary>
    /// PURCHASE REQUISITIONS tab (paper 3.3). The system creates a purchase requisition only for the shortage of
    /// an approved requisition. The Head (the owner) checks the budget and approves it, or holds it with a reason.
    /// Everyone else can view. After approval the manager compares the supplier quotations and
    /// creates the purchase order on the PURCHASE ORDERS tab.
    /// </summary>
    public class PurchaseRequisitionsPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _isHead;

        private DataGridView grid, gridItems;
        private Button btnApprove, btnHold;
        private Label lblInfo;

        public PurchaseRequisitionsPanel()
        {
            _isHead = _user != null && _user.Role == Roles.Head;

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

            // ---- toolbar
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.White,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 9, 0, 0),
                Margin = new Padding(0)
            };
            btnApprove = Theme.MakeButton("APPROVE BUDGET", 170, true, OnApprove);
            btnHold = Theme.MakeButton("HOLD / REJECT", 150, false, OnHold);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadData());
            if (_isHead)
            {
                flow.Controls.Add(btnApprove);
                flow.Controls.Add(btnHold);
            }
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            root.Controls.Add(flow, 0, 0);

            // ---- purchase requisitions
            Panel card1 = Theme.MakeCard(new Padding(10));
            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.ReadOnly = true;
            Theme.AddColumn(grid, "pNo", "PURCH. REQ.", "PrNo", 14f);
            Theme.AddColumn(grid, "pReq", "REQUISITION", "RequisitionRef", 15f);
            Theme.AddColumn(grid, "pBranch", "BRANCH", "Branch", 11f);
            Theme.AddColumn(grid, "pDate", "DATE", "DateCreated", 12f, "MMM dd, yyyy");
            Theme.AddColumn(grid, "pStatus", "STATUS", "Status", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "pItems", "ITEMS", "Items", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "pPairs", "PAIRS SHORT", "Pairs", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "pBudget", "ESTIMATED BUDGET (PHP)", "EstimatedBudget", 21f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            grid.CellFormatting += OnCellFormatting;
            grid.SelectionChanged += (s, e) => OnSelectionChanged();
            card1.Controls.Add(grid);
            card1.Controls.Add(Theme.MakeSectionTitle("Purchase requisitions (the shortage of approved requisitions)"));
            root.Controls.Add(card1, 0, 1);

            // ---- items
            Panel card2 = Theme.MakeCard(new Padding(10));
            gridItems = new DataGridView();
            Theme.StyleGrid(gridItems);
            gridItems.ReadOnly = true;
            Theme.AddColumn(gridItems, "iSku", "SKU", "Sku", 16f);
            Theme.AddColumn(gridItems, "iBrand", "BRAND", "Brand", 14f);
            Theme.AddColumn(gridItems, "iProd", "PRODUCT", "Product", 28f);
            Theme.AddColumn(gridItems, "iSize", "SIZE", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iReq", "REQUESTED", "RequestedQty", 11f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iFree", "USABLE STOCK", "FreeQty", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iShort", "TO BUY", "ShortQty", 11f, null, true, DataGridViewContentAlignment.MiddleCenter);

            lblInfo = new Label { Dock = DockStyle.Bottom, Height = 28, ForeColor = Theme.Muted, Font = Theme.FontSmall, TextAlign = ContentAlignment.MiddleLeft };
            card2.Controls.Add(gridItems);
            card2.Controls.Add(lblInfo);
            card2.Controls.Add(Theme.MakeSectionTitle("Items of the selected purchase requisition"));
            root.Controls.Add(card2, 0, 2);

            Controls.Add(root);
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            PurchaseRequisitionRow r = grid.Rows[e.RowIndex].DataBoundItem as PurchaseRequisitionRow;
            if (r == null) return;

            string col = grid.Columns[e.ColumnIndex].Name;
            if (col == "pStatus")
            {
                e.CellStyle.ForeColor = r.Status == "Approved" ? Theme.Ok
                                      : r.Status == "Pending" ? Theme.Warn
                                      : r.Status == "Held" ? Theme.Red
                                      : r.Status == "Ordered" ? Theme.Ink : Theme.Muted;
                e.CellStyle.Font = Theme.FontBold;
            }
            else if (col == "pBudget" && !r.EstimatedBudget.HasValue)
            {
                e.Value = "no complete quotation";
                e.FormattingApplied = true;
                e.CellStyle.ForeColor = Theme.Muted;
            }
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private PurchaseRequisitionRow Selected
        {
            get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as PurchaseRequisitionRow; }
        }

        public void LoadData()
        {
            try
            {
                int keep = Selected != null ? Selected.PrId : 0;
                grid.DataSource = PurchasingService.GetPurchaseRequisitions();
                if (keep != 0)
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        PurchaseRequisitionRow row = r.DataBoundItem as PurchaseRequisitionRow;
                        if (row != null && row.PrId == keep) { r.Selected = true; grid.CurrentCell = r.Cells[0]; break; }
                    }
                OnSelectionChanged();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnSelectionChanged()
        {
            PurchaseRequisitionRow sel = Selected;
            bool open = sel != null && (sel.Status == "Pending" || sel.Status == "Held");
            btnApprove.Enabled = open;
            btnHold.Enabled = sel != null && sel.Status == "Pending";

            try { gridItems.DataSource = sel == null ? new List<PrItemRow>() : PurchasingService.GetPrItems(sel.PrId); }
            catch (Exception ex) { ShowError(ex); }

            if (sel == null) { lblInfo.Text = ""; return; }
            string info = "Requisition " + sel.RequisitionRef + " (" + sel.Branch + ")";
            if (sel.Status == "Pending") info += "   |   waiting for the Head to check the budget";
            else if (sel.Status == "Approved") info += "   |   budget approved by " + sel.ReviewedBy + " - the manager can now create the purchase order";
            else if (sel.Status == "Held") info += "   |   held by " + sel.ReviewedBy + ": " + sel.ReviewNote + " (the Head can approve it later)";
            else if (sel.Status == "Ordered") info += "   |   purchase order created";
            lblInfo.Text = info;
        }

        // ==================================================================
        //  Actions (Head)
        // ==================================================================
        private void OnApprove(object sender, EventArgs e)
        {
            PurchaseRequisitionRow sel = Selected;
            if (sel == null) return;
            string budget = sel.EstimatedBudget.HasValue ? "PHP " + sel.EstimatedBudget.Value.ToString("N2") + " (cheapest complete quotation)"
                                                          : "not known yet (no supplier has quoted every item)";
            if (MessageBox.Show(this, "Approve the budget of " + sel.PrNo + "?\r\n\r\n" + sel.Pairs + " pair(s) short\r\nEstimated budget: " + budget,
                    "Approve budget", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                PurchasingService.DecidePr(sel.PrId, true, "", _user);
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnHold(object sender, EventArgs e)
        {
            PurchaseRequisitionRow sel = Selected;
            if (sel == null) return;
            string reason = InputDialog.Ask(this, "Hold purchase requisition", "Reason for holding " + sel.PrNo + " (for example: over budget):");
            if (string.IsNullOrWhiteSpace(reason)) return;
            try
            {
                PurchasingService.DecidePr(sel.PrId, false, reason, _user);
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
