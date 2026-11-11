using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SolemateKicks
{
    // ------------------------------------------------------------------
    //  Rows returned by the usp_Del... procedures
    //  (property names must match the column names the procedure returns)
    // ------------------------------------------------------------------
    public class DeliveryRow
    {
        public int DeliveryId { get; set; }
        public string DeliveryRef { get; set; }
        public string PoRef { get; set; }
        public string SupplierName { get; set; }
        public string RequisitionRef { get; set; }
        public string Status { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateReceived { get; set; }
        public int Lines { get; set; }
        public int OrderedQty { get; set; }
        public int AcceptedQty { get; set; }
        public int RejectedQty { get; set; }
        public int OutstandingQty { get; set; }
        public string CreatedBy { get; set; }
        public string Remarks { get; set; }

        private List<DeliveryRow> _all = new List<DeliveryRow>();
        private bool _loading;

        private TableLayoutPanel root;
        private DataGridView gridDel;
        private DataGridView gridItems;
        private ComboBox cmbStatus;
        private Label lblBanner, lblDetail;
        private Button btnNew, btnReceive, btnHistory;

        public DeliveryForm()
        {
            _canEdit = _user != null && _user.Role == Roles.Manager;

            Text = "Solemate - Delivery";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
            Shown += (s, e) => LoadData();
        }

    public class DeliveryItemRow
    {
        public int ItemId { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int OrderedQty { get; set; }
        public int AcceptedQty { get; set; }
        public int RejectedQty { get; set; }
        public int OutstandingQty { get; set; }
        public string Note { get; set; }

        // added by 7_DeliveryUpgrade.sql
        public decimal UnitCost { get; set; }
        public decimal LineValue { get; set; }
    }

    public class ProductPick
    {
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Display { get; set; }
        public int Stock { get; set; }
    }

    public class ApprovedRequisitionPick
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Display { get; set; }
        public int HasShortage { get; set; }
    }

            Panel rightBox = new Panel { Dock = DockStyle.Right, Width = 270 };
            Label lbl = new Label { Text = "Show:", Font = Theme.FontBold, AutoSize = true, Location = new Point(10, 22) };
            cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(62, 18),
                Width = 180
            };
            cmbStatus.Items.AddRange(new object[] { "All", "Expected", "Partial", "Complete", "Short" });
            cmbStatus.SelectedIndex = 0;
            cmbStatus.SelectedIndexChanged += (s, e) => { if (!_loading) ApplyFilter(); };
            rightBox.Controls.Add(lbl);
            rightBox.Controls.Add(cmbStatus);

            toolbar.Controls.Add(flow);
            toolbar.Controls.Add(rightBox);
            return toolbar;
        }

    public class ReceivingReportRow
    {
        public string ReportRef { get; set; }
        public DateTime DateReceived { get; set; }
        public string ReceivedBy { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
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
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 54f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 46f));

            // ---- top card: deliveries ----
            Panel cardTop = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridDel = new DataGridView();
            Theme.StyleGrid(gridDel);
            gridDel.ReadOnly = true;
            Theme.AddColumn(gridDel, "DeliveryRef", "Delivery Ref", "DeliveryRef", 15f);
            Theme.AddColumn(gridDel, "PoRef", "PO Ref", "PoRef", 12f);
            Theme.AddColumn(gridDel, "Supplier", "Supplier", "SupplierName", 18f);
            Theme.AddColumn(gridDel, "Requisition", "Requisition", "RequisitionRef", 15f);
            Theme.AddColumn(gridDel, "Created", "Created", "DateCreated", 13f, "MMM dd, yyyy");
            Theme.AddColumn(gridDel, "Ordered", "Ordered", "OrderedQty", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Accepted", "Accepted", "AcceptedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Rejected", "Rejected", "RejectedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Outstanding", "Outstanding", "OutstandingQty", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Status", "Status", "Status", 10f);

            gridDel.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string col = gridDel.Columns[e.ColumnIndex].Name;
                DeliveryRow row = gridDel.Rows[e.RowIndex].DataBoundItem as DeliveryRow;
                if (row == null) return;

                if (col == "Status")
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = StatusColor(row.Status);
                }
                else if (col == "Outstanding" && row.OutstandingQty > 0 && row.Status != "Expected")
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = Theme.Red;
                }
                else if (col == "Rejected" && row.RejectedQty > 0)
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = Theme.Red;
                }
            };
            gridDel.SelectionChanged += (s, e) => { if (!_loading) OnSelectionChanged(); };

            cardTop.Controls.Add(gridDel);
            cardTop.Controls.Add(Theme.MakeSectionTitle("Deliveries"));

            // ---- bottom card: items of the selected delivery ----
            Panel cardBottom = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridItems = new DataGridView();
            Theme.StyleGrid(gridItems);
            gridItems.ReadOnly = true;
            Theme.AddColumn(gridItems, "Sku", "SKU", "Sku", 13f);
            Theme.AddColumn(gridItems, "Brand", "Brand", "Brand", 14f);
            Theme.AddColumn(gridItems, "Product", "Model", "Product", 22f);
            Theme.AddColumn(gridItems, "Size", "Size (US)", "Size", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "OrderedQty", "Ordered", "OrderedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "AcceptedQty", "Accepted", "AcceptedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "RejectedQty", "Rejected", "RejectedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "OutstandingQty", "Outstanding", "OutstandingQty", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "Note", "Inspection note", "Note", 22f);

            gridItems.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string col = gridItems.Columns[e.ColumnIndex].Name;
                DeliveryItemRow row = gridItems.Rows[e.RowIndex].DataBoundItem as DeliveryItemRow;
                if (row == null) return;

                if (col == "OutstandingQty")
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = row.OutstandingQty > 0 ? Theme.Warn : Theme.Ok;
                }
                else if (col == "RejectedQty" && row.RejectedQty > 0)
                {
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = Theme.Red;
                }
            };

    /// <summary>Header of one receiving report, used by the print preview (usp_DelGetReportHeader).</summary>
    public class ReportHeaderRow
    {
        public string ReportRef { get; set; }
        public DateTime DateReceived { get; set; }
        public string ReceivedBy { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
        public string Remarks { get; set; }
        public string DeliveryRef { get; set; }
        public string PoRef { get; set; }
        public string SupplierName { get; set; }
        public string RequisitionRef { get; set; }
    }

    /// <summary>One line of a receiving report (usp_DelGetReportLines).</summary>
    public class ReportLineRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int AcceptedQty { get; set; }
        public int RejectedQty { get; set; }
        public string Note { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineValue { get; set; }
    }

    /// <summary>What usp_DelReceive returns after a receipt.</summary>
    public class ReceiveResult
    {
        public string ReportRef { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
        public int Outstanding { get; set; }
        public string Status { get; set; }
        public string RequisitionRef { get; set; }
        public int RequisitionReady { get; set; }
    }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string status = Convert.ToString(cmbStatus.SelectedItem);

            IEnumerable<DeliveryRow> query = _all;
            if (!string.IsNullOrEmpty(status) && status != "All")
                query = query.Where(d => d.Status == status);

            _loading = true;
            gridDel.DataSource = null;
            gridDel.DataSource = query.ToList();
            gridDel.ClearSelection();
            _loading = false;

            if (gridDel.Rows.Count > 0)
            {
                gridDel.Rows[0].Selected = true;
                gridDel.CurrentCell = gridDel.Rows[0].Cells[0];
            }
            OnSelectionChanged();
        }

        private void UpdateBanner()
        {
            int pending = _all.Count(d => (d.Status == "Partial" || d.Status == "Short") && d.OutstandingQty > 0);

            string msg = null;
            if (pending > 0)
                msg = string.Format("   {0} delivery(ies) still have outstanding items - follow up with the supplier.", pending);

            lblBanner.Text = msg ?? string.Empty;
            root.RowStyles[2].Height = (msg == null) ? 0f : 36f;
        }

    /// <summary>
    /// All database work for the Delivery module. Same style as InventoryService:
    /// it calls the usp_Del... procedures from 6_Delivery.sql and 7_DeliveryUpgrade.sql
    /// with db.ExecuteQuery, so Solemate.dbml does not need to be changed.
    /// Errors raised by THROW in SQL arrive here as exceptions whose Message is the SQL text.
    /// </summary>
    public static class DeliveryService
    {
        /// <summary>All deliveries. Search, status and date filters are applied on screen.</summary>
        public static List<DeliveryRow> GetDeliveries()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DeliveryRow>("EXEC dbo.usp_DelGetList {0}", "All").ToList();
            }
        }

            List<string> parts = new List<string>();
            parts.Add("Created by " + sel.CreatedBy);
            if (sel.DateReceived.HasValue) parts.Add("Last received " + sel.DateReceived.Value.ToString("MMM dd, yyyy h:mm tt"));
            if (!string.IsNullOrWhiteSpace(sel.Remarks)) parts.Add("Remarks: " + sel.Remarks);
            if (sel.Status == "Short") parts.Add("Closed as short - request the missing items from the supplier");
            lblDetail.Text = string.Join("     |     ", parts);

            SetButtons(sel);
        }

        private void SetButtons(DeliveryRow sel)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DeliveryItemRow>("EXEC dbo.usp_DelGetItems {0}", deliveryId).ToList();
            }
        }

        public static List<ProductPick> GetProducts()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ProductPick>("EXEC dbo.usp_DelGetProducts").ToList();
            }
        }

        public static List<ApprovedRequisitionPick> GetApprovedRequisitions()
        {
            List<string> lines = new List<string>();
            lines.Add("Delivery " + sel.DeliveryRef + " is now: " + r.Status);
            lines.Add(string.Empty);
            lines.Add("Accepted (added to stock): " + r.TotalAccepted);
            if (!string.IsNullOrEmpty(r.ReportRef)) lines.Add("Receiving report: " + r.ReportRef);

            if (r.TotalRejected > 0)
            {
                return db.ExecuteQuery<ApprovedRequisitionPick>("EXEC dbo.usp_DelGetApprovedRequisitions").ToList();
            }
        }

        public static List<ShortageRow> GetShortage(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ShortageRow>("EXEC dbo.usp_DelGetShortage {0}", requisitionId).ToList();
            }
        }

    // ======================================================================
    //  NEW DELIVERY  (manager records what is expected from the supplier)
    // ======================================================================
    public class NewDeliveryDialog : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly BindingList<DeliveryLine> _lines = new BindingList<DeliveryLine>();

        private ComboBox cmbSupplier, cmbReq, cmbProduct;
        private TextBox txtPo, txtRemarks;
        private NumericUpDown nudQty;
        private DataGridView grid;
        private Button btnSave;
        private Label lblTotal;

        public string CreatedRef { get; private set; }

        public NewDeliveryDialog()
        {
            Text = "Solemate - New delivery";
            ClientSize = new Size(960, 660);
            MinimumSize = new Size(960, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            BuildUi();
            LoadPickLists();
            RefreshTotal();
        }

        private void BuildUi()
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red };

            // ---- row 1 ----
            Label lblSupplier = new Label { Text = "Supplier", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 24) };
            cmbSupplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.FontBody, Location = new Point(20, 46), Width = 280 };
            cmbSupplier.Items.AddRange(new object[] { "United Kingdom supplier", "Vietnam supplier" });

            Label lblPo = new Label { Text = "PO reference (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(320, 24) };
            txtPo = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, MaxLength = 30, Location = new Point(320, 46), Width = 200 };

            Label lblReq = new Label { Text = "Linked approved requisition (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(540, 24) };
            cmbReq = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.FontBody, Location = new Point(540, 46), Width = 260, DisplayMember = "Display" };
            Button btnLoad = Theme.MakeButton("LOAD SHORTAGE", 140, false, OnLoadShortage);
            btnLoad.Location = new Point(808, 42);
            btnLoad.Height = 32;

            // ---- row 2 ----
            Label lblRemarks = new Label { Text = "Remarks (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 90) };
            txtRemarks = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, MaxLength = 200, Location = new Point(20, 112), Width = 780 };

            // ---- row 3: add an item ----
            Label lblProduct = new Label { Text = "Add item", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 156) };
            cmbProduct = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = Theme.FontBody,
                Location = new Point(20, 178),
                Width = 600,
                DisplayMember = "Display",
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            Label lblQty = new Label { Text = "Qty", Font = Theme.FontBold, AutoSize = true, Location = new Point(640, 156) };
            nudQty = new NumericUpDown { Font = Theme.FontBody, Minimum = 1, Maximum = 9999, Value = 1, Location = new Point(640, 178), Width = 80 };
            Button btnAdd = Theme.MakeButton("ADD ITEM", 130, true, OnAddItem);
            btnAdd.Location = new Point(740, 174);
            btnAdd.Height = 32;

            // ---- grid ----
            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.None;
            grid.Location = new Point(20, 222);
            grid.Size = new Size(920, 340);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ReadOnly = false;
            grid.DataSource = _lines;
            Theme.AddColumn(grid, "Sku", "SKU", "Sku", 16f);
            Theme.AddColumn(grid, "Display", "Product", "Display", 56f);
            DataGridViewTextBoxColumn colQty =
                Theme.AddColumn(grid, "OrderedQty", "Ordered qty", "OrderedQty", 14f, null, false, DataGridViewContentAlignment.MiddleCenter);
            colQty.DefaultCellStyle.Font = Theme.FontBold;
            colQty.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 246);

            DataGridViewButtonColumn colRemove = new DataGridViewButtonColumn
            {
                Name = "Remove",
                HeaderText = string.Empty,
                Text = "Remove",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 14f,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRemove.DefaultCellStyle.ForeColor = Theme.Red;
            colRemove.DefaultCellStyle.BackColor = Theme.White;
            grid.Columns.Add(colRemove);

            grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Remove") _lines.RemoveAt(e.RowIndex);
            };
            grid.CellValidating += Grid_CellValidating;
            grid.CellEndEdit += (s, e) => { if (e.RowIndex >= 0) grid.Rows[e.RowIndex].ErrorText = string.Empty; RefreshTotal(); };
            grid.DataError += (s, e) => { e.ThrowException = false; };
            _lines.ListChanged += (s, e) => RefreshTotal();

            // ---- footer ----
            lblTotal = new Label
            {
                Font = Theme.FontBold,
                ForeColor = Theme.Red,
                AutoSize = false,
                Location = new Point(20, 590),
                Size = new Size(420, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            btnSave = Theme.MakeButton("SAVE DELIVERY", 190, true, OnSave);
            btnSave.Location = new Point(750, 586);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Button btnCancel = Theme.MakeButton("CANCEL", 110, false, null);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(630, 586);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = btnCancel;

            Controls.Add(grid);
            Controls.Add(lblSupplier);
            Controls.Add(cmbSupplier);
            Controls.Add(lblPo);
            Controls.Add(txtPo);
            Controls.Add(lblReq);
            Controls.Add(cmbReq);
            Controls.Add(btnLoad);
            Controls.Add(lblRemarks);
            Controls.Add(txtRemarks);
            Controls.Add(lblProduct);
            Controls.Add(cmbProduct);
            Controls.Add(lblQty);
            Controls.Add(nudQty);
            Controls.Add(btnAdd);
            Controls.Add(lblTotal);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(bar);
        }

        private void LoadPickLists()
        {
            try
            {
                cmbProduct.DataSource = DeliveryService.GetProducts();
                cmbProduct.SelectedIndex = -1;

                List<ApprovedRequisitionPick> reqs = DeliveryService.GetApprovedRequisitions();
                reqs.Insert(0, new ApprovedRequisitionPick { RequisitionId = 0, RefNo = string.Empty, Display = "(none)" });
                cmbReq.DataSource = reqs;
                cmbReq.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddOrIncrease(int productId, string sku, string display, int qty)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].ProductId == productId)
                {
                    _lines[i].OrderedQty += qty;
                    _lines.ResetItem(i);
                    return;
                }
            }
            _lines.Add(new DeliveryLine { ProductId = productId, Sku = sku, Display = display, OrderedQty = qty });
        }

        private void OnAddItem(object sender, EventArgs e)
        {
            ProductPick pick = cmbProduct.SelectedItem as ProductPick;
            if (pick == null)
            {
                MessageBox.Show(this, "Choose a product from the list first.", "Add item", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddOrIncrease(pick.ProductId, pick.Sku, pick.Display, (int)nudQty.Value);
            cmbProduct.SelectedIndex = -1;
            cmbProduct.Text = string.Empty;
            nudQty.Value = 1;
            cmbProduct.Focus();
        }

        private void OnLoadShortage(object sender, EventArgs e)
        {
            ApprovedRequisitionPick req = cmbReq.SelectedItem as ApprovedRequisitionPick;
            if (req == null || req.RequisitionId == 0)
            {
                MessageBox.Show(this, "Choose an approved requisition first.", "Load shortage", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                List<ShortageRow> shortage = DeliveryService.GetShortage(req.RequisitionId);
                if (shortage.Count == 0)
                {
                    MessageBox.Show(this, "Stock already covers every item of " + req.RefNo + ". There is nothing to order.",
                        "Load shortage", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                foreach (ShortageRow s in shortage) AddOrIncrease(s.ProductId, s.Sku, s.Display, s.Shortage);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "OrderedQty") return;

            int qty;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out qty) || qty < 1 || qty > 9999)
            {
                grid.Rows[e.RowIndex].ErrorText = "Quantity must be a whole number from 1 to 9999.";
                e.Cancel = true;
            }
            else
            {
                grid.Rows[e.RowIndex].ErrorText = string.Empty;
            }
        }

        private void RefreshTotal()
        {
            lblTotal.Text = _lines.Count == 0
                ? "No items yet"
                : string.Format("{0} item(s)   |   {1} pair(s) ordered", _lines.Count, _lines.Sum(l => l.OrderedQty));
            btnSave.Enabled = _lines.Count > 0;
        }

        private void OnSave(object sender, EventArgs e)
        {
            grid.EndEdit();

            try
            {
                ApprovedRequisitionPick req = cmbReq.SelectedItem as ApprovedRequisitionPick;
                int reqId = (req == null) ? 0 : req.RequisitionId;

                CreatedRef = DeliveryService.CreateDelivery(_user, cmbSupplier.Text, txtPo.Text, reqId,
                    txtRemarks.Text, _lines);

                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ======================================================================
    //  RECEIVE / INSPECT  (manager checks quantity and condition)
    // ======================================================================
    public class ReceiveDialog : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly DeliveryRow _delivery;
        private readonly BindingList<ReceiveLine> _lines = new BindingList<ReceiveLine>();

        private DataGridView grid;
        private CheckBox chkShort;
        private TextBox txtRemarks;

        public ReceiveResult Result { get; private set; }

        public ReceiveDialog(DeliveryRow delivery)
        {
            _delivery = delivery;

            Text = "Solemate - Receive delivery " + delivery.DeliveryRef;
            ClientSize = new Size(1040, 640);
            MinimumSize = new Size(1000, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            BuildUi();
            LoadLines();
        }

        // ------------------------------------------------------------------
        //  Create / receive
        // ------------------------------------------------------------------
        /// <summary>
        /// Records an expected delivery. requisitionId = 0 means not linked,
        /// supplierId = 0 means "use the supplier name (created if new)",
        /// expectedDate = null means no due date. Returns the DEL- reference.
        /// </summary>
        public static string CreateDelivery(AppUser user, string supplier, int supplierId, string poRef,
            int requisitionId, DateTime? expectedDate, string remarks, IEnumerable<DeliveryLine> lines)
        {
            List<DeliveryLine> items = lines.ToList();
            if (string.IsNullOrWhiteSpace(supplier))
                throw new InvalidOperationException("Supplier is required.");
            if (items.Count == 0)
                throw new InvalidOperationException("Add at least one item to the delivery.");
            if (items.Any(l => l.OrderedQty < 1))
                throw new InvalidOperationException("Every ordered quantity must be at least 1.");
            if (items.Any(l => l.UnitCost < 0))
                throw new InvalidOperationException("Unit cost cannot be negative.");

            XElement root = new XElement("Items",
                items.Select(l =>
                {
                    XElement item = new XElement("Item",
                        new XAttribute("ProductId", l.ProductId),
                        new XAttribute("Quantity", l.OrderedQty));
                    if (l.UnitCost > 0) item.Add(new XAttribute("UnitCost", l.UnitCost));
                    return item;
                }));

            string sql = "EXEC dbo.usp_DelCreate @UserId={0}, @SupplierName={1}, @PoRef={2}, @RequisitionId={3}, " +
                         "@Remarks={4}, @ItemsXml={5}, @SupplierId={6}";
            List<object> args = new List<object>
            {
                user.UserId, supplier.Trim(), poRef ?? string.Empty, requisitionId,
                remarks ?? string.Empty, root.ToString(SaveOptions.DisableFormatting), supplierId
            };
            if (expectedDate.HasValue)
            {
                sql += ", @ExpectedDate={7}";
                args.Add(expectedDate.Value.Date);
            }

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<string>(sql, args.ToArray()).First();
            }
        }

        /// <summary>Inspect a delivery: accepted goes to stock, rejected is logged, the rest stays outstanding.</summary>
        public static ReceiveResult Receive(int deliveryId, AppUser user, IEnumerable<ReceiveLine> lines,
            bool closeShort, string remarks)
        {
            XElement root = new XElement("Lines",
                lines.Where(l => l.AcceptNow > 0 || l.RejectNow > 0)
                     .Select(l => new XElement("Line",
                         new XAttribute("ItemId", l.ItemId),
                         new XAttribute("Accepted", l.AcceptNow),
                         new XAttribute("Rejected", l.RejectNow),
                         new XAttribute("Note", l.Note ?? string.Empty))));

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReceiveResult>(
                    "EXEC dbo.usp_DelReceive {0}, {1}, {2}, {3}, {4}",
                    deliveryId, user.UserId, root.ToString(SaveOptions.DisableFormatting),
                    closeShort ? 1 : 0, remarks ?? string.Empty).First();
            }
        }

        // ------------------------------------------------------------------
        //  Reports
        // ------------------------------------------------------------------
        public static List<ReceivingReportRow> GetReports(int deliveryId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReceivingReportRow>("EXEC dbo.usp_DelGetReports {0}", deliveryId).ToList();
            }
        }

        public static List<DiscrepancyRow> GetDiscrepancies(int deliveryId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DiscrepancyRow>("EXEC dbo.usp_DelGetDiscrepancies {0}", deliveryId).ToList();
            }
        }

        /// <summary>Header of one receiving report for printing (null if not found).</summary>
        public static ReportHeaderRow GetReportHeader(string reportRef)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReportHeaderRow>("EXEC dbo.usp_DelGetReportHeader {0}", reportRef).FirstOrDefault();
            }
        }

    // ======================================================================
    //  REPORTS & ISSUES  (receiving reports + discrepancy log of one delivery)
    // ======================================================================
    public class DeliveryHistoryDialog : Form
    {
        public DeliveryHistoryDialog(DeliveryRow delivery)
        {
            Text = "Solemate - Reports and issues: " + delivery.DeliveryRef;
            ClientSize = new Size(960, 620);
            MinimumSize = new Size(800, 520);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));

            // ---- receiving reports ----
            Panel cardReports = Theme.MakeCard(new Padding(14, 8, 14, 12));
            DataGridView gridReports = new DataGridView();
            Theme.StyleGrid(gridReports);
            gridReports.ReadOnly = true;
            Theme.AddColumn(gridReports, "ReportRef", "Receiving report", "ReportRef", 24f);
            Theme.AddColumn(gridReports, "DateReceived", "Date received", "DateReceived", 26f, "MMM dd, yyyy  h:mm tt");
            Theme.AddColumn(gridReports, "ReceivedBy", "Received by", "ReceivedBy", 24f);
            Theme.AddColumn(gridReports, "TotalAccepted", "Accepted", "TotalAccepted", 13f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReports, "TotalRejected", "Rejected", "TotalRejected", 13f, null, true, DataGridViewContentAlignment.MiddleCenter);
            cardReports.Controls.Add(gridReports);
            cardReports.Controls.Add(Theme.MakeSectionTitle("Receiving reports (accepted goods)"));

            // ---- discrepancies ----
            Panel cardDisc = Theme.MakeCard(new Padding(14, 8, 14, 12));
            DataGridView gridDisc = new DataGridView();
            Theme.StyleGrid(gridDisc);
            gridDisc.ReadOnly = true;
            Theme.AddColumn(gridDisc, "DiscrepancyType", "Issue", "DiscrepancyType", 10f);
            Theme.AddColumn(gridDisc, "Sku", "SKU", "Sku", 12f);
            Theme.AddColumn(gridDisc, "Product", "Model", "Product", 20f);
            Theme.AddColumn(gridDisc, "Size", "Size", "Size", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDisc, "Qty", "Qty", "Qty", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDisc, "Note", "Note", "Note", 26f);
            Theme.AddColumn(gridDisc, "DateLogged", "Logged", "DateLogged", 16f, "MMM dd, yyyy");
            Theme.AddColumn(gridDisc, "ByUser", "By", "ByUser", 14f);
            gridDisc.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridDisc.Columns[e.ColumnIndex].Name != "DiscrepancyType") return;
                e.CellStyle.Font = Theme.FontBold;
                e.CellStyle.ForeColor = Theme.Red;
            };
            cardDisc.Controls.Add(gridDisc);
            cardDisc.Controls.Add(Theme.MakeSectionTitle("Discrepancies (rejected or short - follow up with the supplier)"));

            layout.Controls.Add(cardReports, 0, 0);
            layout.Controls.Add(cardDisc, 0, 1);
            Controls.Add(layout);

            try
            {
                gridReports.DataSource = DeliveryService.GetReports(delivery.DeliveryId);
                gridDisc.DataSource = DeliveryService.GetDiscrepancies(delivery.DeliveryId);
                gridReports.ClearSelection();
                gridDisc.ClearSelection();
            }
            catch (Exception ex)
            {
                return db.ExecuteQuery<ReportLineRow>("EXEC dbo.usp_DelGetReportLines {0}", reportRef).ToList();
            }

            btnPrint.Enabled = gridReports.Rows.Count > 0;
        }

        private void OnPrint(object sender, EventArgs e)
        {
            ReceivingReportRow sel = (gridReports.CurrentRow == null)
                ? null
                : gridReports.CurrentRow.DataBoundItem as ReceivingReportRow;

            if (sel == null)
            {
                MessageBox.Show(this, "Select a receiving report first.", "Print report",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                ReportHeaderRow header = DeliveryService.GetReportHeader(sel.ReportRef);
                if (header == null)
                {
                    MessageBox.Show(this, "The receiving report was not found.", "Print report",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<ReportLineRow> lines = DeliveryService.GetReportLines(sel.ReportRef);
                new DeliveryReportPrinter(header, lines).ShowPreview(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ======================================================================
    //  PRINTABLE RECEIVING REPORT  (print preview -> printer or "Microsoft Print to PDF")
    // ======================================================================
    public class DeliveryReportPrinter
    {
        private readonly ReportHeaderRow _h;
        private readonly List<ReportLineRow> _lines;
        private int _index;
        private int _page;

        public DeliveryReportPrinter(ReportHeaderRow header, List<ReportLineRow> lines)
        {
            _h = header;
            _lines = lines ?? new List<ReportLineRow>();
        }

        public void ShowPreview(IWin32Window owner)
        {
            using (PrintDocument doc = new PrintDocument())
            using (PrintPreviewDialog dlg = new PrintPreviewDialog())
            {
                doc.DocumentName = "Receiving report " + _h.ReportRef;
                doc.DefaultPageSettings.Margins = new Margins(60, 60, 60, 60);
                doc.BeginPrint += (s, e) => { _index = 0; _page = 0; };
                doc.PrintPage += OnPrintPage;

                dlg.Document = doc;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(1000, 780);
                dlg.ShowDialog(owner);
            }
        }

        private static string Dash(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "-" : s;
        }

        private static string Money(decimal value)
        {
            return value <= 0 ? "-" : value.ToString("N2");
        }

        private static void DrawCell(Graphics g, string text, Font font, Brush brush, RectangleF rect, StringAlignment align)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush, rect, sf);
            }
        }

        private void OnPrintPage(object sender, PrintPageEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle m = e.MarginBounds;
            _page++;

            using (Font fTitle = new Font("Segoe UI", 18f, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (Font fBody = new Font("Segoe UI", 9f))
            using (Font fBold = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (Brush brRed = new SolidBrush(Theme.Red))
            using (Brush brBlack = new SolidBrush(Color.Black))
            using (Brush brWhite = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.Gray, 1f))
            using (Pen penLight = new Pen(Color.LightGray, 1f))
            {
                float y = m.Top;

                // ---- title ----
                g.DrawString("SOLEMATE KICKS", fTitle, brRed, m.Left, y);
                DrawCell(g, "RECEIVING REPORT", fSub, brBlack, new RectangleF(m.Left, y + 6, m.Width, 26), StringAlignment.Far);
                y += fTitle.GetHeight(g) + 6;
                g.DrawLine(pen, m.Left, y, m.Right, y);
                y += 10;

                // ---- details (first page only) ----
                if (_page == 1)
                {
                    float half = m.Width / 2f;
                    float rowH = fBody.GetHeight(g) + 4;

                    string[,] left =
                    {
                        { "Report no.", Dash(_h.ReportRef) },
                        { "Delivery", Dash(_h.DeliveryRef) },
                        { "PO reference", Dash(_h.PoRef) },
                        { "Supplier", Dash(_h.SupplierName) }
                    };
                    string[,] right =
                    {
                        { "Date received", _h.DateReceived.ToString("MMM dd, yyyy h:mm tt") },
                        { "Received by", Dash(_h.ReceivedBy) },
                        { "Requisition", Dash(_h.RequisitionRef) },
                        { "Remarks", Dash(_h.Remarks) }
                    };

                    for (int i = 0; i < 4; i++)
                    {
                        float ry = y + i * rowH;
                        DrawCell(g, left[i, 0] + ":", fBold, brBlack, new RectangleF(m.Left, ry, 100, rowH), StringAlignment.Near);
                        DrawCell(g, left[i, 1], fBody, brBlack, new RectangleF(m.Left + 100, ry, half - 110, rowH), StringAlignment.Near);
                        DrawCell(g, right[i, 0] + ":", fBold, brBlack, new RectangleF(m.Left + half, ry, 100, rowH), StringAlignment.Near);
                        DrawCell(g, right[i, 1], fBody, brBlack, new RectangleF(m.Left + half + 100, ry, half - 100, rowH), StringAlignment.Near);
                    }
                    y += rowH * 4 + 12;
                }

                // ---- table header ----
                float[] frac = { 0.13f, 0.33f, 0.06f, 0.10f, 0.10f, 0.13f, 0.15f };
                string[] heads = { "SKU", "Item", "Size", "Accepted", "Rejected", "Unit cost", "Amount" };
                StringAlignment[] aligns =
                {
                    StringAlignment.Near, StringAlignment.Near, StringAlignment.Center, StringAlignment.Center,
                    StringAlignment.Center, StringAlignment.Far, StringAlignment.Far
                };
                float[] xs = new float[frac.Length];
                float[] ws = new float[frac.Length];
                float acc = m.Left;
                for (int i = 0; i < frac.Length; i++)
                {
                    xs[i] = acc;
                    ws[i] = frac[i] * m.Width;
                    acc += ws[i];
                }

                float rowH2 = fBody.GetHeight(g) + 8;
                g.FillRectangle(brRed, m.Left, y, m.Width, rowH2);
                for (int i = 0; i < frac.Length; i++)
                    DrawCell(g, heads[i], fBold, brWhite, new RectangleF(xs[i] + 3, y, ws[i] - 6, rowH2), aligns[i]);
                y += rowH2;

                // ---- table rows (keep room at the bottom for totals, notes and signatures) ----
                while (_index < _lines.Count)
                {
                    if (y + rowH2 > m.Bottom - 170)
                    {
                        e.HasMorePages = true;
                        return;
                    }

                    ReportLineRow r = _lines[_index];
                    string[] cells =
                    {
                        Dash(r.Sku),
                        (r.Brand + " " + r.Product).Trim(),
                        r.Size.ToString(),
                        r.AcceptedQty.ToString(),
                        r.RejectedQty.ToString(),
                        Money(r.UnitCost),
                        Money(r.LineValue)
                    };
                    for (int i = 0; i < cells.Length; i++)
                        DrawCell(g, cells[i], fBody, brBlack, new RectangleF(xs[i] + 3, y, ws[i] - 6, rowH2), aligns[i]);

                    y += rowH2;
                    g.DrawLine(penLight, m.Left, y, m.Right, y);
                    _index++;
                }

                // ---- totals ----
                y += 8;
                decimal totalValue = _lines.Sum(l => l.LineValue);
                string totals = string.Format("Total accepted: {0}      Total rejected: {1}      Total value: {2}",
                    _h.TotalAccepted, _h.TotalRejected, Money(totalValue));
                DrawCell(g, totals, fBold, brBlack, new RectangleF(m.Left, y, m.Width, rowH2), StringAlignment.Far);
                y += rowH2 + 6;

                // ---- inspection notes ----
                List<string> notes = _lines
                    .Where(l => !string.IsNullOrWhiteSpace(l.Note))
                    .Select(l => l.Sku + ": " + l.Note.Trim())
                    .ToList();
                if (notes.Count > 0)
                {
                    g.DrawString("Inspection notes", fBold, brBlack, m.Left, y);
                    y += fBold.GetHeight(g) + 2;
                    foreach (string n in notes)
                    {
                        SizeF size = g.MeasureString(n, fBody, m.Width);
                        if (y + size.Height > m.Bottom - 70) break;
                        g.DrawString(n, fBody, brBlack, new RectangleF(m.Left, y, m.Width, size.Height));
                        y += size.Height + 2;
                    }
                }

                // ---- signatures ----
                float sigY = m.Bottom - 40;
                g.DrawLine(pen, m.Left, sigY, m.Left + 220, sigY);
                g.DrawString("Received by (Manager)", fBody, brBlack, m.Left, sigY + 4);
                g.DrawLine(pen, m.Right - 220, sigY, m.Right, sigY);
                g.DrawString("Checked / noted by", fBody, brBlack, m.Right - 220, sigY + 4);

                e.HasMorePages = false;
            }
        }
    }
}
