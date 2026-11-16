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

    public class QuotationsDialog : Form
    {
        private readonly AppUser _user;
        private readonly List<SupplierRow> _suppliers;
        private readonly ComboBox _req = new ComboBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _hint = new Label();
        private Button _btnAdd, _btnCreate;
        private readonly bool _canCreate;

        public QuotationsDialog(List<ApprovedRequisitionRow> reqs, List<SupplierRow> suppliers, AppUser user)
        {
            _user = user;
            _suppliers = suppliers;
            _canCreate = user != null && user.Role == Roles.Manager;   // paper: the purchasing officer (Manager) creates the PO; the Head already approved the budget

            Text = "New purchase order - supplier quotations (for the shortage of an approved purchase requisition)";
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

        private ReqQuotationRow SelectedQuote
        {
            get { return _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as ReqQuotationRow; }
        }

        private void LoadQuotes()
        {
            try
            {
                ApprovedRequisitionRow req = SelectedReq;
                if (req == null) return;

                List<ReqQuotationRow> list = PurchasingService.GetRequisitionQuotations(req.RequisitionId);
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
                _btnCreate.Enabled = enough && _canCreate;
                if (!enough)
                    _hint.Text = "Record quotations from at least two suppliers (the UK and Vietnam suppliers) before creating the purchase order.";
                else if (!_canCreate)
                    _hint.Text = "Lowest total: " + LowestOf(list).SupplierName + ". Only the Manager (purchasing officer) can create the purchase order.";
                else
                    _hint.Text = "Lowest total: " + LowestOf(list).SupplierName + ". CREATE PO uses the selected supplier's quoted prices and payment terms. The Head already approved the budget.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private static ReqQuotationRow LowestOf(List<ReqQuotationRow> list)
        {
            foreach (ReqQuotationRow q in list) if (q.IsLowest) return q;
            return list[0];
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            ReqQuotationRow row = _grid.Rows[e.RowIndex].DataBoundItem as ReqQuotationRow;
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
                List<ReqQuotationRow> existing = PurchasingService.GetRequisitionQuotations(req.RequisitionId);
                ReqQuotationRow sel = SelectedQuote;

                using (QuotationEntryDialog dlg = new QuotationEntryDialog(
                    req.RequisitionId, _suppliers, existing, sel != null ? sel.SupplierId : 0))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    PurchasingService.SaveRequisitionQuotation(req.RequisitionId, dlg.SupplierIdValue,
                        dlg.PaymentTermsValue, dlg.ItemsValue, _user);
                }
                LoadQuotes();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCreate(object sender, EventArgs e)
        {
            ApprovedRequisitionRow req = SelectedReq;
            ReqQuotationRow sel = SelectedQuote;
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
    //  New purchase order: the Head picks a supplier, then ticks the items to order from any approved request
    //  and types the quantity of each. One PO = one supplier, and may mix items of several requests.
    //  (The file keeps its old name so the project file does not change.)
    // ======================================================================
    public class NewPoDialog : Form
    {
        private readonly int _requisitionId;
        private readonly List<ReqQuotationRow> _existing;
        private readonly List<SupplierRow> _all;
        private readonly ComboBox _supplier = new ComboBox();
        private readonly ComboBox _filter = new ComboBox();
        private readonly TextBox _terms = new TextBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _total = new Label();
        private BindingList<PendingItemRow> _view;

        public int SupplierIdValue { get { return ((SupplierRow)_supplier.SelectedItem).SupplierId; } }
        public string PaymentTermsValue { get { return _terms.Text.Trim(); } }

        /// <summary>The ticked items with their quantities (across all requests, not only the ones shown).</summary>
        public List<PendingItemRow> SelectedItems
        {
            get { return _all.FindAll(delegate (PendingItemRow r) { return r.Selected && r.OrderQty > 0; }); }
        }

        public QuotationEntryDialog(int requisitionId, List<SupplierRow> suppliers, List<ReqQuotationRow> existing, int preselectSupplierId)
        {
            _requisitionId = requisitionId;
            _existing = existing;
            _all = suppliers;

            Text = "New purchase order";
            ClientSize = new Size(1000, 640);
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
            _supplier.Location = new Point(130, 26);
            _supplier.Width = 400;
            foreach (SupplierRow s in suppliers) _supplier.Items.Add(s);    // label comes from SupplierRow.ToString()
            _supplier.SelectedIndex = 0;
            Controls.Add(_supplier);

            Controls.Add(new Label { Text = "Payment terms", Font = Theme.FontBold, AutoSize = true, Location = new Point(560, 30) });
            _terms.MaxLength = 100;
            _terms.BorderStyle = BorderStyle.FixedSingle;
            _terms.Location = new Point(680, 26);
            _terms.Width = 300;
            Controls.Add(_terms);

            Controls.Add(new Label { Text = "Show request", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 72) });
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Location = new Point(130, 68);
            _filter.Width = 400;
            _filter.Items.Add(AllRequests);
            List<string> seen = new List<string>();
            foreach (PendingItemRow r in _all)
            {
                Text = "Enter the unit price quoted for each item to buy, in PHP (the price list is suggested when there is no quotation yet).",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 108)
            });

            Panel gridHost = new Panel { Location = new Point(20, 110), Size = new Size(960, 430) };
            Theme.StyleGrid(_grid);
            _grid.AutoGenerateColumns = false;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.Dock = DockStyle.Fill;

            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "colItem",
                HeaderText = "ITEM",
                DataPropertyName = "ProductName",
                ReadOnly = true,
                FillWeight = 44
            });
            DataGridViewTextBoxColumn qty = new DataGridViewTextBoxColumn
            {
                Name = "colQty",
                HeaderText = "QTY",
                DataPropertyName = "Quantity",
                ReadOnly = true,
                FillWeight = 12
            };
            qty.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Add(qty);
            DataGridViewTextBoxColumn price = new DataGridViewTextBoxColumn
            {
                Name = "colPrice",
                HeaderText = "UNIT PRICE (PHP)",
                DataPropertyName = "UnitPrice",
                ReadOnly = false,
                FillWeight = 22
            };
            price.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            price.DefaultCellStyle.Format = "N2";
            _grid.Columns.Add(price);
            DataGridViewTextBoxColumn line = new DataGridViewTextBoxColumn
            {
                Name = "colLine",
                HeaderText = "AMOUNT (PHP)",
                DataPropertyName = "LineTotal",
                ReadOnly = true,
                FillWeight = 22
            };
            line.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            line.DefaultCellStyle.Format = "N2";
            _grid.Columns.Add(line);

            _grid.CurrentCellDirtyStateChanged += OnDirty;
            _grid.CellValueChanged += delegate { _grid.Invalidate(); UpdateTotal(); };
            _grid.DataError += OnDataError;
            gridHost.Controls.Add(_grid);
            Controls.Add(gridHost);

            _total.Font = Theme.FontBold;
            _total.AutoSize = false;
            _total.TextAlign = ContentAlignment.MiddleRight;
            _total.Bounds = new Rectangle(560, 548, 420, 28);
            Controls.Add(_total);

            Controls.Add(new Label
            {
                Text = "Tick the items to order and type the quantity. Items you leave out stay available for the next purchase order.",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 552)
            });

            Button save = Theme.MakeButton("CREATE PO", 150, true, OnSave);
            save.Location = new Point(830, 590);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(700, 590);
            Controls.Add(save);
            Controls.Add(cancel);
            CancelButton = cancel;

            _filter.SelectedIndexChanged += delegate { ApplyFilter(); };
            ApplyFilter();
        }

        private static DataGridViewTextBoxColumn TextCol(string name, string header, string prop, float weight, bool readOnly, bool right)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = prop,
                ReadOnly = readOnly,
                FillWeight = weight
            };
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            return c;
        }

        // shows only the items of the chosen request (the ticks of hidden items are kept)
        private void ApplyFilter()
        {
            _grid.EndEdit();
            string pick = _filter.SelectedItem as string;
            List<PendingItemRow> rows = _all.FindAll(delegate (PendingItemRow r)
            {
                int supplierId = SupplierIdValue;
                _items = new BindingList<QuotationItemRow>(PurchasingService.GetQuotationItems(_requisitionId, supplierId));
                _grid.DataSource = _items;

                // an earlier quotation of this supplier for this requisition wins; otherwise suggest the supplier's usual payment terms
                _terms.Text = ((SupplierRow)_supplier.SelectedItem).PaymentTerms ?? string.Empty;
                foreach (ReqQuotationRow q in _existing)
                    if (q.SupplierId == supplierId) { _terms.Text = q.PaymentTerms; break; }

        private void OnTickAll(object sender, EventArgs e)
        {
            _grid.EndEdit();
            foreach (PendingItemRow r in _view) r.Selected = true;
            _grid.Refresh();
            UpdateTotal();
        }

        private void OnClear(object sender, EventArgs e)
        {
            _grid.EndEdit();
            foreach (PendingItemRow r in _all) r.Selected = false;
            _grid.Refresh();
            UpdateTotal();
        }

        // a tick is saved at once, so the total updates without leaving the cell
        private void OnDirty(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void UpdateTotal()
        {
            decimal sum = 0;
            int count = 0;
            foreach (PendingItemRow r in _all)
            {
                if (!r.Selected || r.OrderQty <= 0) continue;
                sum += r.LineTotal;
                count++;
            }
            _total.Text = count + " item(s) selected   |   Total: PHP " + sum.ToString("N2");
        }

        private void OnDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            MessageBox.Show(this, "Please enter the quantity as a whole number, for example 12.",
                "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnSave(object sender, EventArgs e)
        {
            _grid.EndEdit();

            string problem = null;
            if (_supplier.SelectedItem == null)
                problem = "Please choose a supplier.";
            else if (SelectedItems.Count == 0)
                problem = "Tick at least one item to order, with a quantity of 1 or more.";
            else
            {
                foreach (PendingItemRow r in _all)
                {
                    if (!r.Selected) continue;
                    if (r.OrderQty < 1 || r.OrderQty > r.RemainingQty)
                    {
                        problem = "\"" + r.ProductName + "\" (" + r.RefNo + "): the quantity must be from 1 to " + r.RemainingQty + ".";
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