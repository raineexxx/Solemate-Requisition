using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Purchasing page with two tabs in the same window: PURCHASE ORDERS and SUPPLIERS.
    /// Receiving the goods into stock is the Delivery module's job (it comes after purchasing).
    /// </summary>
    public partial class PurchasingForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;

        private PurchaseRequisitionsPanel pnlPrs;
        private PurchaseOrdersPanel pnlOrders;
        private SuppliersPanel pnlSuppliers;
        private QuotationsPanel pnlQuotes;
        private Button btnTabPrs, btnTabOrders, btnTabQuotes, btnTabSuppliers;

        public PurchasingForm()
        {
            Text = "Solemate - Purchasing";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
            Shown += PurchasingForm_Shown;
        }

        private void PurchasingForm_Shown(object sender, EventArgs e)
        {
            // the Head starts on the purchase requisitions (budget approval); everyone else on the purchase orders
            ShowTab(_user.Role == Roles.Head ? 0 : 1);
        }

        // ==================================================================
        //  UI
        // ==================================================================
        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));   // tabs
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // page content

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildTabBar(), 0, 1);
            root.Controls.Add(BuildPages(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Purchasing", "Quotations, purchase orders and suppliers");

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
            btnBack.Click += OnBack;

            rightBox.Controls.Add(lblUser);
            rightBox.Controls.Add(btnBack);
            header.Controls.Add(rightBox);
            return header;
        }

        private Control BuildTabBar()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.White,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 9, 0, 0),
                Margin = new Padding(0)
            };

            btnTabPrs = Theme.MakeButton("PURCHASE REQUISITIONS", 250, true, OnTabPrs);
            btnTabOrders = Theme.MakeButton("PURCHASE ORDERS", 190, false, OnTabOrders);
            btnTabQuotes = Theme.MakeButton("QUOTATIONS", 150, false, OnTabQuotes);
            btnTabSuppliers = Theme.MakeButton("SUPPLIERS", 150, false, OnTabSuppliers);

            flow.Controls.Add(btnTabPrs);
            flow.Controls.Add(btnTabOrders);
            flow.Controls.Add(btnTabQuotes);
            flow.Controls.Add(btnTabSuppliers);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildPages()
        {
            Panel host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };

            pnlPrs = new PurchaseRequisitionsPanel();
            pnlOrders = new PurchaseOrdersPanel();
            pnlOrders.Visible = false;
            pnlQuotes = new QuotationsPanel();
            pnlQuotes.Visible = false;
            pnlSuppliers = new SuppliersPanel();
            pnlSuppliers.Visible = false;

            host.Controls.Add(pnlPrs);
            host.Controls.Add(pnlOrders);
            host.Controls.Add(pnlQuotes);
            host.Controls.Add(pnlSuppliers);
            return host;
        }

        // ==================================================================
        //  Tabs
        // ==================================================================
        // tab: 0 = purchase requisitions, 1 = purchase orders, 2 = quotations, 3 = suppliers
        private void ShowTab(int tab)
        {
            pnlPrs.Visible = tab == 0;
            pnlOrders.Visible = tab == 1;
            pnlQuotes.Visible = tab == 2;
            pnlSuppliers.Visible = tab == 3;

            Theme.StyleButton(btnTabPrs, tab == 0);
            Theme.StyleButton(btnTabOrders, tab == 1);
            Theme.StyleButton(btnTabQuotes, tab == 2);
            Theme.StyleButton(btnTabSuppliers, tab == 3);

            if (tab == 0) pnlPrs.LoadData();
            else if (tab == 1) pnlOrders.LoadData();
            else if (tab == 2) pnlQuotes.LoadData();
            else pnlSuppliers.LoadData();
        }

        private void OnTabPrs(object sender, EventArgs e)
        {
            ShowTab(0);
        }

        private void OnTabOrders(object sender, EventArgs e)
        {
            ShowTab(1);
        }

        private void OnTabQuotes(object sender, EventArgs e)
        {
            ShowTab(2);
        }

        private void OnTabSuppliers(object sender, EventArgs e)
        {
            ShowTab(3);
        }

        private void OnBack(object sender, EventArgs e)
        {
            Close();
        }
    }

    // ======================================================================
    //  Tab 1: purchase orders
    //  Head (who is also the Owner and the purchasing officer): get quotations, create and send a purchase order.
    //  Everyone else: view only.  (The Manager receives the delivery in the Delivery module.)
    // ======================================================================
    public class PurchaseOrdersPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _isManager;
        private readonly bool _canCancel;

        private DataGridView grid, gridItems;
        private Button btnNew, btnSend, btnCancelPo, btnPrint, btnEmail;
        private Label lblInfo;

        public PurchaseOrdersPanel()
        {
            _isManager = _user != null && _user.Role == Roles.Manager;
            _canCancel = _user != null && (_user.Role == Roles.Head || _user.Role == Roles.Manager);

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));   // toolbar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));    // purchase orders
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));    // items of the selected PO

            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildOrdersCard(), 0, 1);
            root.Controls.Add(BuildItemsCard(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildToolbar()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.White,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 9, 0, 0),
                Margin = new Padding(0)
            };

            btnNew = Theme.MakeButton("+ NEW PO", 120, true, OnNew);
            btnSend = Theme.MakeButton("SEND TO SUPPLIER", 180, true, OnSend);
            btnCancelPo = Theme.MakeButton("CANCEL PO", 120, false, OnCancelPo);
            btnPrint = Theme.MakeButton("PRINT PO", 110, false, OnPrint);
            btnEmail = Theme.MakeButton("EMAIL PO", 110, false, OnEmail);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, OnRefresh);

            if (_isManager) { flow.Controls.Add(btnNew); flow.Controls.Add(btnSend); }
            if (_canCancel) flow.Controls.Add(btnCancelPo);
            flow.Controls.Add(btnPrint);
            flow.Controls.Add(btnEmail);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildOrdersCard()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.ReadOnly = true;
            Theme.AddColumn(grid, "colPo", "PO NO.", "PoNo", 12);
            Theme.AddColumn(grid, "colPr", "PURCH. REQ.", "PrNo", 12);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 10, "MMM dd, yyyy");
            Theme.AddColumn(grid, "colSupplier", "SUPPLIER", "Supplier", 18);
            Theme.AddColumn(grid, "colReq", "REQUISITION", "RequisitionRef", 12);
            Theme.AddColumn(grid, "colStatus", "STATUS", "Status", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colTerms", "PAYMENT TERMS", "PaymentTerms", 12);
            Theme.AddColumn(grid, "colExp", "EXPECTED", "ExpectedDate", 10, "MMM dd, yyyy");
            Theme.AddColumn(grid, "colTotal", "TOTAL (PHP)", "Total", 10, "N2", true, DataGridViewContentAlignment.MiddleRight);

            grid.CellFormatting += OnCellFormatting;
            grid.SelectionChanged += (s, e) => OnSelectionChanged();

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Purchase orders"));
            return card;
        }

        private Control BuildItemsCard()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            gridItems = new DataGridView();
            Theme.StyleGrid(gridItems);
            gridItems.ReadOnly = true;
            Theme.AddColumn(gridItems, "iSku", "SKU", "Sku", 16);
            Theme.AddColumn(gridItems, "iBrand", "BRAND", "Brand", 14);
            Theme.AddColumn(gridItems, "iProd", "PRODUCT", "Product", 26);
            Theme.AddColumn(gridItems, "iSize", "SIZE", "Size", 8, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iQty", "QTY", "Quantity", 8, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iCost", "UNIT COST", "UnitCost", 14, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridItems, "iLine", "AMOUNT", "LineTotal", 14, "N2", true, DataGridViewContentAlignment.MiddleRight);

            lblInfo = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft
            };

            card.Controls.Add(gridItems);
            card.Controls.Add(lblInfo);
            card.Controls.Add(Theme.MakeSectionTitle("Items of the selected purchase order (only what is short)"));
            return card;
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "colStatus") return;

            PurchaseOrderRow row = grid.Rows[e.RowIndex].DataBoundItem as PurchaseOrderRow;
            if (row == null) return;

            e.Value = row.Status == "PendingApproval" ? "Pending approval" : row.Status;
            e.FormattingApplied = true;

            e.CellStyle.ForeColor = row.Status == "Received" ? Theme.Ok
                                  : row.Status == "Approved" ? Theme.Ok
                                  : row.Status == "Ordered" ? Theme.Ink
                                  : row.Status == "PendingApproval" ? Theme.Warn
                                  : row.Status == "Rejected" ? Theme.Red : Theme.Muted;
            e.CellStyle.Font = Theme.FontBold;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private PurchaseOrderRow Selected
        {
            get
            {
                if (grid.CurrentRow == null) return null;
                return grid.CurrentRow.DataBoundItem as PurchaseOrderRow;
            }
        }

        public void LoadData()
        {
            try
            {
                int keepId = Selected != null ? Selected.PurchaseOrderId : 0;
                List<PurchaseOrderRow> list = PurchasingService.GetPurchaseOrders();
                grid.DataSource = list;

                // keep the same PO selected after a refresh
                if (keepId != 0)
                {
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        PurchaseOrderRow item = r.DataBoundItem as PurchaseOrderRow;
                        if (item != null && item.PurchaseOrderId == keepId)
                        {
                            r.Selected = true;
                            grid.CurrentCell = r.Cells[0];
                            break;
                        }
                    }
                }
                OnSelectionChanged();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnSelectionChanged()
        {
            PurchaseOrderRow sel = Selected;
            bool has = sel != null;
            string st = has ? sel.Status : "";

            btnSend.Enabled = has && st == "Approved";
            btnCancelPo.Enabled = has && (st == "PendingApproval" || st == "Approved" || st == "Rejected");
            btnPrint.Enabled = has;
            btnEmail.Enabled = has;

            try
            {
                gridItems.DataSource = has ? PurchasingService.GetOrderItems(sel.PurchaseOrderId) : new List<PoItemRow>();
            }
            catch (Exception ex) { ShowError(ex); }

            if (!has) { lblInfo.Text = ""; return; }

            string info = "Created by " + sel.CreatedBy;
            if (!string.IsNullOrEmpty(sel.PrNo)) info += "   |   from purchase requisition " + sel.PrNo;
            if (st == "Approved") info += "   |   budget approved by the Head - ready to send to the supplier";
            else if (st == "Ordered") info += "   |   sent to the supplier - receive and inspect it in the DELIVERY module";
            else if (st == "Received") info += "   |   goods received through Delivery";
            lblInfo.Text = info;
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnRefresh(object sender, EventArgs e)
        {
            LoadData();
        }

        // New PO: choose an approved requisition, record the supplier quotations, create the PO from the lowest one
        private void OnNew(object sender, EventArgs e)
        {
            try
            {
               
                List<ApprovedRequisitionRow> reqs = PurchasingService.GetApprovedRequisitions();
                if (reqs.Count == 0)
                {
                    MessageBox.Show(this, "There are no approved requisitions that need purchasing.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                List<SupplierRow> suppliers = PurchasingService.GetSuppliers(true);
                if (suppliers.Count == 0)
                {
                    MessageBox.Show(this, "There are no suppliers yet. Open the SUPPLIERS tab and add one first.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (QuotationsDialog dlg = new QuotationsDialog(reqs, suppliers, _user))
                {
                    dlg.ShowDialog(this);   // the dialog creates the PO itself when CREATE PO is pressed
                }
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnSend(object sender, EventArgs e)
        {
            PurchaseOrderRow sel = Selected;
            if (sel == null) return;
            if (MessageBox.Show(this, "Send " + sel.PoNo + " to " + sel.Supplier + "?\r\n\r\n" +
                    "An expected delivery linked to this PO will be created in the DELIVERY module.",
                    "Send to supplier", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                SendResult res = PurchasingService.SendOrder(sel.PurchaseOrderId, _user);
                LoadData();

                DialogResult ask = MessageBox.Show(this,
                    sel.PoNo + " is now ORDERED.\r\nDelivery " + res.DeliveryRef + " is expected on " + res.ExpectedDate.ToString("MMM dd, yyyy") + ".\r\n\r\n" +
                    "Open an e-mail to the supplier with this order now?",
                    "Sent", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ask == DialogResult.Yes) PurchaseOrderPrinter.Email(sel.PurchaseOrderId);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCancelPo(object sender, EventArgs e)
        {
            PurchaseOrderRow sel = Selected;
            if (sel == null) return;
            if (MessageBox.Show(this, "Cancel " + sel.PoNo + "?", "Cancel purchase order",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                PurchasingService.CancelOrder(sel.PurchaseOrderId, _user);
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnPrint(object sender, EventArgs e)
        {
            PurchaseOrderRow sel = Selected;
            if (sel == null) return;
            try { PurchaseOrderPrinter.Print(sel.PurchaseOrderId); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnEmail(object sender, EventArgs e)
        {
            PurchaseOrderRow sel = Selected;
            if (sel == null) return;
            try { PurchaseOrderPrinter.Email(sel.PurchaseOrderId); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
