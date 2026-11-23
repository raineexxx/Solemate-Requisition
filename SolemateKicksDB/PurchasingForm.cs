using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Purchasing page with three tabs in the same window: PURCHASE REQUISITIONS (shortages, budget approval, supplier choice),
    /// PURCHASE ORDERS and SUPPLIERS.
    /// Receiving the goods into stock is the Delivery module's job (it comes after purchasing).
    /// </summary>
    public partial class PurchasingForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;

        private PurchaseRequisitionsPanel pnlPr;
        private PurchaseOrdersPanel pnlOrders;
        private QuotationsPanel pnlQuotes;
        private SuppliersPanel pnlSuppliers;
        private Button btnTabPr, btnTabQuotes, btnTabOrders, btnTabSuppliers;

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
            ShowTab(0);
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
            Panel header = Theme.MakeHeader("Purchasing", "Purchase requisitions, quotations, purchase orders and suppliers");

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

            btnTabPr = Theme.MakeButton("PURCHASE REQUISITIONS", 250, true, OnTabPr);
            btnTabQuotes = Theme.MakeButton("QUOTATIONS", 150, false, OnTabQuotes);
            btnTabOrders = Theme.MakeButton("PURCHASE ORDERS", 190, false, OnTabOrders);
            btnTabSuppliers = Theme.MakeButton("SUPPLIERS", 150, false, OnTabSuppliers);

            flow.Controls.Add(btnTabPr);
            flow.Controls.Add(btnTabQuotes);
            flow.Controls.Add(btnTabOrders);
            flow.Controls.Add(btnTabSuppliers);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildPages()
        {
            Panel host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };

            pnlPr = new PurchaseRequisitionsPanel();
            pnlOrders = new PurchaseOrdersPanel();
            pnlOrders.Visible = false;
            pnlQuotes = new QuotationsPanel();
            pnlQuotes.Visible = false;
            pnlSuppliers = new SuppliersPanel();
            pnlSuppliers.Visible = false;

            // after a purchase order is created, show the PURCHASE ORDERS tab
            pnlPr.OrderCreated += OnOrderCreated;

            host.Controls.Add(pnlPr);
            host.Controls.Add(pnlOrders);
            host.Controls.Add(pnlQuotes);
            host.Controls.Add(pnlSuppliers);
            return host;
        }

        // ==================================================================
        //  Tabs
        // ==================================================================
        private void ShowTab(int tab)
        {
            pnlPr.Visible = tab == 0;
            pnlQuotes.Visible = tab == 1;
            pnlOrders.Visible = tab == 2;
            pnlSuppliers.Visible = tab == 3;

            Theme.StyleButton(btnTabPr, tab == 0);
            Theme.StyleButton(btnTabQuotes, tab == 1);
            Theme.StyleButton(btnTabOrders, tab == 2);
            Theme.StyleButton(btnTabSuppliers, tab == 3);

            if (tab == 0) pnlPr.LoadData();
            else if (tab == 1) pnlQuotes.LoadData();
            else if (tab == 2) pnlOrders.LoadData();
            else pnlSuppliers.LoadData();
        }

        private void OnOrderCreated(object sender, EventArgs e)
        {
            ShowTab(2);
        }

        private void OnTabPr(object sender, EventArgs e)
        {
            ShowTab(0);
        }

        private void OnTabQuotes(object sender, EventArgs e)
        {
            ShowTab(1);
        }

        private void OnTabOrders(object sender, EventArgs e)
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
    //  Tab 1: purchase requisitions (paper 3.2 step 5 and 3.3 steps 1-4)
    //  The system creates one for the SHORTAGE of an approved requisition (when the Manager reserves the stock).
    //  Head (owner and purchasing officer): checks the budget -> approve or hold (the branch is told),
    //  then compares the suppliers' prices and creates the purchase order for the lowest one.
    // ======================================================================
    public class PurchaseRequisitionsPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _isHead;

        private DataGridView grid;

        /// <summary>Raised after a purchase order was created, so the form can switch to the PURCHASE ORDERS tab.</summary>
        public event EventHandler OrderCreated;

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // hint
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // grid

            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildHint(), 0, 1);
            root.Controls.Add(BuildContent(), 0, 2);
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

            flow.Controls.Add(Theme.MakeButton("VIEW ITEMS", 120, false, OnViewItems));
            if (_isHead)
            {
                flow.Controls.Add(Theme.MakeButton("APPROVE BUDGET", 170, true, OnApprove));
                flow.Controls.Add(Theme.MakeButton("HOLD", 90, false, OnHold));
                flow.Controls.Add(Theme.MakeButton("SUPPLIER PRICES / CREATE PO", 270, true, OnQuotes));
            }
            flow.Controls.Add(Theme.MakeButton("REFRESH", 100, false, OnRefresh));
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildHint()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 0, 0),
                ForeColor = Theme.Muted,
                BackColor = Theme.Canvas,
                Text = _isHead
                    ? "Only the shortage is purchased. Approve the budget (or hold it with a reason), then compare the suppliers and create the purchase order."
                    : "Purchase requisitions are created for the shortage of approved requisitions. The Head approves the budget."
            };
        }

        private Control BuildContent()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colPr", "PR NO.", "PrNo", 14);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 13, "g");
            Theme.AddColumn(grid, "colReq", "REQUISITION", "RequisitionRef", 14);
            Theme.AddColumn(grid, "colBranch", "BRANCH", "Branch", 12);
            Theme.AddColumn(grid, "colItems", "ITEMS", "Items", 7, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colPairs", "PAIRS", "Pairs", 7, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colBudget", "BUDGET (LOWEST QUOTE)", "EstimatedBudget", 15, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colStatus", "STATUS", "Status", 9, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colNote", "NOTE", "ReviewNote", 20);

            grid.CellFormatting += OnCellFormatting;
            grid.CellDoubleClick += OnGridDoubleClick;

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Purchase requisitions"));
            return card;
        }

        private void OnGridDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0) OnViewItems(null, EventArgs.Empty);
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "colStatus") return;

            PurchaseRequisitionRow row = grid.Rows[e.RowIndex].DataBoundItem as PurchaseRequisitionRow;
            if (row == null) return;

            e.CellStyle.ForeColor = row.Status == "Approved" || row.Status == "Ordered" ? Theme.Ok
                                  : row.Status == "Pending" ? Theme.Warn
                                  : row.Status == "Held" ? Theme.Red : Theme.Muted;
            e.CellStyle.Font = Theme.FontBold;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private PurchaseRequisitionRow Selected
        {
            get
            {
                if (grid.CurrentRow == null) return null;
                return grid.CurrentRow.DataBoundItem as PurchaseRequisitionRow;
            }
        }

        public void LoadData()
        {
            try
            {
                int keepId = Selected != null ? Selected.PrId : 0;
                grid.DataSource = PurchasingService.GetPurchaseRequisitions();

                if (keepId != 0)
                {
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        PurchaseRequisitionRow item = r.DataBoundItem as PurchaseRequisitionRow;
                        if (item != null && item.PrId == keepId)
                        {
                            r.Selected = true;
                            grid.CurrentCell = r.Cells[0];
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private PurchaseRequisitionRow NeedSelection()
        {
            PurchaseRequisitionRow pr = Selected;
            if (pr == null)
                MessageBox.Show(this, "Select a purchase requisition first.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return pr;
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnRefresh(object sender, EventArgs e)
        {
            LoadData();
        }

        private void OnViewItems(object sender, EventArgs e)
        {
            PurchaseRequisitionRow pr = NeedSelection();
            if (pr == null) return;

            try
            {
                List<PrItemRow> items = PurchasingService.GetPrItems(pr.PrId);
                using (GridDialog dlg = new GridDialog("Items to buy - " + pr.PrNo,
                    pr.PrNo + "  |  " + pr.RequisitionRef + "  |  " + pr.Branch + "  |  only the shortage is purchased",
                    items, BuildItemColumns))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private static void BuildItemColumns(DataGridView g)
        {
            Theme.AddColumn(g, "colSku", "SKU", "Sku", 14);
            Theme.AddColumn(g, "colBrand", "BRAND", "Brand", 14);
            Theme.AddColumn(g, "colProduct", "PRODUCT", "Product", 26);
            Theme.AddColumn(g, "colSize", "SIZE (US)", "Size", 9, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(g, "colReq", "REQUESTED", "RequestedQty", 11, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(g, "colRes", "FROM STOCK", "FreeQty", 11, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(g, "colShort", "TO BUY", "ShortQty", 9, null, true, DataGridViewContentAlignment.MiddleCenter);
        }

        // Approve the budget (paper 3.3 step 2)
        private void OnApprove(object sender, EventArgs e)
        {
            PurchaseRequisitionRow pr = NeedSelection();
            if (pr == null) return;
            if (pr.Status != "Pending" && pr.Status != "Held")
            {
                MessageBox.Show(this, "Only a pending or held purchase requisition can be approved.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = pr.EstimatedBudget.HasValue
                ? "Approve the budget of PHP " + pr.EstimatedBudget.Value.ToString("N2") + " (lowest complete supplier quotation) for " + pr.PrNo + "?"
                : "No supplier has a price for every item yet, so there is no budget figure.\r\nAdd the supplier prices in the SUPPLIERS tab, or approve anyway?";

            if (MessageBox.Show(this, msg, "Approve purchase", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                PurchasingService.DecidePr(pr.PrId, true, string.Empty, _user);
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // Not approved: the purchase is held and the branch is told (paper 3.3 step 3)
        private void OnHold(object sender, EventArgs e)
        {
            PurchaseRequisitionRow pr = NeedSelection();
            if (pr == null) return;
            if (pr.Status != "Pending")
            {
                MessageBox.Show(this, "Only a pending purchase requisition can be put on hold.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string reason = InputDialog.Ask(this, "Hold purchase", "Reason for holding " + pr.PrNo + " (the branch will see it):");
            if (reason == null) return;
            if (reason.Trim().Length == 0)
            {
                MessageBox.Show(this, "Enter a reason.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                PurchasingService.DecidePr(pr.PrId, false, reason.Trim(), _user);
                LoadData();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // Compare the suppliers' prices, choose one and create the purchase order (paper 3.3 steps 4-5)
        private void OnQuotes(object sender, EventArgs e)
        {
            PurchaseRequisitionRow pr = NeedSelection();
            if (pr == null) return;
            if (pr.Status != "Approved")
            {
                MessageBox.Show(this, pr.Status == "Ordered"
                    ? "A purchase order was already created for this purchase requisition."
                    : "Approve the budget first. A purchase order can only be created from an approved purchase requisition.",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                List<QuoteRow> quotes = PurchasingService.GetQuotes(pr.PrId);
                if (quotes.Count == 0)
                {
                    MessageBox.Show(this, "There are no suppliers yet. Open the SUPPLIERS tab and add them (with their prices) first.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (QuotesDialog dlg = new QuotesDialog(pr, quotes))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    PurchasingService.CreateOrderFromPr(pr.PrId, dlg.SupplierIdValue, _user);
                }

                LoadData();
                MessageBox.Show(this, "The purchase order was created. Open the PURCHASE ORDERS tab to send it to the supplier.",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (OrderCreated != null) OrderCreated(this, EventArgs.Empty);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ======================================================================
    //  Tab 2: purchase orders
    //  Head (who is also the Owner and the purchasing officer): send the written PO to the supplier, or cancel it before it is sent.
    //  Everyone else: view only.  (The Manager receives the delivery in the Delivery module.)
    // ======================================================================
    public class PurchaseOrdersPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _isHead;

        private DataGridView grid;

        public PurchaseOrdersPanel()
        {
            _isHead = _user != null && _user.Role == Roles.Head;

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
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));   // toolbar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // grid

            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
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

            flow.Controls.Add(Theme.MakeButton("VIEW ITEMS", 120, false, OnViewItems));
            if (_isHead)
            {
                flow.Controls.Add(Theme.MakeButton("SEND PO TO SUPPLIER", 210, true, OnSend));
                flow.Controls.Add(Theme.MakeButton("CANCEL PO", 120, false, OnCancel));
            }
            flow.Controls.Add(Theme.MakeButton("REFRESH", 100, false, OnRefresh));
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildContent()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colPo", "PO NO.", "PoNo", 13);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 12, "g");
            Theme.AddColumn(grid, "colSupplier", "SUPPLIER", "Supplier", 18);
            Theme.AddColumn(grid, "colPr", "PURCHASE REQ.", "PrNo", 13);
            Theme.AddColumn(grid, "colReq", "REQUISITION", "RequisitionRef", 13);
            Theme.AddColumn(grid, "colTerms", "PAYMENT TERMS", "PaymentTerms", 13);
            Theme.AddColumn(grid, "colSent", "SENT TO SUPPLIER", "DateSent", 13, "g", true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colExp", "EXPECTED", "ExpectedDate", 10, "d", true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colStatus", "STATUS", "Status", 9, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colTotal", "TOTAL (PHP)", "Total", 12, "N2", true, DataGridViewContentAlignment.MiddleRight);

            grid.CellFormatting += OnCellFormatting;
            grid.CellDoubleClick += OnGridDoubleClick;

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Purchase orders"));
            return card;
        }

        private void OnGridDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0) OnViewItems(null, EventArgs.Empty);
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "colStatus") return;

            PurchaseOrderRow row = grid.Rows[e.RowIndex].DataBoundItem as PurchaseOrderRow;
            if (row == null) return;

            e.CellStyle.ForeColor = row.Status == "Received" ? Theme.Ok
                                  : row.Status == "Ordered" ? Theme.Warn : Theme.Muted;
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
                grid.DataSource = PurchasingService.GetPurchaseOrders();

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
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private PurchaseOrderRow NeedSelection()
        {
            PurchaseOrderRow po = Selected;
            if (po == null)
                MessageBox.Show(this, "Select a purchase order first.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return po;
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnRefresh(object sender, EventArgs e)
        {
            LoadData();
        }

        private void OnViewItems(object sender, EventArgs e)
        {
            PurchaseOrderRow po = NeedSelection();
            if (po == null) return;

            try
            {
                List<PoItemRow> items = PurchasingService.GetOrderItems(po.PurchaseOrderId);
                using (GridDialog dlg = new GridDialog("Purchase order " + po.PoNo,
                    po.PoNo + "  |  " + po.Supplier + "  |  PHP " + po.Total.ToString("N2"),
                    items, BuildPoItemColumns))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private static void BuildPoItemColumns(DataGridView g)
        {
            Theme.AddColumn(g, "colSku", "SKU", "Sku", 14);
            Theme.AddColumn(g, "colBrand", "BRAND", "Brand", 14);
            Theme.AddColumn(g, "colProduct", "PRODUCT", "Product", 24);
            Theme.AddColumn(g, "colSize", "SIZE (US)", "Size", 9, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(g, "colQty", "QTY", "Quantity", 8, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(g, "colCost", "UNIT COST", "UnitCost", 12, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(g, "colTotal", "AMOUNT", "LineTotal", 12, "N2", true, DataGridViewContentAlignment.MiddleRight);
        }

        // Send PO: make the written purchase order, open an email to the supplier, then record that it was sent
        private void OnSend(object sender, EventArgs e)
        {
            PurchaseOrderRow po = NeedSelection();
            if (po == null) return;
            if (po.Status != "Ordered" || po.DateSent.HasValue)
            {
                MessageBox.Show(this, po.DateSent.HasValue
                    ? "This purchase order was already sent to the supplier."
                    : "Only a purchase order with status Ordered can be sent.",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                PoPrintHeader header = PurchasingService.GetPrintHeader(po.PurchaseOrderId);
                List<PoItemRow> items = PurchasingService.GetOrderItems(po.PurchaseOrderId);

                string path = PoPrinter.Save(header, items, _user.FullName);
                PoPrinter.Open(path);

                bool hasEmail = !string.IsNullOrEmpty(header.SupplierEmail);
                if (hasEmail) PoPrinter.OpenEmail(header, _user.FullName);

                string msg = "The written purchase order was saved here:\r\n" + path + "\r\n\r\n"
                    + (hasEmail
                        ? "A new email to " + header.SupplierEmail + " was opened. Print the order to PDF from the browser and attach it to the email, then send it.\r\n\r\n"
                        : "This supplier has no email address. Print the order and send it another way.\r\n\r\n")
                    + "Was the purchase order sent to the supplier?";

                if (MessageBox.Show(this, msg, "Send purchase order", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                SendResult r = PurchasingService.SendOrder(po.PurchaseOrderId, _user);
                LoadData();

                MessageBox.Show(this, "The purchase order was recorded as sent.\r\nExpected delivery " + r.DeliveryRef
                    + (r.ExpectedDate.HasValue ? " on " + r.ExpectedDate.Value.ToString("MMMM d, yyyy") : "")
                    + ". The Manager will receive it in the Delivery module.",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCancel(object sender, EventArgs e)
        {
            PurchaseOrderRow po = NeedSelection();
            if (po == null) return;
            if (po.Status != "Ordered" || po.DateSent.HasValue)
            {
                MessageBox.Show(this, "Only a purchase order that was not sent yet can be cancelled.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, "Cancel purchase order " + po.PoNo + "?\r\nThe purchase requisition goes back to Approved so you can choose another supplier.",
                    "Cancel purchase order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                PurchasingService.CancelOrder(po.PurchaseOrderId, _user);
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
