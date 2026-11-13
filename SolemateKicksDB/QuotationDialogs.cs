using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    // ======================================================================
    //  New purchase order, step by step (replaces the old "pick any supplier" dialog):
    //    1. pick an approved requisition
    //    2. record the quotation of each supplier (UK and Vietnam)
    //    3. the lowest total is selected; CREATE PO builds the PO from that quotation
    //  Head only (the Head is also the Owner and the purchasing officer).
    // ======================================================================
    public class QuotationsDialog : Form
    {
        private readonly AppUser _user;
        private readonly List<SupplierRow> _suppliers;
        private readonly ComboBox _req = new ComboBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _hint = new Label();
        private Button _btnAdd, _btnCreate;

        public QuotationsDialog(List<ApprovedRequisitionRow> reqs, List<SupplierRow> suppliers, AppUser user)
        {
            _user = user;
            _suppliers = suppliers;

            Text = "New purchase order - supplier quotations";
            ClientSize = new Size(840, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label { Text = "Requisition", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 30) });
            _req.DropDownStyle = ComboBoxStyle.DropDownList;
            _req.Location = new Point(130, 26);
            _req.Width = 480;
            foreach (ApprovedRequisitionRow r in reqs) _req.Items.Add(r);   // label comes from ApprovedRequisitionRow.ToString()
            Controls.Add(_req);

            Controls.Add(new Label
            {
                Text = "Quotations (compare the suppliers; the lowest total is selected)",
                Font = Theme.FontBold,
                AutoSize = true,
                Location = new Point(20, 70)
            });

            Panel gridHost = new Panel { Location = new Point(20, 96), Size = new Size(800, 290) };
            Theme.StyleGrid(_grid);
            Theme.AddColumn(_grid, "colSupplier", "SUPPLIER", "SupplierName", 32);
            Theme.AddColumn(_grid, "colCountry", "COUNTRY", "Country", 14);
            Theme.AddColumn(_grid, "colTerms", "PAYMENT TERMS", "PaymentTerms", 22);
            Theme.AddColumn(_grid, "colTotal", "TOTAL (PHP)", "Total", 16, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(_grid, "colLow", "", "LowestText", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            _grid.Dock = DockStyle.Fill;
            _grid.CellFormatting += OnCellFormatting;
            gridHost.Controls.Add(_grid);
            Controls.Add(gridHost);

            _hint.Font = Theme.FontSmall;
            _hint.ForeColor = Theme.Muted;
            _hint.AutoSize = false;
            _hint.Bounds = new Rectangle(20, 394, 800, 40);
            Controls.Add(_hint);

            _btnAdd = Theme.MakeButton("ADD / UPDATE QUOTATION", 230, false, OnAddQuotation);
            _btnAdd.Location = new Point(20, 460);
            _btnCreate = Theme.MakeButton("CREATE PO", 150, true, OnCreate);
            _btnCreate.Location = new Point(520, 460);
            Button close = Theme.MakeButton("CLOSE", 110, false, null);
            close.DialogResult = DialogResult.Cancel;
            close.Location = new Point(690, 460);

            Controls.Add(_btnAdd);
            Controls.Add(_btnCreate);
            Controls.Add(close);
            CancelButton = close;

            _req.SelectedIndexChanged += delegate { LoadQuotes(); };
            if (_req.Items.Count > 0) _req.SelectedIndex = 0;
        }

        private ApprovedRequisitionRow SelectedReq
        {
            get { return _req.SelectedItem as ApprovedRequisitionRow; }
        }

        private QuotationRow SelectedQuote
        {
            get { return _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as QuotationRow; }
        }

        private void LoadQuotes()
        {
            try
            {
                ApprovedRequisitionRow req = SelectedReq;
                if (req == null) return;

                List<QuotationRow> list = PurchasingService.GetQuotations(req.RequisitionId);
                _grid.DataSource = list;

                // select the lowest quotation by default
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].IsLowest)
                    {
                        _grid.ClearSelection();
                        _grid.Rows[i].Selected = true;
                        _grid.CurrentCell = _grid.Rows[i].Cells[0];
                        break;
                    }
                }

                bool enough = list.Count >= 2;
                _btnCreate.Enabled = enough;
                _hint.Text = enough
                    ? "Lowest total: " + LowestOf(list).SupplierName + ". CREATE PO uses the selected supplier's quoted prices and payment terms."
                    : "Record quotations from at least two suppliers (the UK and Vietnam suppliers) before creating the purchase order.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private static QuotationRow LowestOf(List<QuotationRow> list)
        {
            foreach (QuotationRow q in list) if (q.IsLowest) return q;
            return list[0];
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            QuotationRow row = _grid.Rows[e.RowIndex].DataBoundItem as QuotationRow;
            if (row == null || !row.IsLowest) return;

            e.CellStyle.ForeColor = Theme.Ok;
            e.CellStyle.Font = Theme.FontBold;
        }

        private void OnAddQuotation(object sender, EventArgs e)
        {
            ApprovedRequisitionRow req = SelectedReq;
            if (req == null) return;

            try
            {
                List<QuotationRow> existing = PurchasingService.GetQuotations(req.RequisitionId);
                QuotationRow sel = SelectedQuote;

                using (QuotationEntryDialog dlg = new QuotationEntryDialog(
                    req.RequisitionId, _suppliers, existing, sel != null ? sel.SupplierId : 0))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    PurchasingService.SaveQuotation(req.RequisitionId, dlg.SupplierIdValue,
                        dlg.PaymentTermsValue, dlg.ItemsValue, _user);
                }
                LoadQuotes();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCreate(object sender, EventArgs e)
        {
            ApprovedRequisitionRow req = SelectedReq;
            QuotationRow sel = SelectedQuote;
            if (req == null || sel == null) return;

            if (!sel.IsLowest)
            {
                DialogResult ask = MessageBox.Show(this,
                    "\"" + sel.SupplierName + "\" is not the lowest quotation. The current practice is to choose the lowest price.\r\n\r\nCreate the purchase order for this supplier anyway?",
                    "Not the lowest price", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (ask != DialogResult.Yes) return;
            }

            try
            {
                PurchasingService.CreateOrder(req.RequisitionId, sel.SupplierId, _user);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ======================================================================
    //  Enter (or update) the quotation of one supplier: unit price for every requisition item + payment terms
    // ======================================================================
    public class QuotationEntryDialog : Form
    {
        private readonly int _requisitionId;
        private readonly List<QuotationRow> _existing;
        private readonly ComboBox _supplier = new ComboBox();
        private readonly TextBox _terms = new TextBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _total = new Label();
        private BindingList<QuotationItemRow> _items = new BindingList<QuotationItemRow>();
        private bool _loading;

        public int SupplierIdValue { get { return ((SupplierRow)_supplier.SelectedItem).SupplierId; } }
        public string PaymentTermsValue { get { return _terms.Text.Trim(); } }
        public List<QuotationItemRow> ItemsValue { get { return new List<QuotationItemRow>(_items); } }

        public QuotationEntryDialog(int requisitionId, List<SupplierRow> suppliers, List<QuotationRow> existing, int preselectSupplierId)
        {
            _requisitionId = requisitionId;
            _existing = existing;

            Text = "Supplier quotation";
            ClientSize = new Size(660, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label { Text = "Supplier", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 30) });
            _supplier.DropDownStyle = ComboBoxStyle.DropDownList;
            _supplier.Location = new Point(140, 26);
            _supplier.Width = 500;
            foreach (SupplierRow s in suppliers) _supplier.Items.Add(s);    // label comes from SupplierRow.ToString()
            Controls.Add(_supplier);

            Controls.Add(new Label { Text = "Payment terms", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 72) });
            _terms.MaxLength = 100;
            _terms.BorderStyle = BorderStyle.FixedSingle;
            _terms.Location = new Point(140, 68);
            _terms.Width = 500;
            Controls.Add(_terms);

            Controls.Add(new Label
            {
                Text = "Enter the unit price quoted for each item, in PHP.",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 108)
            });

            Panel gridHost = new Panel { Location = new Point(20, 132), Size = new Size(620, 300) };
            Theme.StyleGrid(_grid);
            _grid.AutoGenerateColumns = false;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.Dock = DockStyle.Fill;

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colItem", HeaderText = "ITEM", DataPropertyName = "ProductName", ReadOnly = true, FillWeight = 44
            });
            DataGridViewTextBoxColumn qty = new DataGridViewTextBoxColumn
            {
                Name = "colQty", HeaderText = "QTY", DataPropertyName = "Quantity", ReadOnly = true, FillWeight = 12
            };
            qty.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Add(qty);
            DataGridViewTextBoxColumn price = new DataGridViewTextBoxColumn
            {
                Name = "colPrice", HeaderText = "UNIT PRICE (PHP)", DataPropertyName = "UnitPrice", ReadOnly = false, FillWeight = 22
            };
            price.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            price.DefaultCellStyle.Format = "N2";
            _grid.Columns.Add(price);
            DataGridViewTextBoxColumn line = new DataGridViewTextBoxColumn
            {
                Name = "colLine", HeaderText = "AMOUNT (PHP)", DataPropertyName = "LineTotal", ReadOnly = true, FillWeight = 22
            };
            line.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            line.DefaultCellStyle.Format = "N2";
            _grid.Columns.Add(line);

            _grid.CellEndEdit += delegate { _grid.Invalidate(); UpdateTotal(); };
            _grid.DataError += OnDataError;
            gridHost.Controls.Add(_grid);
            Controls.Add(gridHost);

            _total.Font = Theme.FontBold;
            _total.AutoSize = false;
            _total.TextAlign = ContentAlignment.MiddleRight;
            _total.Bounds = new Rectangle(340, 440, 300, 28);
            Controls.Add(_total);

            Button save = Theme.MakeButton("SAVE QUOTATION", 170, true, OnSave);
            save.Location = new Point(470, 488);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(350, 488);
            Controls.Add(save);
            Controls.Add(cancel);
            CancelButton = cancel;

            _supplier.SelectedIndexChanged += delegate { LoadItems(); };
            if (_supplier.Items.Count > 0)
            {
                int idx = 0;
                for (int i = 0; i < _supplier.Items.Count; i++)
                    if (((SupplierRow)_supplier.Items[i]).SupplierId == preselectSupplierId) { idx = i; break; }
                _supplier.SelectedIndex = idx;
            }
        }

        // loads the requisition items with the prices this supplier already quoted (if any)
        private void LoadItems()
        {
            if (_loading || _supplier.SelectedItem == null) return;
            _loading = true;
            try
            {
                int supplierId = SupplierIdValue;
                _items = new BindingList<QuotationItemRow>(PurchasingService.GetQuotationItems(_requisitionId, supplierId));
                _grid.DataSource = _items;

                _terms.Text = string.Empty;
                foreach (QuotationRow q in _existing)
                    if (q.SupplierId == supplierId) { _terms.Text = q.PaymentTerms; break; }

                UpdateTotal();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _loading = false; }
        }

        private void UpdateTotal()
        {
            decimal sum = 0;
            foreach (QuotationItemRow it in _items) sum += it.LineTotal;
            _total.Text = "Quotation total: PHP " + sum.ToString("N2");
        }

        private void OnDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            MessageBox.Show(this, "Please enter the unit price as a number, for example 1250.00.",
                "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnSave(object sender, EventArgs e)
        {
            _grid.EndEdit();

            string problem = null;
            if (_supplier.SelectedItem == null)
                problem = "Please choose a supplier.";
            else if (PaymentTermsValue.Length == 0)
                problem = "Payment terms are required (for example: 30 days, 50% advance).";
            else
            {
                foreach (QuotationItemRow it in _items)
                {
                    if (it.UnitPrice <= 0)
                    {
                        problem = "Enter a price greater than zero for \"" + it.ProductName + "\".";
                        break;
                    }
                }
            }

            if (problem != null)
            {
                MessageBox.Show(this, problem, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
