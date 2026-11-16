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
    /// Inventory screen: stock by brand with OK / LOW / OUT status.
    /// Manager: add product, edit product, stock in / correction.  Everyone else: view only.
    /// </summary>
    public class InventoryForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canEdit;

        private List<CatalogRow> _all = new List<CatalogRow>();
        private List<Brand> _brands = new List<Brand>();
        private bool _loading;

        private TableLayoutPanel root;
        private DataGridView grid;
        private ComboBox cmbBrand, cmbStatus;
        private TextBox txtSearch;
        private Label lblBanner, lblCount;
        private Button btnAdd, btnEdit, btnStock, btnHistory;

        public InventoryForm()
        {
            _canEdit = _user != null && _user.Role == Roles.Manager;

            Text = "Solemate - Inventory";
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));    // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));   // toolbar (filters + buttons)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));     // banner (0 = hidden)
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // grid

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildToolbar(), 0, 1);
            root.Controls.Add(BuildBanner(), 0, 2);
            root.Controls.Add(BuildContent(), 0, 3);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Inventory", "Stock levels by brand");

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

            // ---- row 1: filters ----
            Panel filters = new Panel { Dock = DockStyle.Top, Height = 50 };

            filters.Controls.Add(new Label { Text = "Brand:", Font = Theme.FontBold, AutoSize = true, Location = new Point(12, 17) });
            cmbBrand = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(70, 13),
                Width = 180,
                DisplayMember = "BrandName"
            };

            filters.Controls.Add(new Label { Text = "Status:", Font = Theme.FontBold, AutoSize = true, Location = new Point(272, 17) });
            cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(334, 13),
                Width = 120
            };
            cmbStatus.Items.AddRange(new object[] { "All", "OK", "LOW", "OUT" });
            cmbStatus.SelectedIndex = 0;

            filters.Controls.Add(new Label { Text = "Search:", Font = Theme.FontBold, AutoSize = true, Location = new Point(476, 17) });
            txtSearch = new TextBox
            {
                Font = Theme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(542, 13),
                Width = 220
            };

            lblCount = new Label
            {
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(784, 18)
            };

            filters.Controls.Add(cmbBrand);
            filters.Controls.Add(cmbStatus);
            filters.Controls.Add(txtSearch);
            filters.Controls.Add(lblCount);

            cmbBrand.SelectedIndexChanged += (s, e) => ApplyFilter();
            cmbStatus.SelectedIndexChanged += (s, e) => ApplyFilter();
            txtSearch.TextChanged += (s, e) => ApplyFilter();

            // ---- row 2: buttons ----
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 2, 0, 0)
            };

            btnAdd = Theme.MakeButton("+ ADD PRODUCT", 150, true, OnAdd);
            btnEdit = Theme.MakeButton("EDIT PRODUCT", 130, false, OnEdit);
            btnStock = Theme.MakeButton("STOCK IN / ADJUST", 170, true, OnStock);
            btnHistory = Theme.MakeButton("HISTORY", 100, false, OnHistory);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadData());

            if (_canEdit)
            {
                flow.Controls.Add(btnAdd);
                flow.Controls.Add(btnEdit);
                flow.Controls.Add(btnStock);
            }
            flow.Controls.Add(btnHistory);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);

            toolbar.Controls.Add(flow);
            toolbar.Controls.Add(filters);
            return toolbar;
        }

        private Control BuildBanner()
        {
            lblBanner = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.FontBold,
                ForeColor = Theme.Warn,
                BackColor = Color.FromArgb(255, 244, 224),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                Margin = new Padding(0)
            };
            return lblBanner;
        }

        private Control BuildContent()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colBrand", "BRAND", "Brand", 15);
            Theme.AddColumn(grid, "colSku", "SKU", "Sku", 15);
            Theme.AddColumn(grid, "colProduct", "PRODUCT", "Product", 28);
            Theme.AddColumn(grid, "colSize", "SIZE", "Size", 7, null, true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colPrice", "PRICE", "Price", 11, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colStock", "STOCK", "Stock", 8, null, true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colReorder", "REORDER AT", "ReorderLevel", 10, null, true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colStatus", "STATUS", "StockStatus", 9, null, true, DataGridViewContentAlignment.MiddleCenter);

            grid.CellFormatting += OnCellFormatting;
            grid.SelectionChanged += (s, e) => SetButtons();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && _canEdit) OnStock(s, e); };

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Products"));
            return card;
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = grid.Columns[e.ColumnIndex].Name;
            if (col != "colStatus" && col != "colStock") return;

            CatalogRow row = grid.Rows[e.RowIndex].DataBoundItem as CatalogRow;
            if (row == null) return;

            Color c = row.StockStatus == "OUT" ? Theme.Red : (row.StockStatus == "LOW" ? Theme.Warn : Theme.Ok);
            e.CellStyle.ForeColor = c;
            e.CellStyle.Font = Theme.FontBold;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private CatalogRow Selected
        {
            get
            {
                if (grid.CurrentRow == null) return null;
                return grid.CurrentRow.DataBoundItem as CatalogRow;
            }
        }

        private void LoadData()
        {
            try
            {
                int keepId = Selected != null ? Selected.ProductId : 0;

                _all = InventoryService.GetStock();
                LoadBrands();
                UpdateBanner();
                ApplyFilter(keepId);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void LoadBrands()
        {
            int keepBrand = cmbBrand.SelectedItem is Brand ? ((Brand)cmbBrand.SelectedItem).BrandId : 0;

            _loading = true;
            _brands = RequisitionService.GetBrands();

            List<Brand> items = new List<Brand> { new Brand { BrandId = 0, BrandName = "All brands" } };
            items.AddRange(_brands);

            cmbBrand.Items.Clear();
            foreach (Brand b in items) cmbBrand.Items.Add(b);

            Brand pick = items.FirstOrDefault(b => b.BrandId == keepBrand) ?? items[0];
            cmbBrand.SelectedItem = pick;
            _loading = false;
        }

        private void ApplyFilter()
        {
            ApplyFilter(Selected != null ? Selected.ProductId : 0);
        }

        private void ApplyFilter(int keepProductId)
        {
            if (_loading) return;

            IEnumerable<CatalogRow> q = _all;

            Brand brand = cmbBrand.SelectedItem as Brand;
            if (brand != null && brand.BrandId != 0) q = q.Where(r => r.BrandId == brand.BrandId);

            string status = cmbStatus.SelectedItem as string;
            if (!string.IsNullOrEmpty(status) && status != "All") q = q.Where(r => r.StockStatus == status);

            string term = txtSearch.Text.Trim();
            if (term.Length > 0)
                q = q.Where(r => r.Product.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                              || r.Sku.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            List<CatalogRow> list = q.ToList();
            grid.DataSource = list;
            lblCount.Text = list.Count + " of " + _all.Count + " products";

            // keep the same product selected after a refresh / filter
            if (keepProductId != 0)
            {
                foreach (DataGridViewRow r in grid.Rows)
                {
                    CatalogRow item = r.DataBoundItem as CatalogRow;
                    if (item != null && item.ProductId == keepProductId)
                    {
                        r.Selected = true;
                        grid.CurrentCell = r.Cells[0];
                        break;
                    }
                }
            }

            SetButtons();
        }

        private void UpdateBanner()
        {
            int low = _all.Count(r => r.StockStatus == "LOW");
            int out0 = _all.Count(r => r.StockStatus == "OUT");

            if (low + out0 == 0)
            {
                root.RowStyles[2].Height = 0f;
                lblBanner.Text = string.Empty;
                return;
            }

            lblBanner.Text = string.Format("Low stock alert:  {0} item(s) at or below the reorder level, {1} item(s) out of stock. Request restocking before they run out.", low, out0);
            root.RowStyles[2].Height = 38f;
        }

        private void SetButtons()
        {
            bool has = Selected != null;
            btnEdit.Enabled = _canEdit && has;
            btnStock.Enabled = _canEdit && has;
            btnHistory.Enabled = has;
            btnAdd.Enabled = _canEdit;
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnAdd(object sender, EventArgs e)
        {
            using (ProductDialog dlg = new ProductDialog(_brands, null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int id = InventoryService.SaveProduct(0, dlg.BrandId, dlg.Sku, dlg.ProductNameValue,
                        dlg.SizeValue, dlg.PriceValue, dlg.ReorderLevelValue, dlg.OpeningStock, _user);
                    LoadDataAndSelect(id);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            CatalogRow sel = Selected;
            if (sel == null) return;

            using (ProductDialog dlg = new ProductDialog(_brands, sel))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    InventoryService.SaveProduct(sel.ProductId, dlg.BrandId, dlg.Sku, dlg.ProductNameValue,
                        dlg.SizeValue, dlg.PriceValue, dlg.ReorderLevelValue, 0, _user);
                    LoadDataAndSelect(sel.ProductId);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnStock(object sender, EventArgs e)
        {
            CatalogRow sel = Selected;
            if (sel == null || !_canEdit) return;

            using (StockDialog dlg = new StockDialog(sel))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int newStock = InventoryService.AdjustStock(sel.ProductId, dlg.QtyChange, dlg.Reason, _user);
                    LoadDataAndSelect(sel.ProductId);
                    MessageBox.Show(this,
                        string.Format("{0} (US {1}) now has {2} in stock.", sel.Product, sel.Size, newStock),
                        "Stock updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnHistory(object sender, EventArgs e)
        {
            CatalogRow sel = Selected;
            if (sel == null) return;

            using (HistoryForm f = new HistoryForm(sel)) f.ShowDialog(this);
        }

        private void LoadDataAndSelect(int productId)
        {
            _all = InventoryService.GetStock();
            LoadBrands();
            UpdateBanner();
            ApplyFilter(productId);
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ======================================================================
    //  Add / edit product
    // ======================================================================
    public class ProductDialog : Form
    {
        private readonly ComboBox _brand = new ComboBox();
        private readonly TextBox _sku = new TextBox();
        private readonly TextBox _name = new TextBox();
        private readonly NumericUpDown _size = new NumericUpDown();
        private readonly NumericUpDown _price = new NumericUpDown();
        private readonly NumericUpDown _reorder = new NumericUpDown();
        private readonly NumericUpDown _opening = new NumericUpDown();

        public int BrandId { get { return ((Brand)_brand.SelectedItem).BrandId; } }
        public string Sku { get { return _sku.Text.Trim(); } }
        public string ProductNameValue { get { return _name.Text.Trim(); } }
        public int SizeValue { get { return (int)_size.Value; } }
        public decimal PriceValue { get { return _price.Value; } }
        public int ReorderLevelValue { get { return (int)_reorder.Value; } }
        public int OpeningStock { get { return (int)_opening.Value; } }

        public ProductDialog(List<Brand> brands, CatalogRow existing)
        {
            bool isNew = existing == null;

            Text = isNew ? "Add product" : "Edit product";
            ClientSize = new Size(480, isNew ? 440 : 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            int y = 26;
            AddRow("Brand", _brand, ref y);
            _brand.DropDownStyle = ComboBoxStyle.DropDownList;
            _brand.DisplayMember = "BrandName";
            foreach (Brand b in brands) _brand.Items.Add(b);

            AddRow("SKU", _sku, ref y);
            _sku.MaxLength = 30;
            AddRow("Product name", _name, ref y);
            _name.MaxLength = 100;

            AddRow("Size (US)", _size, ref y);
            _size.Minimum = 1; _size.Maximum = 60; _size.Value = 8;

            AddRow("Price (PHP)", _price, ref y);
            _price.DecimalPlaces = 2; _price.Minimum = 0; _price.Maximum = 999999; _price.Increment = 50;

            AddRow("Reorder level", _reorder, ref y);
            _reorder.Minimum = 0; _reorder.Maximum = 9999; _reorder.Value = 5;

            if (isNew)
            {
                AddRow("Opening stock", _opening, ref y);
                _opening.Minimum = 0; _opening.Maximum = 99999; _opening.Value = 0;
            }
            else
            {
                _brand.SelectedItem = brands.FirstOrDefault(b => b.BrandId == existing.BrandId);
                _sku.Text = existing.Sku;
                _name.Text = existing.Product;
                _size.Value = Math.Max(_size.Minimum, Math.Min(_size.Maximum, existing.Size));
                _price.Value = Math.Max(_price.Minimum, Math.Min(_price.Maximum, existing.Price));
                _reorder.Value = Math.Max(_reorder.Minimum, Math.Min(_reorder.Maximum, existing.ReorderLevel));
            }
            if (_brand.SelectedItem == null && _brand.Items.Count > 0) _brand.SelectedIndex = 0;

            if (!isNew)
            {
                Label note = new Label
                {
                    Text = "To change the stock quantity, use STOCK IN / ADJUST.",
                    Font = Theme.FontSmall,
                    ForeColor = Theme.Muted,
                    AutoSize = true,
                    Location = new Point(20, y)
                };
                Controls.Add(note);
                y += 28;
            }

            Button save = Theme.MakeButton("SAVE", 130, true, OnSave);
            save.Location = new Point(330, ClientSize.Height - 58);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(210, ClientSize.Height - 58);

            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void AddRow(string label, Control input, ref int y)
        {
            Controls.Add(new Label { Text = label, Font = Theme.FontBold, AutoSize = true, Location = new Point(20, y + 4) });
            input.Location = new Point(170, y);
            input.Width = 290;
            if (input is TextBox) ((TextBox)input).BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(input);
            y += 44;
        }

        private void OnSave(object sender, EventArgs e)
        {
            string problem = null;
            if (_brand.SelectedItem == null) problem = "Please choose a brand.";
            else if (Sku.Length == 0) problem = "SKU is required.";
            else if (ProductNameValue.Length == 0) problem = "Product name is required.";

            if (problem != null)
            {
                MessageBox.Show(this, problem, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // ======================================================================
    //  Stock in / correction
    // ======================================================================
    public class StockDialog : Form
    {
        private readonly RadioButton _in = new RadioButton();
        private readonly RadioButton _out = new RadioButton();
        private readonly NumericUpDown _qty = new NumericUpDown();
        private readonly TextBox _reason = new TextBox();
        private readonly CatalogRow _row;

        public int QtyChange
        {
            get { return _in.Checked ? (int)_qty.Value : -(int)_qty.Value; }
        }

        public string Reason { get { return _reason.Text.Trim(); } }

        public StockDialog(CatalogRow row)
        {
            _row = row;

            Text = "Stock in / adjust";
            ClientSize = new Size(480, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label
            {
                Text = string.Format("{0}  -  {1} (US {2})\r\nCurrent stock: {3}     Reorder at: {4}",
                    row.Brand, row.Product, row.Size, row.Stock, row.ReorderLevel),
                Font = Theme.FontBold,
                AutoSize = false,
                Location = new Point(20, 24),
                Size = new Size(440, 48)
            });

            _in.Text = "Stock in (items received / added)";
            _in.Checked = true;
            _in.AutoSize = true;
            _in.Location = new Point(20, 86);
            _in.CheckedChanged += (s, e) => SuggestReason();

            _out.Text = "Remove (correction, damaged, lost)";
            _out.AutoSize = true;
            _out.Location = new Point(20, 116);

            Controls.Add(_in);
            Controls.Add(_out);

            Controls.Add(new Label { Text = "Quantity", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 164) });
            _qty.Minimum = 1;
            _qty.Maximum = 99999;
            _qty.Value = 1;
            _qty.Location = new Point(170, 160);
            _qty.Width = 140;
            Controls.Add(_qty);

            Controls.Add(new Label { Text = "Reason", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 214) });
            _reason.MaxLength = 100;
            _reason.BorderStyle = BorderStyle.FixedSingle;
            _reason.Location = new Point(170, 210);
            _reason.Width = 290;
            Controls.Add(_reason);
            SuggestReason();

            Button ok = Theme.MakeButton("CONFIRM", 130, true, OnOk);
            ok.Location = new Point(330, 330);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(210, 330);

            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void SuggestReason()
        {
            string a = "Stock received";
            string b = "Stock correction";
            string current = _reason.Text.Trim();
            if (current.Length == 0 || current == a || current == b)
                _reason.Text = _in.Checked ? a : b;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (Reason.Length == 0)
            {
                MessageBox.Show(this, "Please enter a reason.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_out.Checked && (int)_qty.Value > _row.Stock)
            {
                MessageBox.Show(this, "You cannot remove more than the current stock (" + _row.Stock + ").",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // ======================================================================
    //  Stock history of one product
    // ======================================================================
    public class HistoryForm : Form
    {
        public HistoryForm(CatalogRow row)
        {
            Text = "Stock history";
            ClientSize = new Size(900, 520);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            Panel card = Theme.MakeCard(new Padding(10));
            DataGridView grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 20, "g");
            Theme.AddColumn(grid, "colChange", "CHANGE", "QtyChange", 10, "+#;-#;0", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colAfter", "STOCK AFTER", "StockAfter", 12, null, true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "colReason", "REASON", "Reason", 36);
            Theme.AddColumn(grid, "colBy", "BY", "ByUser", 22);

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle(string.Format("{0} - {1} (US {2})", row.Brand, row.Product, row.Size)));
            Controls.Add(card);

            Shown += (s, e) =>
            {
                try { grid.DataSource = InventoryService.GetMovements(row.ProductId); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
