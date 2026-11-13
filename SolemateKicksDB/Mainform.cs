using SolemateKicksDB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Requisition list. Cashier/Manager: create, revise, cancel.  Head: view details, approve or reject.
    /// The bottom card can show the requested items of the selected requisition, or every item in stock,
    /// filtered by brand (Nike, Adidas, ...) and by low stock.
    /// </summary>
    public class MainForm : Form
    {
        private const string AllBrands = "All brands";
        private const string ViewRequested = "Requested items";
        private const string ViewAll = "All items in stock";
        private const string AllLevels = "All stock levels";
        private const string LowOnly = "Low stock only";

        public bool LogoutRequested { get; private set; }

        private readonly AppUser _user = Session.CurrentUser;
        private List<RequisitionRow> _rows = new List<RequisitionRow>();
        private List<RequisitionLineRow> _selLines = new List<RequisitionLineRow>();   // items of the selected requisition (unfiltered)
        private List<CatalogRow> _stock = new List<CatalogRow>();                      // every product with its stock
        private bool _loading;
        private bool _filling;   // true while the brand dropdown is being rebuilt, so it does not trigger a re-filter
        private readonly Dictionary<int, string> _itemsText = new Dictionary<int, string>();   // every item of the requisition, one per line

        private TableLayoutPanel root;
        private DataGridView gridReq;
        private DataGridView gridLines;   // requested items of the selected requisition
        private DataGridView gridStock;   // every item in stock
        private ComboBox cmbStatus;
        private ComboBox cmbBrand;
        private ComboBox cmbView;
        private ComboBox cmbStock;
        private Label lblCount;
        private Label lblBanner;
        private Label lblDetail;
        private Button btnNew, btnRevise, btnApprove, btnReject, btnCancel, btnInventory, btnDelivery, btnPurchasing, btnSales, btnIssuance, btnRefresh, btnDetails;
        public MainForm()
        {
            Text = "Solemate - Requisition";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1100, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();

            cmbStatus.SelectedItem = (_user.Role == Roles.Head) ? Statuses.Pending : "All";
            cmbStatus.SelectedIndexChanged += (s, e) => LoadData();
            cmbBrand.SelectedIndexChanged += (s, e) => { if (!_filling) ApplyLineFilter(); };
            cmbView.SelectedIndexChanged += (s, e) => { if (!_filling) ApplyLineFilter(); };
            cmbStock.SelectedIndexChanged += (s, e) => { if (!_filling) ApplyLineFilter(); };
            Shown += (s, e) => LoadData();
        }

        // ==================================================================
        //  UI
        // ==================================================================
        private void BuildUi()
        {
            root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));  // toolbar (two rows: actions + modules)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));    // banner (0 = hidden)
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // content

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildToolbar(), 0, 1);
            root.Controls.Add(BuildBanner(), 0, 2);
            root.Controls.Add(BuildContent(), 0, 3);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Requisition", "Item requests and approvals");

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

            Button btnLogout = new Button { Text = "LOG OUT", Bounds = new Rectangle(322, 22, 96, 32) };
            Theme.StyleButtonOnRed(btnLogout);
            btnLogout.Click += (s, e) => { LogoutRequested = true; Close(); };

            rightBox.Controls.Add(lblUser);
            rightBox.Controls.Add(btnLogout);
            header.Controls.Add(rightBox);
            return header;
        }

        private Control BuildToolbar()
        {
            Panel toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.White, Margin = new Padding(0) };

            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,   // row 1 = requisition actions, row 2 = module buttons (forced by SetFlowBreak below)
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 11, 0, 0)
            };

            btnNew = Theme.MakeButton("+ NEW REQUISITION", 170, true, OnNew);
            btnRevise = Theme.MakeButton("REVISE", 90, false, OnRevise);
            btnDetails = Theme.MakeButton("DETAILS", 100, false, OnDetails);
            btnApprove = Theme.MakeButton("APPROVE", 110, true, OnApprove);
            btnReject = Theme.MakeButton("REJECT", 100, false, OnReject);
            btnCancel = Theme.MakeButton("CANCEL REQUEST", 140, false, OnCancelRequest);
            btnInventory = Theme.MakeButton("INVENTORY", 110, false, OnInventory);
            btnPurchasing = Theme.MakeButton("PURCHASING", 120, false, OnPurchasing);
            btnDelivery = Theme.MakeButton("DELIVERY", 100, false, OnDelivery);
            btnSales = Theme.MakeButton("SALES", 90, false, OnSales);
            btnIssuance = Theme.MakeButton("ISSUANCE", 100, false, OnIssuance);
            btnRefresh = Theme.MakeButton("REFRESH", 90, false, (s, e) => LoadData());

            bool isHead = _user.Role == Roles.Head;
            if (!isHead)
            {
                flow.Controls.Add(btnNew);
                flow.Controls.Add(btnRevise);
                flow.Controls.Add(btnCancel);
            }
            else
            {
                flow.Controls.Add(btnDetails);   // Head: open the full details of the selected requisition
                flow.Controls.Add(btnApprove);
                flow.Controls.Add(btnReject);
            }
            // end of row 1: the last action button starts a new row for the module buttons
            flow.SetFlowBreak(isHead ? btnReject : btnCancel, true);

            flow.Controls.Add(btnInventory);   // link to the Inventory module (everyone can view, only the Manager can edit)
            flow.Controls.Add(btnPurchasing);  // link to the Purchasing module (everyone can view, only the Manager can edit)
            flow.Controls.Add(btnSales);       // Sales: stock is deducted when a sale is completed
            flow.Controls.Add(btnIssuance);    // Issuance: reserve, issue and acknowledge stock for approved requisitions
            flow.Controls.Add(btnDelivery);    // link to the Delivery module (everyone can view, only the Manager can edit)
            flow.Controls.Add(btnRefresh);

            foreach (Control c in flow.Controls) c.Margin = new Padding(3, 0, 3, 8);

            // status filter on the right
            Panel rightBox = new Panel { Dock = DockStyle.Right, Width = 240 };
            Label lbl = new Label { Text = "Show:", Font = Theme.FontBold, AutoSize = true, Location = new Point(10, 22) };
            cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(62, 18),
                Width = 160
            };
            cmbStatus.Items.AddRange(Statuses.Filter);
            rightBox.Controls.Add(lbl);
            rightBox.Controls.Add(cmbStatus);

            toolbar.Controls.Add(flow);       // fill
            toolbar.Controls.Add(rightBox);   // right edge
            return toolbar;
        }

        private Control BuildBanner()
        {
            lblBanner = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                BackColor = Theme.RedSoft,
                ForeColor = Theme.RedDark,
                Font = Theme.FontBold,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Margin = new Padding(0)
            };
            return lblBanner;
        }

        private Control BuildContent()
        {
            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8),
                Margin = new Padding(0)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 46f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 54f));

            // ---- top card: requisitions ----
            Panel cardTop = Theme.MakeCard(new Padding(14, 8, 14, 12));

            gridReq = new DataGridView();
            Theme.StyleGrid(gridReq);
            gridReq.ReadOnly = true;
            Theme.AddColumn(gridReq, "RefNo", "Ref No.", "RefNo", 15f);
            Theme.AddColumn(gridReq, "Date", "Date", "DateCreated", 14f, "MMM dd, yyyy h:mm tt");
            Theme.AddColumn(gridReq, "RequestedBy", "Requested by", "RequestedBy", 12f);
            Theme.AddColumn(gridReq, "Branch", "Branch", "Branch", 9f);
            Theme.AddColumn(gridReq, "Items", "Items ordered", null, 32f);
            Theme.AddColumn(gridReq, "Lines", "Lines", "Lines", 6f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "TotalQty", "Pairs", "TotalQty", 6f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "Status", "Status", "Status", 9f);
            Theme.AddColumn(gridReq, "ReviewedBy", "Reviewed by", "ReviewedBy", 11f);
            gridReq.Columns["RefNo"].MinimumWidth = 150;
            gridReq.Columns["Date"].MinimumWidth = 160;
            gridReq.Columns["RequestedBy"].MinimumWidth = 120;
            gridReq.Columns["Branch"].MinimumWidth = 85;
            gridReq.Columns["Items"].MinimumWidth = 280;
            gridReq.Columns["Lines"].MinimumWidth = 60;
            gridReq.Columns["TotalQty"].MinimumWidth = 60;
            gridReq.Columns["Status"].MinimumWidth = 85;
            gridReq.Columns["ReviewedBy"].MinimumWidth = 110;

            // keep the column titles on one line (the wrap setting below is only for the item rows)
            gridReq.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            gridReq.ShowCellToolTips = true;

            // show every item of the requisition (one per line) instead of cutting it with "+ more"
            gridReq.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            gridReq.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            gridReq.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridReq.Columns[e.ColumnIndex].Name != "Status") return;
                e.CellStyle.Font = Theme.FontBold;
                e.CellStyle.ForeColor = Theme.StatusColor(Convert.ToString(e.Value));
            };
            gridReq.SelectionChanged += (s, e) => { if (!_loading) OnSelectionChanged(); };
            gridReq.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenReview(); };
            gridReq.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridReq.Columns[e.ColumnIndex].Name != "Items") return;
                RequisitionRow row = gridReq.Rows[e.RowIndex].DataBoundItem as RequisitionRow;
                string text;
                if (row != null && _itemsText.TryGetValue(row.RequisitionId, out text))
                {
                    e.Value = text;
                    e.FormattingApplied = true;
                }
            };

            cardTop.Controls.Add(gridReq);                                // fill first
            cardTop.Controls.Add(Theme.MakeSectionTitle("Requisitions")); // then top edge

            // ---- bottom card: items (requested items of the selected requisition, or everything in stock) ----
            Panel cardBottom = Theme.MakeCard(new Padding(14, 8, 14, 12));

            // filter bar: brand + what to show + low stock
            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent };

            Label lblBrand = new Label { Text = "Brand:", Font = Theme.FontBold, AutoSize = true, Location = new Point(0, 11) };
            cmbBrand = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(58, 6),
                Width = 150
            };
            cmbBrand.Items.Add(AllBrands);   // the real brands are loaded from the database in LoadData
            cmbBrand.SelectedIndex = 0;

            Label lblView = new Label { Text = "Show:", Font = Theme.FontBold, AutoSize = true, Location = new Point(228, 11) };
            cmbView = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(280, 6),
                Width = 170
            };
            cmbView.Items.AddRange(new object[] { ViewRequested, ViewAll });
            cmbView.SelectedIndex = 0;

            Label lblStock = new Label { Text = "Stock:", Font = Theme.FontBold, AutoSize = true, Location = new Point(470, 11) };
            cmbStock = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(522, 6),
                Width = 150
            };
            cmbStock.Items.AddRange(new object[] { AllLevels, LowOnly });
            cmbStock.SelectedIndex = 0;

            lblCount = new Label { Font = Theme.FontSmall, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(694, 12) };

            filterBar.Controls.Add(lblBrand);
            filterBar.Controls.Add(cmbBrand);
            filterBar.Controls.Add(lblView);
            filterBar.Controls.Add(cmbView);
            filterBar.Controls.Add(lblStock);
            filterBar.Controls.Add(cmbStock);
            filterBar.Controls.Add(lblCount);

            // grid 1: the requested items of the selected requisition
            gridLines = new DataGridView();
            Theme.StyleGrid(gridLines);
            gridLines.ReadOnly = true;
            Theme.AddColumn(gridLines, "Sku", "SKU", "Sku", 14f);
            Theme.AddColumn(gridLines, "Brand", "Brand", "Brand", 16f);
            Theme.AddColumn(gridLines, "Product", "Model", "Product", 28f);
            Theme.AddColumn(gridLines, "Size", "Size (US)", "Size", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Quantity", "Qty requested", "Quantity", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Available", "In stock", "Available", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Check", "Stock check", "Check", 14f);

            gridLines.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridLines.Columns[e.ColumnIndex].Name != "Check") return;
                e.CellStyle.Font = Theme.FontBold;
                bool ok = Convert.ToString(e.Value) == "Sufficient";
                e.CellStyle.ForeColor = ok ? Theme.Ok : Theme.Red;
            };

            // grid 2: every item in stock (shown when "All items in stock" is chosen)
            gridStock = new DataGridView();
            Theme.StyleGrid(gridStock);
            gridStock.ReadOnly = true;
            gridStock.Visible = false;
            Theme.AddColumn(gridStock, "Sku", "SKU", "Sku", 14f);
            Theme.AddColumn(gridStock, "Brand", "Brand", "Brand", 14f);
            Theme.AddColumn(gridStock, "Product", "Model", "Product", 26f);
            Theme.AddColumn(gridStock, "Size", "Size (US)", "Size", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridStock, "Stock", "In stock", "Stock", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridStock, "Reorder", "Reorder at", "ReorderLevel", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridStock, "Status", "Status", "StockStatus", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridStock, "Requested", "In this requisition", null, 14f, null, true, DataGridViewContentAlignment.MiddleCenter);

            gridStock.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                CatalogRow row = gridStock.Rows[e.RowIndex].DataBoundItem as CatalogRow;
                if (row == null) return;

                string col = gridStock.Columns[e.ColumnIndex].Name;
                if (col == "Status" || col == "Stock")
                {
                    e.CellStyle.ForeColor = row.StockStatus == "OUT" ? Theme.Red : (row.StockStatus == "LOW" ? Theme.Warn : Theme.Ok);
                    e.CellStyle.Font = Theme.FontBold;
                }
                else if (col == "Requested")
                {
                    // how many pairs of this item the selected requisition asks for
                    int qty = _selLines
                        .Where(l => string.Equals(l.Sku, row.Sku, StringComparison.OrdinalIgnoreCase))
                        .Sum(l => l.Quantity);
                    e.Value = qty > 0 ? "x" + qty : "-";
                    e.FormattingApplied = true;
                    if (qty > 0) e.CellStyle.Font = Theme.FontBold;
                }
            };

            lblDetail = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                AutoSize = false,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft
            };

            cardBottom.Controls.Add(gridLines);
            cardBottom.Controls.Add(gridStock);
            cardBottom.Controls.Add(lblDetail);
            cardBottom.Controls.Add(filterBar);
            cardBottom.Controls.Add(Theme.MakeSectionTitle("Items in selected requisition"));

            content.Controls.Add(cardTop, 0, 0);
            content.Controls.Add(cardBottom, 0, 1);
            return content;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private void LoadData()
        {
            _loading = true;
            try
            {
                string status = Convert.ToString(cmbStatus.SelectedItem);
                _rows = RequisitionService.GetRequisitions(_user, status);

                _itemsText.Clear();
                foreach (RequisitionRow r in _rows)
                {
                    List<RequisitionLineRow> items = RequisitionService.GetLines(r.RequisitionId);
                    _itemsText[r.RequisitionId] = DescribeLines(items);
                }

                // every product with its stock, for the "All items in stock" view and the brand list
                _stock = InventoryService.GetStock();
                RebuildBrandList();

                gridReq.DataSource = null;
                gridReq.DataSource = _rows;
                gridReq.ClearSelection();

                UpdateBanner();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _loading = false;
            }

            if (gridReq.Rows.Count > 0)
            {
                gridReq.Rows[0].Selected = true;
                gridReq.CurrentCell = gridReq.Rows[0].Cells[0];
            }
            OnSelectionChanged();
        }

        private void UpdateBanner()
        {
            string msg = null;

            if (_user.Role == Roles.Head)
            {
                int pending = RequisitionService.GetPendingCount();
                if (pending > 0)
                    msg = string.Format("   {0} requisition(s) waiting for your approval.", pending);
            }
            else
            {
                int rejected = RequisitionService.GetRejectedCount(_user.UserId);
                if (rejected > 0)
                    msg = string.Format("   {0} of your requisition(s) were rejected. Select one, then Revise or Cancel it.", rejected);
            }

            lblBanner.Text = msg ?? string.Empty;
            root.RowStyles[2].Height = (msg == null) ? 0f : 36f;
        }

        private RequisitionRow Selected()
        {
            if (gridReq.CurrentRow == null) return null;
            return gridReq.CurrentRow.DataBoundItem as RequisitionRow;
        }

        private void OnSelectionChanged()
        {
            RequisitionRow sel = Selected();

            if (sel == null)
            {
                _selLines = new List<RequisitionLineRow>();
                ApplyLineFilter();
                lblDetail.Text = string.Empty;
                SetButtons(null);
                return;
            }

            try
            {
                _selLines = RequisitionService.GetLines(sel.RequisitionId);
                ApplyLineFilter();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(sel.Remarks)) parts.Add("Remarks: " + sel.Remarks);
            if (sel.Status == Statuses.Rejected && !string.IsNullOrWhiteSpace(sel.RejectReason)) parts.Add("Rejected: " + sel.RejectReason);
            if (!string.IsNullOrWhiteSpace(sel.StockCheck)) parts.Add("Stock check: " + sel.StockCheck);
            lblDetail.Text = string.Join("     |     ", parts);

            SetButtons(sel);
        }

        /// <summary>"All brands" first, then every brand from the database. Keeps the current choice if it still exists.</summary>
        private void RebuildBrandList()
        {
            _filling = true;
            try
            {
                string current = Convert.ToString(cmbBrand.SelectedItem);

                List<string> names = new List<string>();
                try
                {
                    foreach (var b in RequisitionService.GetBrands()) names.Add(b.BrandName);
                }
                catch
                {
                    // if the brand list cannot be read, fall back to the brands found in the stock list below
                }
                names.AddRange(_stock.Select(r => r.Brand));

                List<string> list = names
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                cmbBrand.Items.Clear();
                cmbBrand.Items.Add(AllBrands);
                foreach (string n in list) cmbBrand.Items.Add(n);

                int idx = cmbBrand.Items.IndexOf(current);
                cmbBrand.SelectedIndex = idx >= 0 ? idx : 0;
            }
            finally
            {
                _filling = false;
            }
        }

        /// <summary>
        /// Fills the bottom grid. "Requested items" = the items of the selected requisition;
        /// "All items in stock" = every product in inventory. Both follow the Brand and Stock dropdowns.
        /// </summary>
        private void ApplyLineFilter()
        {
            string brandSel = Convert.ToString(cmbBrand.SelectedItem);
            bool brandOn = !string.IsNullOrEmpty(brandSel) && brandSel != AllBrands;
            bool lowOn = Convert.ToString(cmbStock.SelectedItem) == LowOnly;
            bool showAll = Convert.ToString(cmbView.SelectedItem) == ViewAll;

            gridLines.Visible = !showAll;
            gridStock.Visible = showAll;

            if (!showAll)
            {
                List<RequisitionLineRow> shown = _selLines.Where(l =>
                    (!brandOn || string.Equals(l.Brand, brandSel, StringComparison.OrdinalIgnoreCase)) &&
                    (!lowOn || l.Available < l.Quantity)).ToList();

                gridLines.DataSource = shown;
                gridLines.ClearSelection();

                lblCount.Text = string.Format("{0} of {1} requested item(s) shown", shown.Count, _selLines.Count);
            }
            else
            {
                List<CatalogRow> shown = _stock.Where(r =>
                    (!brandOn || string.Equals(r.Brand, brandSel, StringComparison.OrdinalIgnoreCase)) &&
                    (!lowOn || r.StockStatus != "OK"))
                    .OrderBy(r => r.Brand).ThenBy(r => r.Product).ThenBy(r => r.Size)
                    .ToList();

                gridStock.DataSource = shown;
                gridStock.ClearSelection();

                lblCount.Text = string.Format("{0} of {1} item(s) in stock shown", shown.Count, _stock.Count);
            }
        }

        private void SetButtons(RequisitionRow sel)
        {
            bool has = sel != null;
            bool mine = has && sel.RequestedById == _user.UserId;
            bool pending = has && sel.Status == Statuses.Pending;
            bool rejected = has && sel.Status == Statuses.Rejected;

            btnDetails.Enabled = has;
            btnApprove.Enabled = pending;
            btnReject.Enabled = pending;
            btnRevise.Enabled = mine && (pending || rejected);
            btnCancel.Enabled = mine && (pending || rejected);
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnNew(object sender, EventArgs e)
        {
            using (RequisitionForm f = new RequisitionForm())
            {
                if (f.ShowDialog(this) == DialogResult.OK) LoadData();
            }
        }

        private void OnRevise(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            using (RequisitionForm f = new RequisitionForm(sel.RequisitionId))
            {
                if (f.ShowDialog(this) == DialogResult.OK) LoadData();
            }
        }

        private void OnInventory(object sender, EventArgs e)
        {
            using (InventoryForm f = new InventoryForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // stock may have changed, so refresh the "In stock" / "Stock check" columns
        }

        private void OnSales(object sender, EventArgs e)
        {
            using (SalesForm f = new SalesForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // stock changed, so refresh the stock columns
        }

        private void OnIssuance(object sender, EventArgs e)
        {
            using (IssuanceForm f = new IssuanceForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // issued requisitions become Closed
        }

        private void OnPurchasing(object sender, EventArgs e)
        {
            using (PurchasingForm f = new PurchasingForm())
            {
                f.ShowDialog(this);
            }
            LoadData();
        }

        private void OnDelivery(object sender, EventArgs e)
        {
            using (DeliveryForm f = new DeliveryForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // received goods change the stock, so refresh the stock check columns
        }

        /// <summary>
        /// One line per item: "Nike Dunk Low (US 8) x6". Items that are short on stock get a LOW STOCK note.
        /// </summary>
        private static string DescribeLines(List<RequisitionLineRow> lines)
        {
            return string.Join("\r\n", lines.Select(l =>
            {
                string text = string.Format("{0} {1} (US {2}) x{3}", l.Brand, l.Product, l.Size, l.Quantity);
                if (l.Available < l.Quantity)
                    text += string.Format("  - LOW STOCK ({0} in stock)", l.Available);
                return text;
            }));
        }

        // DETAILS opens the same dialog: APPROVE / REJECT only show when the requisition is still pending.
        private void OnDetails(object sender, EventArgs e)
        {
            OpenReview();
        }

        // APPROVE and REJECT both open the details dialog first, so the Head sees every item before deciding.
        private void OnApprove(object sender, EventArgs e)
        {
            OpenReview();
        }

        private void OnReject(object sender, EventArgs e)
        {
            OpenReview();
        }

        private void OpenReview()
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            List<RequisitionLineRow> lines;
            try
            {
                lines = RequisitionService.GetLines(sel.RequisitionId);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return;
            }

            bool canDecide = _user.Role == Roles.Head && sel.Status == Statuses.Pending;

            RequisitionReviewDialog.Decision choice;
            string reason;
            using (RequisitionReviewDialog dlg = new RequisitionReviewDialog(sel, lines, canDecide))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                choice = dlg.Choice;
                reason = dlg.RejectReason;
            }

            if (choice == RequisitionReviewDialog.Decision.Approve) DoApprove(sel);
            else if (choice == RequisitionReviewDialog.Decision.Reject) DoReject(sel, reason);
        }

        private void DoApprove(RequisitionRow sel)
        {
            try
            {
                ApprovalResult result = RequisitionService.Approve(sel.RequisitionId, _user);

                string msg = result.RefNo + " was approved.\r\n\r\nStock check: " + result.Summary;
                if (!result.AllAvailable)
                    msg += "\r\n\r\n" + string.Join("\r\n", result.Shortages);

                bool openInventory = false;
                if (result.AllAvailable)
                {
                    MessageBox.Show(this, msg, "Approved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    msg += "\r\n\r\nOpen Inventory to check or add stock?";
                    openInventory = MessageBox.Show(this, msg, "Approved - stock is short",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }

                LoadData();
                if (openInventory) OnInventory(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void DoReject(RequisitionRow sel, string reason)
        {
            try
            {
                RequisitionService.Reject(sel.RequisitionId, _user, reason);
                LoadData();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void OnCancelRequest(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            if (MessageBox.Show(this, "Cancel requisition " + sel.RefNo + "? This cannot be undone.", "Cancel request",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                RequisitionService.Cancel(sel.RequisitionId, _user);
                LoadData();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}