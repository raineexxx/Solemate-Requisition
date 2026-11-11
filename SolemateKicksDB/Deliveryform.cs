using SolemateKicksDB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Delivery screen (analysis 3.4): record expected deliveries, inspect what arrived,
    /// accepted goods go into stock with a receiving report, rejected goods are logged,
    /// missing quantities stay outstanding for follow-up.
    /// Manager: new delivery, receive / inspect.  Everyone else: view only.
    /// </summary>
    public class DeliveryForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canEdit;

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));   // toolbar
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
            Panel header = Theme.MakeHeader("Delivery", "Receiving and inspection");

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

        private Control BuildToolbar()
        {
            Panel toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.White, Margin = new Padding(0) };

            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 11, 0, 0)
            };

            btnNew = Theme.MakeButton("+ NEW DELIVERY", 160, true, OnNew);
            btnReceive = Theme.MakeButton("RECEIVE / INSPECT", 180, true, OnReceive);
            btnHistory = Theme.MakeButton("REPORTS & ISSUES", 160, false, OnHistory);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadData());

            if (_canEdit)
            {
                flow.Controls.Add(btnNew);
                flow.Controls.Add(btnReceive);
            }
            flow.Controls.Add(btnHistory);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);

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

        private Control BuildBanner()
        {
            lblBanner = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = Theme.FontBold,
                ForeColor = Theme.Warn,
                BackColor = Color.FromArgb(255, 244, 224),
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

            lblDetail = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                AutoSize = false,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft
            };

            cardBottom.Controls.Add(gridItems);
            cardBottom.Controls.Add(lblDetail);
            cardBottom.Controls.Add(Theme.MakeSectionTitle("Items in selected delivery"));

            content.Controls.Add(cardTop, 0, 0);
            content.Controls.Add(cardBottom, 0, 1);
            return content;
        }

        private static Color StatusColor(string status)
        {
            switch (status)
            {
                case "Complete": return Theme.Ok;
                case "Short": return Theme.Red;
                case "Partial": return Theme.Warn;
                default: return Theme.Muted;      // Expected
            }
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private void LoadData()
        {
            _loading = true;
            try
            {
                _all = DeliveryService.GetDeliveries();
                UpdateBanner();
            }
            catch (Exception ex)
            {
                _all = new List<DeliveryRow>();
                ShowError(ex);
            }
            finally
            {
                _loading = false;
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

        private DeliveryRow Selected()
        {
            if (gridDel.CurrentRow == null) return null;
            return gridDel.CurrentRow.DataBoundItem as DeliveryRow;
        }

        private void OnSelectionChanged()
        {
            DeliveryRow sel = Selected();

            if (sel == null)
            {
                gridItems.DataSource = null;
                lblDetail.Text = string.Empty;
                SetButtons(null);
                return;
            }

            try
            {
                gridItems.DataSource = DeliveryService.GetItems(sel.DeliveryId);
                gridItems.ClearSelection();
            }
            catch (Exception ex)
            {
                ShowError(ex);
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
            btnReceive.Enabled = sel != null && (sel.Status == "Expected" || sel.Status == "Partial");
            btnHistory.Enabled = sel != null;
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnNew(object sender, EventArgs e)
        {
            using (NewDeliveryDialog dlg = new NewDeliveryDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                MessageBox.Show(this,
                    "Delivery " + dlg.CreatedRef + " was recorded as Expected.\r\n\r\n" +
                    "When the goods arrive, select it and click RECEIVE / INSPECT.",
                    "Delivery recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            LoadData();
        }

        private void OnReceive(object sender, EventArgs e)
        {
            DeliveryRow sel = Selected();
            if (sel == null) return;

            using (ReceiveDialog dlg = new ReceiveDialog(sel))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ShowReceiveResult(sel, dlg.Result);
            }
            LoadData();
        }

        private void ShowReceiveResult(DeliveryRow sel, ReceiveResult r)
        {
            List<string> lines = new List<string>();
            lines.Add("Delivery " + sel.DeliveryRef + " is now: " + r.Status);
            lines.Add(string.Empty);
            lines.Add("Accepted (added to stock): " + r.TotalAccepted);
            if (!string.IsNullOrEmpty(r.ReportRef)) lines.Add("Receiving report: " + r.ReportRef);

            if (r.TotalRejected > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Rejected: " + r.TotalRejected + " - logged, NOT added to stock.");
                lines.Add("Ask the supplier for a correction or replacement.");
            }
            if (r.Outstanding > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Outstanding: " + r.Outstanding + " - follow up with the supplier.");
            }
            if (!string.IsNullOrEmpty(r.RequisitionRef))
            {
                lines.Add(string.Empty);
                lines.Add(r.RequisitionReady == 1
                    ? "Requisition " + r.RequisitionRef + " now has enough stock - ready for issuance."
                    : "Requisition " + r.RequisitionRef + " still has items without enough stock.");
            }

            MessageBox.Show(this, string.Join("\r\n", lines), "Delivery received",
                MessageBoxButtons.OK, r.TotalRejected > 0 || r.Outstanding > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void OnHistory(object sender, EventArgs e)
        {
            DeliveryRow sel = Selected();
            if (sel == null) return;
            using (DeliveryHistoryDialog dlg = new DeliveryHistoryDialog(sel)) dlg.ShowDialog(this);
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void BuildUi()
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red };

            Label lblTitle = new Label
            {
                Text = string.Format("{0}   |   {1}{2}", _delivery.DeliveryRef, _delivery.SupplierName,
                    string.IsNullOrEmpty(_delivery.PoRef) ? "" : "   |   PO " + _delivery.PoRef),
                Font = Theme.FontSection,
                ForeColor = Theme.Red,
                AutoSize = false,
                Location = new Point(20, 22),
                Size = new Size(1000, 30)
            };

            Label lblHelp = new Label
            {
                Text = "Enter what arrived. Accepted goods go into stock. Rejected goods (damaged / wrong) are logged and are NOT added to stock - a note is required.",
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                AutoSize = false,
                Location = new Point(20, 54),
                Size = new Size(1000, 22)
            };

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.None;
            grid.Location = new Point(20, 86);
            grid.Size = new Size(1000, 360);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ReadOnly = false;
            grid.DataSource = _lines;
            Theme.AddColumn(grid, "Sku", "SKU", "Sku", 12f);
            Theme.AddColumn(grid, "Display", "Product", "Display", 30f);
            Theme.AddColumn(grid, "OrderedQty", "Ordered", "OrderedQty", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "AcceptedQty", "Accepted so far", "AcceptedQty", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "OutstandingQty", "Outstanding", "OutstandingQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);

            DataGridViewTextBoxColumn colAccept =
                Theme.AddColumn(grid, "AcceptNow", "Accept now", "AcceptNow", 9f, null, false, DataGridViewContentAlignment.MiddleCenter);
            colAccept.DefaultCellStyle.Font = Theme.FontBold;
            colAccept.DefaultCellStyle.BackColor = Color.FromArgb(240, 250, 244);

            DataGridViewTextBoxColumn colReject =
                Theme.AddColumn(grid, "RejectNow", "Reject now", "RejectNow", 9f, null, false, DataGridViewContentAlignment.MiddleCenter);
            colReject.DefaultCellStyle.Font = Theme.FontBold;
            colReject.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 246);

            Theme.AddColumn(grid, "Note", "Inspection note", "Note", 22f, null, false);

            grid.CellValidating += Grid_CellValidating;
            grid.CellEndEdit += (s, e) => { if (e.RowIndex >= 0) grid.Rows[e.RowIndex].ErrorText = string.Empty; };
            grid.DataError += (s, e) => { e.ThrowException = false; };

            Label lblRemarks = new Label
            {
                Text = "Remarks (optional)",
                Font = Theme.FontBold,
                AutoSize = true,
                Location = new Point(20, 466),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            txtRemarks = new TextBox
            {
                Font = Theme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 200,
                Location = new Point(20, 490),
                Width = 600,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            chkShort = new CheckBox
            {
                Text = "No more items are coming - close this delivery as SHORT (missing quantities are logged)",
                Font = Theme.FontBody,
                AutoSize = true,
                Location = new Point(20, 528),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            Button btnOk = Theme.MakeButton("CONFIRM RECEIPT", 200, true, OnConfirm);
            btnOk.Location = new Point(820, 580);
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Button btnCancel = Theme.MakeButton("CANCEL", 110, false, null);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(700, 580);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = btnCancel;

            Controls.Add(grid);
            Controls.Add(lblTitle);
            Controls.Add(lblHelp);
            Controls.Add(lblRemarks);
            Controls.Add(txtRemarks);
            Controls.Add(chkShort);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            Controls.Add(bar);
        }

        private void LoadLines()
        {
            try
            {
                foreach (DeliveryItemRow item in DeliveryService.GetItems(_delivery.DeliveryId))
                {
                    _lines.Add(new ReceiveLine
                    {
                        ItemId = item.ItemId,
                        Sku = item.Sku,
                        Display = string.Format("{0} {1} (US {2})", item.Brand, item.Product, item.Size),
                        OrderedQty = item.OrderedQty,
                        AcceptedQty = item.AcceptedQty,
                        OutstandingQty = item.OutstandingQty,
                        AcceptNow = item.OutstandingQty,   // assume everything is fine - change what is not
                        RejectNow = 0,
                        Note = string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = grid.Columns[e.ColumnIndex].Name;
            if (col != "AcceptNow" && col != "RejectNow") return;

            int value;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out value) || value < 0 || value > 99999)
            {
                grid.Rows[e.RowIndex].ErrorText = "Enter a whole number (0 or more).";
                e.Cancel = true;
                return;
            }

            if (col == "AcceptNow")
            {
                ReceiveLine line = grid.Rows[e.RowIndex].DataBoundItem as ReceiveLine;
                if (line != null && value > line.OutstandingQty)
                {
                    grid.Rows[e.RowIndex].ErrorText = "You cannot accept more than the outstanding quantity (" + line.OutstandingQty + ").";
                    e.Cancel = true;
                    return;
                }
            }

            grid.Rows[e.RowIndex].ErrorText = string.Empty;
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            grid.EndEdit();

            foreach (ReceiveLine line in _lines)
            {
                if (line.RejectNow > 0 && string.IsNullOrWhiteSpace(line.Note))
                {
                    MessageBox.Show(this, "Add an inspection note (condition / reason) for " + line.Sku + ", because some units are rejected.",
                        "Note required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            if (!_lines.Any(l => l.AcceptNow > 0 || l.RejectNow > 0) && !chkShort.Checked)
            {
                MessageBox.Show(this, "Enter an accepted or rejected quantity for at least one item, or tick the 'close as short' box.",
                    "Receive delivery", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string question = chkShort.Checked
                ? "Confirm this receipt and close the delivery as SHORT?"
                : "Confirm this receipt? Accepted quantities will be added to stock.";
            if (MessageBox.Show(this, question, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                Result = DeliveryService.Receive(_delivery.DeliveryId, _user, _lines, chkShort.Checked, txtRemarks.Text);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}