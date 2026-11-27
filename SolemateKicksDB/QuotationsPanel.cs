using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// QUOTATIONS tab: the unit cost each supplier quoted for each product, plus each supplier's
    /// country, payment terms and lead time. New POs are priced from these quotations (lowest price wins).
    /// Head / Manager can edit; everyone else can view.
    /// </summary>
    public class QuotationsPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canManage;

        private DataGridView grid;
        private ComboBox cmbSupplier;
        private Label lblInfo;
        private List<SupplierRow> _suppliers = new List<SupplierRow>();
        private bool _loading;

        public QuotationsPanel()
        {
            _canManage = _user != null && (_user.Role == Roles.Head || _user.Role == Roles.Manager);

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.White,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 9, 0, 0),
                Margin = new Padding(0)
            };

            Label lbl = new Label { Text = "Supplier:", Font = Theme.FontBold, AutoSize = true, Margin = new Padding(4, 9, 4, 0) };
            cmbSupplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.FontBody, Width = 280, Margin = new Padding(4, 5, 12, 0) };
            cmbSupplier.SelectedIndexChanged += (s, e) => { if (!_loading) LoadQuotes(); };

            Button btnAdd = Theme.MakeButton("ADD / UPDATE QUOTE", 200, true, OnAdd);
            Button btnDelete = Theme.MakeButton("DELETE QUOTE", 140, false, OnDelete);
            Button btnTerms = Theme.MakeButton("SUPPLIER TERMS", 160, false, OnTerms);
            Button btnHistory = Theme.MakeButton("ORDER HISTORY", 160, false, OnHistory);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadData());

            flow.Controls.Add(lbl);
            flow.Controls.Add(cmbSupplier);
            if (_canManage)
            {
                flow.Controls.Add(btnAdd);
                flow.Controls.Add(btnDelete);
                flow.Controls.Add(btnTerms);
            }
            flow.Controls.Add(btnHistory);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls)
                if (c is Button) c.Margin = new Padding(4, 0, 4, 0);
            root.Controls.Add(flow, 0, 0);

            Panel card = Theme.MakeCard(new Padding(10));
            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.ReadOnly = true;
            Theme.AddColumn(grid, "qSup", "SUPPLIER", "SupplierName", 24f);
            Theme.AddColumn(grid, "qSku", "SKU", "Sku", 12f);
            Theme.AddColumn(grid, "qBrand", "BRAND", "Brand", 11f);
            Theme.AddColumn(grid, "qProd", "PRODUCT", "Product", 18f);
            Theme.AddColumn(grid, "qSize", "SIZE", "Size", 6f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "qSell", "SELLING PRICE", "SellingPrice", 11f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "qCost", "QUOTED COST", "UnitCost", 11f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(grid, "qDate", "QUOTED ON", "QuotedDate", 12f, "MMM dd, yyyy");
            grid.SelectionChanged += (s, e) => UpdateInfo();

            lblInfo = new Label { Dock = DockStyle.Bottom, Height = 28, ForeColor = Theme.Muted, Font = Theme.FontSmall, TextAlign = ContentAlignment.MiddleLeft };

            card.Controls.Add(grid);
            card.Controls.Add(lblInfo);
            card.Controls.Add(Theme.MakeSectionTitle("Supplier quotations (unit cost per pair)"));
            root.Controls.Add(card, 0, 1);

            Controls.Add(root);
        }

        // ==================================================================
        //  Data
        // ==================================================================
        public void LoadData()
        {
            try
            {
                _loading = true;
                int keep = SelectedSupplierId();
                _suppliers = PurchasingService.GetSuppliers(true);

                cmbSupplier.Items.Clear();
                cmbSupplier.Items.Add(new SupplierRow { SupplierId = 0, SupplierName = "-- Select Supplier --" });
                foreach (SupplierRow s in _suppliers) cmbSupplier.Items.Add(s);

                cmbSupplier.SelectedIndex = 0;
                for (int i = 0; i < cmbSupplier.Items.Count; i++)
                    if (((SupplierRow)cmbSupplier.Items[i]).SupplierId == keep) { cmbSupplier.SelectedIndex = i; break; }
                _loading = false;

                LoadQuotes();
            }
            catch (Exception ex) { _loading = false; ShowError(ex); }
        }

        private int SelectedSupplierId()
        {
            SupplierRow s = cmbSupplier.SelectedItem as SupplierRow;
            return s == null ? 0 : s.SupplierId;
        }

        private void LoadQuotes()
        {
            try
            {
                grid.DataSource = PurchasingService.GetQuotations(SelectedSupplierId());
                UpdateInfo();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private QuotationRow SelectedQuote()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as QuotationRow;
        }

        private void UpdateInfo()
        {
            SupplierRow s = cmbSupplier.SelectedItem as SupplierRow;
            if (s == null || s.SupplierId == 0) { lblInfo.Text = grid.Rows.Count + " quotation(s)"; return; }

            SupplierRow full = _suppliers.FirstOrDefault(x => x.SupplierId == s.SupplierId);
            if (full == null) { lblInfo.Text = ""; return; }
            lblInfo.Text = full.SupplierName + "   |   Country: " + (string.IsNullOrEmpty(full.Country) ? "-" : full.Country) +
                           "   |   Payment terms: " + (string.IsNullOrEmpty(full.PaymentTerms) ? "-" : full.PaymentTerms) +
                           "   |   Lead time: " + (full.LeadTimeDays > 0 ? full.LeadTimeDays + " days" : "-") +
                           "   |   " + grid.Rows.Count + " quotation(s)";
        }

        // ==================================================================
        //  Actions
        // ==================================================================
        private void OnAdd(object sender, EventArgs e)
        {
            try
            {
                if (_suppliers.Count == 0)
                {
                    MessageBox.Show(this, "Add a supplier in the SUPPLIERS tab first.", "Solemate");
                    return;
                }
                List<CatalogRow> products = SalesService.GetCatalog();
                QuotationRow sel = SelectedQuote();

                using (QuoteDialog dlg = new QuoteDialog(_suppliers, products,
                           sel != null ? sel.SupplierId : SelectedSupplierId(), sel != null ? sel.ProductId : 0,
                           sel != null ? sel.UnitCost : 0m))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    PurchasingService.SaveQuotation(dlg.SupplierId, dlg.ProductId, dlg.UnitCost, _user);
                    LoadQuotes();
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            QuotationRow sel = SelectedQuote();
            if (sel == null) return;
            if (MessageBox.Show(this, "Delete the quotation of " + sel.SupplierName + " for " + sel.Sku + "?", "Delete quotation",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                PurchasingService.DeleteQuotation(sel.SupplierId, sel.ProductId, _user);
                LoadQuotes();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnHistory(object sender, EventArgs e)
        {
            SupplierRow s = cmbSupplier.SelectedItem as SupplierRow;
            if (s == null || s.SupplierId == 0)
            {
                MessageBox.Show(this, "Choose one supplier in the list first.", "Solemate");
                return;
            }
            try
            {
                List<PurchaseOrderRow> orders = PurchasingService.GetPurchaseOrders()
                    .Where(o => o.Supplier == s.SupplierName).ToList();
                using (SupplierHistoryDialog dlg = new SupplierHistoryDialog(s.SupplierName, orders))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnTerms(object sender, EventArgs e)
        {
            SupplierRow s = cmbSupplier.SelectedItem as SupplierRow;
            if (s == null || s.SupplierId == 0)
            {
                MessageBox.Show(this, "Choose one supplier in the list first.", "Solemate");
                return;
            }
            SupplierRow full = _suppliers.FirstOrDefault(x => x.SupplierId == s.SupplierId);
            if (full == null) return;

            using (TermsDialog dlg = new TermsDialog(full))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    PurchasingService.SaveSupplierTerms(full.SupplierId, dlg.Country, dlg.PaymentTerms, dlg.LeadTimeDays, _user);
                    LoadData();
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Order history of one supplier (all purchase orders placed with it).</summary>
    public class SupplierHistoryDialog : Form
    {
        public SupplierHistoryDialog(string supplier, List<PurchaseOrderRow> orders)
        {
            Text = "Order history - " + supplier;
            ClientSize = new Size(900, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            DataGridView g = new DataGridView();
            Theme.StyleGrid(g);
            g.ReadOnly = true;
            Theme.AddColumn(g, "hPo", "PO NO.", "PoNo", 16f);
            Theme.AddColumn(g, "hDate", "DATE", "DateCreated", 14f, "MMM dd, yyyy");
            Theme.AddColumn(g, "hReq", "REQUISITION", "RequisitionRef", 16f);
            Theme.AddColumn(g, "hStatus", "STATUS", "Status", 12f);
            Theme.AddColumn(g, "hTerms", "PAYMENT TERMS", "PaymentTerms", 18f);
            Theme.AddColumn(g, "hExp", "EXPECTED", "ExpectedDate", 12f, "MMM dd, yyyy");
            Theme.AddColumn(g, "hTotal", "TOTAL (PHP)", "Total", 12f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            g.DataSource = orders;

            Label info = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "  " + orders.Count + " order(s)   |   total ordered: PHP " + orders.Where(o => o.Status != "Cancelled").Sum(o => o.Total).ToString("N2")
            };

            Controls.Add(g);
            Controls.Add(info);
        }
    }

    /// <summary>Add or change one quotation (supplier + product + unit cost).</summary>
    public class QuoteDialog : Form
    {
        private class ProductChoice
        {
            public CatalogRow Row;
            public override string ToString()
            {
                return Row.Sku + "  -  " + Row.Brand + " " + Row.Product + "  (US " + Row.Size + ")";
            }
        }

        private readonly ComboBox _sup = new ComboBox();
        private readonly ComboBox _prod = new ComboBox();
        private readonly TextBox _cost = new TextBox();

        public int SupplierId { get { return ((SupplierRow)_sup.SelectedItem).SupplierId; } }
        public int ProductId { get { return ((ProductChoice)_prod.SelectedItem).Row.ProductId; } }
        public decimal UnitCost { get; private set; }

        public QuoteDialog(List<SupplierRow> suppliers, List<CatalogRow> products, int supplierId, int productId, decimal cost)
        {
            Text = "Supplier quotation";
            ClientSize = new Size(560, 270);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label { Text = "Supplier", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 34) });
            _sup.DropDownStyle = ComboBoxStyle.DropDownList;
            _sup.Location = new Point(130, 30);
            _sup.Width = 400;
            foreach (SupplierRow s in suppliers) _sup.Items.Add(s);
            _sup.SelectedIndex = 0;
            for (int i = 0; i < _sup.Items.Count; i++)
                if (((SupplierRow)_sup.Items[i]).SupplierId == supplierId) { _sup.SelectedIndex = i; break; }
            Controls.Add(_sup);

            Controls.Add(new Label { Text = "Product", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 84) });
            _prod.DropDownStyle = ComboBoxStyle.DropDownList;
            _prod.Location = new Point(130, 80);
            _prod.Width = 400;
            foreach (CatalogRow r in products) _prod.Items.Add(new ProductChoice { Row = r });
            if (_prod.Items.Count > 0) _prod.SelectedIndex = 0;
            for (int i = 0; i < _prod.Items.Count; i++)
                if (((ProductChoice)_prod.Items[i]).Row.ProductId == productId) { _prod.SelectedIndex = i; break; }
            Controls.Add(_prod);

            Controls.Add(new Label { Text = "Unit cost (PHP)", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 134) });
            _cost.Location = new Point(130, 130);
            _cost.Width = 160;
            _cost.BorderStyle = BorderStyle.FixedSingle;
            _cost.Text = cost > 0 ? cost.ToString("0.00", CultureInfo.CurrentCulture) : "";
            Controls.Add(_cost);

            Controls.Add(new Label
            {
                Text = "The quoted cost is what the supplier charges per pair. New purchase orders use it.",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 172)
            });

            Button save = Theme.MakeButton("SAVE", 120, true, OnSave);
            save.Location = new Point(410, 214);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(290, 214);
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void OnSave(object sender, EventArgs e)
        {
            decimal v;
            if (!decimal.TryParse(_cost.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out v) || v <= 0)
            {
                MessageBox.Show(this, "Enter a unit cost greater than zero.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UnitCost = v;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Country, payment terms and lead time of one supplier.</summary>
    public class TermsDialog : Form
    {
        private readonly TextBox _country = new TextBox();
        private readonly TextBox _terms = new TextBox();
        private readonly TextBox _lead = new TextBox();

        public string Country { get { return _country.Text.Trim(); } }
        public string PaymentTerms { get { return _terms.Text.Trim(); } }
        public int LeadTimeDays { get; private set; }

        public TermsDialog(SupplierRow s)
        {
            Text = "Supplier terms - " + s.SupplierName;
            ClientSize = new Size(540, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            AddRow("Country", _country, s.Country, 34, 200);
            AddRow("Payment terms", _terms, s.PaymentTerms, 84, 360);
            AddRow("Lead time (days)", _lead, s.LeadTimeDays > 0 ? s.LeadTimeDays.ToString() : "", 134, 100);
            _country.MaxLength = 50;
            _terms.MaxLength = 100;

            Controls.Add(new Label
            {
                Text = "The lead time sets the expected delivery date when a purchase order is sent.",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 176)
            });

            Button save = Theme.MakeButton("SAVE", 120, true, OnSave);
            save.Location = new Point(400, 224);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(280, 224);
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void AddRow(string caption, TextBox box, string value, int y, int width)
        {
            Controls.Add(new Label { Text = caption, Font = Theme.FontBold, AutoSize = true, Location = new Point(20, y + 4) });
            box.Location = new Point(160, y);
            box.Width = width;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = value ?? string.Empty;
            Controls.Add(box);
        }

        private void OnSave(object sender, EventArgs e)
        {
            int days = 0;
            if (_lead.Text.Trim().Length > 0 && (!int.TryParse(_lead.Text.Trim(), out days) || days < 0 || days > 365))
            {
                MessageBox.Show(this, "Lead time must be a whole number of days between 0 and 365.", "Solemate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LeadTimeDays = days;
            DialogResult = DialogResult.OK;
        }
    }
}
