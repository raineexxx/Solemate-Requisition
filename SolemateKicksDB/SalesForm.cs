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
    /// Sales screen. Cashier / Manager ring up a sale; the stock is deducted in the database at the
    /// moment the sale is completed (real-time stock update). The Head can only view.
    /// </summary>
    public class SalesForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canSell;

        private List<CatalogRow> _all = new List<CatalogRow>();
        private readonly BindingList<SaleLine> _cart = new BindingList<SaleLine>();
        private bool _loading;

        private DataGridView gridCatalog, gridCart;
        private ComboBox cmbBrand;
        private TextBox txtSearch, txtRemarks;
        private Label lblTotal, lblCount;
        private Button btnAdd, btnRemove, btnClear, btnComplete;

        public SalesForm()
        {
            _canSell = _user != null && (_user.Role == Roles.Cashier || _user.Role == Roles.Manager);

            Text = "Solemate - Sales";
            ClientSize = new Size(1240, 740);
            MinimumSize = new Size(1040, 640);
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
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(BuildHeader(), 0, 0);

            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(4) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.Controls.Add(BuildCatalogCard(), 0, 0);
            content.Controls.Add(BuildCartCard(), 1, 0);
            root.Controls.Add(content, 0, 1);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Sales", "Every sale updates the stock immediately");

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

        private Control BuildCatalogCard()
        {
            Panel card = Theme.MakeCard(new Padding(10));
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            Label title = Theme.MakeSectionTitle("Products");
            title.Dock = DockStyle.Fill;
            t.Controls.Add(title, 0, 0);

            Panel filters = new Panel { Dock = DockStyle.Fill };
            filters.Controls.Add(new Label { Text = "Brand:", Font = Theme.FontBold, AutoSize = true, Location = new Point(0, 11) });
            cmbBrand = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.FontBody, Location = new Point(56, 7), Width = 170, DisplayMember = "BrandName" };
            cmbBrand.SelectedIndexChanged += (s, e) => ApplyFilter();
            filters.Controls.Add(cmbBrand);
            filters.Controls.Add(new Label { Text = "Search:", Font = Theme.FontBold, AutoSize = true, Location = new Point(244, 11) });
            txtSearch = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, Location = new Point(306, 8), Width = 200 };
            txtSearch.TextChanged += (s, e) => ApplyFilter();
            filters.Controls.Add(txtSearch);
            t.Controls.Add(filters, 0, 1);

            gridCatalog = new DataGridView();
            Theme.StyleGrid(gridCatalog);
            gridCatalog.ReadOnly = true;
            Theme.AddColumn(gridCatalog, "colSku", "SKU", "Sku", 15f);
            Theme.AddColumn(gridCatalog, "colBrand", "BRAND", "Brand", 14f);
            Theme.AddColumn(gridCatalog, "colProduct", "PRODUCT", "Product", 24f);
            Theme.AddColumn(gridCatalog, "colSize", "SIZE", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCatalog, "colPrice", "PRICE", "Price", 14f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridCatalog, "colStock", "AVAILABLE", "Stock", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCatalog, "colStatus", "STATUS", "StockStatus", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            gridCatalog.CellFormatting += OnCatalogFormatting;
            gridCatalog.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OnAdd(this, EventArgs.Empty); };
            t.Controls.Add(gridCatalog, 0, 2);

            Panel bottom = new Panel { Dock = DockStyle.Fill };
            btnAdd = Theme.MakeButton("ADD TO SALE", 140, true, OnAdd);
            btnAdd.Location = new Point(0, 8);
            btnAdd.Enabled = _canSell;
            lblCount = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FontSmall, Location = new Point(160, 18) };
            bottom.Controls.Add(btnAdd);
            bottom.Controls.Add(lblCount);
            t.Controls.Add(bottom, 0, 3);

            card.Controls.Add(t);
            return card;
        }

        private Control BuildCartCard()
        {
            Panel card = Theme.MakeCard(new Padding(10));
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            Label title = Theme.MakeSectionTitle("Current sale");
            title.Dock = DockStyle.Fill;
            t.Controls.Add(title, 0, 0);

            gridCart = new DataGridView();
            Theme.StyleGrid(gridCart);
            gridCart.EditMode = DataGridViewEditMode.EditOnEnter;
            Theme.AddColumn(gridCart, "cSku", "SKU", "Sku", 17f);
            Theme.AddColumn(gridCart, "cProduct", "PRODUCT", "Product", 27f);
            Theme.AddColumn(gridCart, "cSize", "SIZE", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCart, "cPrice", "PRICE", "Price", 15f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridCart, "cQty", "QTY", "Quantity", 9f, null, false, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCart, "cTotal", "TOTAL", "LineTotal", 16f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            gridCart.DataSource = _cart;
            gridCart.CellEndEdit += OnCartEdited;
            gridCart.DataError += (s, e) => { e.ThrowException = false; e.Cancel = true; };
            t.Controls.Add(gridCart, 0, 1);

            Panel totals = new Panel { Dock = DockStyle.Fill };
            totals.Controls.Add(new Label { Text = "Note (optional):", Font = Theme.FontSmall, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(0, 8) });
            txtRemarks = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, Location = new Point(110, 4), Width = 240, MaxLength = 200 };
            totals.Controls.Add(txtRemarks);
            lblTotal = new Label { Text = "TOTAL: PHP 0.00", Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = Theme.Red, AutoSize = true, Location = new Point(0, 44) };
            totals.Controls.Add(lblTotal);
            t.Controls.Add(totals, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnComplete = Theme.MakeButton("COMPLETE SALE", 160, true, OnComplete);
            btnRemove = Theme.MakeButton("REMOVE", 100, false, OnRemove);
            btnClear = Theme.MakeButton("CLEAR", 90, false, OnClear);
            Button btnHistory = Theme.MakeButton("SALES HISTORY", 150, false, OnHistory);
            btnComplete.Enabled = btnRemove.Enabled = btnClear.Enabled = _canSell;
            buttons.Controls.Add(btnComplete);
            buttons.Controls.Add(btnRemove);
            buttons.Controls.Add(btnClear);
            buttons.Controls.Add(btnHistory);
            t.Controls.Add(buttons, 0, 3);

            card.Controls.Add(t);
            return card;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private void LoadData()
        {
            try
            {
                _loading = true;
                _all = SalesService.GetCatalog();

                List<Brand> brands = RequisitionService.GetBrands();
                cmbBrand.Items.Clear();
                cmbBrand.Items.Add(new Brand { BrandId = 0, BrandName = "All brands" });
                foreach (Brand b in brands) cmbBrand.Items.Add(b);
                cmbBrand.SelectedIndex = 0;
                _loading = false;

                ApplyFilter();
                if (!_canSell)
                    MessageBox.Show(this, "Only cashiers and managers can record sales. You can open SALES HISTORY to view them.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _loading = false;
                ShowError(ex);
            }
        }

        private void ApplyFilter()
        {
            if (_loading) return;
            int brandId = 0;
            Brand sel = cmbBrand.SelectedItem as Brand;
            if (sel != null) brandId = sel.BrandId;
            string text = (txtSearch.Text ?? string.Empty).Trim();

            List<CatalogRow> rows = _all.Where(r =>
                (brandId == 0 || r.BrandId == brandId) &&
                (text.Length == 0 ||
                 r.Sku.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 r.Product.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 r.Brand.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            gridCatalog.DataSource = rows;
            lblCount.Text = rows.Count + " product(s)   |   double-click a row to add it";
        }

        private void OnCatalogFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || gridCatalog.Columns[e.ColumnIndex].Name != "colStatus") return;
            string v = Convert.ToString(e.Value);
            e.CellStyle.ForeColor = v == "OUT" ? Theme.Red : (v == "LOW" ? Theme.Warn : Theme.Ok);
            e.CellStyle.Font = Theme.FontBold;
        }

        // ==================================================================
        //  Actions
        // ==================================================================
        private void OnAdd(object sender, EventArgs e)
        {
            if (!_canSell) return;
            CatalogRow row = gridCatalog.CurrentRow == null ? null : gridCatalog.CurrentRow.DataBoundItem as CatalogRow;
            if (row == null) return;

            SaleLine line = _cart.FirstOrDefault(l => l.ProductId == row.ProductId);
            int inCart = line == null ? 0 : line.Quantity;
            if (inCart + 1 > row.Stock)
            {
                MessageBox.Show(this, row.Sku + " has only " + row.Stock + " pair(s) available.", "Solemate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (line == null)
                _cart.Add(new SaleLine
                {
                    ProductId = row.ProductId,
                    Sku = row.Sku,
                    Brand = row.Brand,
                    Product = row.Brand + " " + row.Product,
                    Size = row.Size,
                    Price = row.Price,
                    Available = row.Stock,
                    Quantity = 1
                });
            else
                line.Quantity++;

            RefreshCart();
        }

        private void OnCartEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _cart.Count) return;
            SaleLine line = _cart[e.RowIndex];
            if (line.Quantity < 1)
            {
                line.Quantity = 1;
            }
            else if (line.Quantity > line.Available)
            {
                MessageBox.Show(this, line.Sku + " has only " + line.Available + " pair(s) available.", "Solemate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                line.Quantity = line.Available;
            }
            RefreshCart();
        }

        private void OnRemove(object sender, EventArgs e)
        {
            if (gridCart.CurrentRow == null || gridCart.CurrentRow.Index >= _cart.Count) return;
            _cart.RemoveAt(gridCart.CurrentRow.Index);
            RefreshCart();
        }

        private void OnClear(object sender, EventArgs e)
        {
            _cart.Clear();
            RefreshCart();
        }

        private void RefreshCart()
        {
            gridCart.Invalidate();
            lblTotal.Text = "TOTAL: PHP " + _cart.Sum(l => l.LineTotal).ToString("N2");
        }

        private void OnComplete(object sender, EventArgs e)
        {
            if (_cart.Count == 0)
            {
                MessageBox.Show(this, "Add at least one item to the sale.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            decimal total = _cart.Sum(l => l.LineTotal);
            int pairs = _cart.Sum(l => l.Quantity);
            if (MessageBox.Show(this, string.Format("Complete this sale of {0} pair(s) for PHP {1:N2}?\r\n\r\nStock will be deducted right away.", pairs, total),
                    "Complete sale", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            List<SaleLine> sold = _cart.ToList();
            try
            {
                SaleResult result = SalesService.Create(sold, txtRemarks.Text, _user);

                _cart.Clear();
                txtRemarks.Clear();
                RefreshCart();
                _all = SalesService.GetCatalog();
                ApplyFilter();

                string msg = "Sale " + result.SaleNo + " recorded.\r\nTotal: PHP " + result.Total.ToString("N2") + "\r\nStock updated.";
                List<string> low = new List<string>();
                foreach (SaleLine l in sold)
                {
                    CatalogRow now = _all.FirstOrDefault(c => c.ProductId == l.ProductId);
                    if (now != null && now.Stock <= now.ReorderLevel)
                        low.Add(now.Sku + " - " + now.Brand + " " + now.Product + " (US " + now.Size + "): " + now.Stock + " left");
                }
                if (low.Count > 0)
                    msg += "\r\n\r\nLOW STOCK - request restocking before these run out:\r\n" + string.Join("\r\n", low);

                MessageBox.Show(this, msg, "Sale completed", MessageBoxButtons.OK,
                    low.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                try { _all = SalesService.GetCatalog(); ApplyFilter(); } catch { }   // stock may have changed
            }
        }

        private void OnHistory(object sender, EventArgs e)
        {
            using (SalesHistoryForm f = new SalesHistoryForm())
            {
                f.ShowDialog(this);
            }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>List of recorded sales with the items of the selected sale.</summary>
    public class SalesHistoryForm : Form
    {
        private DataGridView gridSales, gridItems;

        public SalesHistoryForm()
        {
            Text = "Solemate - Sales history";
            ClientSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

            Label l1 = Theme.MakeSectionTitle("Sales");
            l1.Dock = DockStyle.Fill;
            t.Controls.Add(l1, 0, 0);

            gridSales = new DataGridView();
            Theme.StyleGrid(gridSales);
            gridSales.ReadOnly = true;
            Theme.AddColumn(gridSales, "sNo", "SALE NO", "SaleNo", 18f);
            Theme.AddColumn(gridSales, "sDate", "DATE", "DateCreated", 20f, "yyyy-MM-dd HH:mm");
            Theme.AddColumn(gridSales, "sCashier", "CASHIER", "Cashier", 20f);
            Theme.AddColumn(gridSales, "sBranch", "BRANCH", "Branch", 14f);
            Theme.AddColumn(gridSales, "sQty", "PAIRS", "Qty", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridSales, "sTotal", "TOTAL (PHP)", "Total", 16f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            gridSales.SelectionChanged += (s, e) => LoadItems();
            t.Controls.Add(gridSales, 0, 1);

            Label l2 = Theme.MakeSectionTitle("Items of the selected sale");
            l2.Dock = DockStyle.Fill;
            t.Controls.Add(l2, 0, 2);

            gridItems = new DataGridView();
            Theme.StyleGrid(gridItems);
            gridItems.ReadOnly = true;
            Theme.AddColumn(gridItems, "iSku", "SKU", "Sku", 16f);
            Theme.AddColumn(gridItems, "iBrand", "BRAND", "Brand", 14f);
            Theme.AddColumn(gridItems, "iProduct", "PRODUCT", "Product", 26f);
            Theme.AddColumn(gridItems, "iSize", "SIZE", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iQty", "QTY", "Quantity", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "iPrice", "UNIT PRICE", "UnitPrice", 14f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridItems, "iTotal", "TOTAL", "LineTotal", 14f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            t.Controls.Add(gridItems, 0, 3);

            Controls.Add(t);
            Shown += (s, e) => LoadSales();
        }

        private void LoadSales()
        {
            try { gridSales.DataSource = SalesService.GetRecent(Session.CurrentUser); LoadItems(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void LoadItems()
        {
            SaleRow row = gridSales.CurrentRow == null ? null : gridSales.CurrentRow.DataBoundItem as SaleRow;
            try { gridItems.DataSource = row == null ? new List<SaleItemRow>() : SalesService.GetItems(row.SaleId); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
