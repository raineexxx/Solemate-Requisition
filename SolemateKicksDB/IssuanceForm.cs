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
    /// Issuance screen (system analysis 3.2).
    /// Tab 1 - approved requisitions: the Manager reserves stock, then issues it (slip + stock deduction).
    /// Tab 2 - issuance slips: branch staff sign the acknowledgment of receipt.
    /// </summary>
    public class IssuanceForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _isManager;

        private TabControl tabs;
        private DataGridView gridReq, gridReqItems, gridSlips, gridSlipItems;
        private Button btnReserve, btnIssue, btnAck;

        public IssuanceForm()
        {
            _isManager = _user != null && _user.Role == Roles.Manager;

            Text = "Solemate - Issuance";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
            Shown += (s, e) => LoadAll();
        }

        // ==================================================================
        //  UI
        // ==================================================================
        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(BuildHeader(), 0, 0);

            tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.FontBold, Padding = new Point(20, 6) };
            TabPage t1 = new TabPage("Approved requisitions - to issue") { BackColor = Theme.Canvas };
            TabPage t2 = new TabPage("Issuance slips") { BackColor = Theme.Canvas };
            t1.Controls.Add(BuildToIssueTab());
            t2.Controls.Add(BuildSlipsTab());
            tabs.TabPages.Add(t1);
            tabs.TabPages.Add(t2);
            root.Controls.Add(tabs, 0, 1);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Issuance", "Reserve, issue and acknowledge stock for branches");

            Panel rightBox = new Panel { Dock = DockStyle.Right, Width = 430, BackColor = Theme.Red };
            Label lblUser = new Label
            {
                Text = string.Format("{0}\r\n{1}  |  {2}", _user.FullName, _user.Role, _user.Branch),
                ForeColor = Theme.White,
                BackColor = Theme.Red,
                Font = Theme.FontBold,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(0, 12, 310, 52)
            };
            Button btnBack = new Button { Text = "BACK", Bounds = new Rectangle(322, 22, 96, 32) };
            Theme.StyleButtonOnRed(btnBack);
            btnBack.Click += (s, e) => Close();

            rightBox.Controls.Add(lblUser);
            rightBox.Controls.Add(btnBack);
            header.Controls.Add(rightBox);
            return header;
        }

        private static DataGridView NewGrid()
        {
            DataGridView g = new DataGridView();
            Theme.StyleGrid(g);
            g.ReadOnly = true;
            return g;
        }

        private Control BuildToIssueTab()
        {
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

            Label l1 = Theme.MakeSectionTitle("Approved requisitions waiting to be issued");
            l1.Dock = DockStyle.Fill;
            t.Controls.Add(l1, 0, 0);

            gridReq = NewGrid();
            Theme.AddColumn(gridReq, "rRef", "REQUISITION", "RefNo", 18f);
            Theme.AddColumn(gridReq, "rBranch", "BRANCH", "Branch", 16f);
            Theme.AddColumn(gridReq, "rDate", "DATE", "DateCreated", 18f, "yyyy-MM-dd HH:mm");
            Theme.AddColumn(gridReq, "rBy", "REQUESTED BY", "RequestedBy", 18f);
            Theme.AddColumn(gridReq, "rReq", "REQUESTED", "Requested", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "rRes", "RESERVED", "Reserved", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "rIss", "ISSUED", "Issued", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            gridReq.SelectionChanged += (s, e) => LoadReqItems();
            t.Controls.Add(gridReq, 0, 1);

            Label l2 = Theme.MakeSectionTitle("Items of the selected requisition");
            l2.Dock = DockStyle.Fill;
            t.Controls.Add(l2, 0, 2);

            gridReqItems = NewGrid();
            Theme.AddColumn(gridReqItems, "iSku", "SKU", "Sku", 14f);
            Theme.AddColumn(gridReqItems, "iBrand", "BRAND", "Brand", 12f);
            Theme.AddColumn(gridReqItems, "iProd", "PRODUCT", "Product", 22f);
            Theme.AddColumn(gridReqItems, "iSize", "SIZE", "Size", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReqItems, "iReq", "REQUESTED", "Requested", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReqItems, "iIss", "ISSUED", "Issued", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReqItems, "iRes", "RESERVED", "Reserved", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReqItems, "iOut", "STILL SHORT", "Outstanding", 11f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReqItems, "iAvail", "AVAILABLE NOW", "Available", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            gridReqItems.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridReqItems.Columns[e.ColumnIndex].Name != "iOut") return;
                int v;
                if (int.TryParse(Convert.ToString(e.Value), out v) && v > 0)
                {
                    e.CellStyle.ForeColor = Theme.Red;
                    e.CellStyle.Font = Theme.FontBold;
                }
            };
            t.Controls.Add(gridReqItems, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnReserve = Theme.MakeButton("1. RESERVE STOCK", 170, false, OnReserve);
            btnIssue = Theme.MakeButton("2. ISSUE RESERVED STOCK", 220, true, OnIssue);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadAll());
            btnReserve.Enabled = btnIssue.Enabled = _isManager;
            buttons.Controls.Add(btnReserve);
            buttons.Controls.Add(btnIssue);
            buttons.Controls.Add(btnRefresh);
            if (!_isManager)
                buttons.Controls.Add(new Label { Text = "Only the manager can reserve and issue stock.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 14, 0, 0) });
            t.Controls.Add(buttons, 0, 4);

            return t;
        }

        private Control BuildSlipsTab()
        {
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

            Label l1 = Theme.MakeSectionTitle("Stock issuance slips");
            l1.Dock = DockStyle.Fill;
            t.Controls.Add(l1, 0, 0);

            gridSlips = NewGrid();
            Theme.AddColumn(gridSlips, "sNo", "SLIP NO", "IssueNo", 15f);
            Theme.AddColumn(gridSlips, "sReq", "REQUISITION", "RequisitionRef", 16f);
            Theme.AddColumn(gridSlips, "sBranch", "BRANCH", "Branch", 12f);
            Theme.AddColumn(gridSlips, "sDate", "DATE ISSUED", "DateIssued", 16f, "yyyy-MM-dd HH:mm");
            Theme.AddColumn(gridSlips, "sBy", "ISSUED BY", "IssuedBy", 14f);
            Theme.AddColumn(gridSlips, "sQty", "PAIRS", "TotalQty", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridSlips, "sStatus", "STATUS", "Status", 12f);
            Theme.AddColumn(gridSlips, "sRecv", "RECEIVED BY", "ReceivedBy", 14f);
            gridSlips.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridSlips.Columns[e.ColumnIndex].Name != "sStatus") return;
                e.CellStyle.ForeColor = Convert.ToString(e.Value) == "Acknowledged" ? Theme.Ok : Theme.Warn;
                e.CellStyle.Font = Theme.FontBold;
            };
            gridSlips.SelectionChanged += (s, e) => LoadSlipItems();
            t.Controls.Add(gridSlips, 0, 1);

            Label l2 = Theme.MakeSectionTitle("Items on the selected slip");
            l2.Dock = DockStyle.Fill;
            t.Controls.Add(l2, 0, 2);

            gridSlipItems = NewGrid();
            Theme.AddColumn(gridSlipItems, "tSku", "SKU", "Sku", 16f);
            Theme.AddColumn(gridSlipItems, "tBrand", "BRAND", "Brand", 16f);
            Theme.AddColumn(gridSlipItems, "tProd", "PRODUCT", "Product", 30f);
            Theme.AddColumn(gridSlipItems, "tSize", "SIZE", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridSlipItems, "tQty", "QTY", "Quantity", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            t.Controls.Add(gridSlipItems, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            bool canAck = _user != null && (_user.Role == Roles.Cashier || _user.Role == Roles.Manager);
            btnAck = Theme.MakeButton("ACKNOWLEDGE RECEIPT", 220, true, OnAcknowledge);
            btnAck.Enabled = canAck;
            buttons.Controls.Add(btnAck);
            t.Controls.Add(buttons, 0, 4);

            return t;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private void LoadAll()
        {
            try
            {
                int reqId = SelectedReq() == null ? 0 : SelectedReq().RequisitionId;
                gridReq.DataSource = IssuanceService.GetRequisitions();
                if (reqId != 0)
                    foreach (DataGridViewRow r in gridReq.Rows)
                    {
                        IssueRequisitionRow row = r.DataBoundItem as IssueRequisitionRow;
                        if (row != null && row.RequisitionId == reqId) { r.Selected = true; gridReq.CurrentCell = r.Cells[0]; break; }
                    }
                LoadReqItems();

                gridSlips.DataSource = IssuanceService.GetSlips(_user);
                LoadSlipItems();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private IssueRequisitionRow SelectedReq()
        {
            return gridReq == null || gridReq.CurrentRow == null ? null : gridReq.CurrentRow.DataBoundItem as IssueRequisitionRow;
        }

        private SlipRow SelectedSlip()
        {
            return gridSlips == null || gridSlips.CurrentRow == null ? null : gridSlips.CurrentRow.DataBoundItem as SlipRow;
        }

        private void LoadReqItems()
        {
            IssueRequisitionRow r = SelectedReq();
            try { gridReqItems.DataSource = r == null ? new List<IssueItemRow>() : IssuanceService.GetItems(r.RequisitionId); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void LoadSlipItems()
        {
            SlipRow s = SelectedSlip();
            try { gridSlipItems.DataSource = s == null ? new List<SlipItemRow>() : IssuanceService.GetSlipItems(s.IssuanceId); }
            catch (Exception ex) { ShowError(ex); }
        }

        // ==================================================================
        //  Actions
        // ==================================================================
        private void OnReserve(object sender, EventArgs e)
        {
            IssueRequisitionRow r = SelectedReq();
            if (r == null) { MessageBox.Show(this, "Select a requisition first.", "Solemate"); return; }
            try
            {
                ReserveResult res = IssuanceService.Reserve(r.RequisitionId, _user);
                string msg = res.Reserved + " pair(s) newly reserved for " + r.RefNo + ".";
                if (res.ShortLines > 0)
                    msg += "\r\n\r\n" + res.ShortLines + " item(s) are still short. Order them through Purchasing / Delivery, then reserve again after the goods are received.";
                MessageBox.Show(this, msg, "Reserve stock", MessageBoxButtons.OK,
                    res.ShortLines > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                LoadAll();
            }
            catch (Exception ex) { ShowError(ex); LoadAll(); }
        }

        private void OnIssue(object sender, EventArgs e)
        {
            IssueRequisitionRow r = SelectedReq();
            if (r == null) { MessageBox.Show(this, "Select a requisition first.", "Solemate"); return; }
            if (r.Reserved <= 0)
            {
                MessageBox.Show(this, "Nothing is reserved for " + r.RefNo + " yet. Use RESERVE STOCK first.", "Solemate",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Issue " + r.Reserved + " reserved pair(s) to " + r.Branch + "?\r\n\r\nA slip is created and the stock is deducted now.",
                    "Issue stock", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                IssueResult res = IssuanceService.Issue(r.RequisitionId, _user);
                string msg = "Issuance slip " + res.IssueNo + " created. Stock deducted.";
                msg += res.Closed ? "\r\nRequisition " + r.RefNo + " is fully issued and now CLOSED."
                                  : "\r\nRequisition " + r.RefNo + " is only partly issued and stays open for the remaining items.";
                MessageBox.Show(this, msg, "Issued", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadAll();
                tabs.SelectedIndex = 1;
            }
            catch (Exception ex) { ShowError(ex); LoadAll(); }
        }

        private void OnAcknowledge(object sender, EventArgs e)
        {
            SlipRow s = SelectedSlip();
            if (s == null) { MessageBox.Show(this, "Select an issuance slip first.", "Solemate"); return; }
            if (s.Status == "Acknowledged")
            {
                MessageBox.Show(this, s.IssueNo + " was already acknowledged by " + s.ReceivedBy + ".", "Solemate");
                return;
            }
            string name = InputDialog.Ask(this, "Acknowledge receipt", "Name of the person who received the items for " + s.Branch + ":");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                IssuanceService.Acknowledge(s.IssuanceId, name, _user);
                MessageBox.Show(this, s.IssueNo + " acknowledged by " + name.Trim() + ".", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadAll();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
