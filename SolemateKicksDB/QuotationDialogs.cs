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
    //  New purchase order: the Head picks a supplier, then ticks the items to order from any approved request
    //  and types the quantity of each. One PO = one supplier, and may mix items of several requests.
    //  (The file keeps its old name so the project file does not change.)
    // ======================================================================
    public class NewPoDialog : Form
    {
        private const string AllRequests = "All requests";

        private readonly List<PendingItemRow> _all;
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

        public NewPoDialog(List<PendingItemRow> pending, List<SupplierRow> suppliers)
        {
            _all = pending;
            foreach (PendingItemRow r in _all) r.OrderQty = r.RemainingQty;   // default: order everything that is left

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
                string label = r.RefNo + "  -  " + r.Branch;
                if (!seen.Contains(label)) { seen.Add(label); _filter.Items.Add(label); }
            }
            _filter.SelectedIndex = 0;
            Controls.Add(_filter);

            Button all = Theme.MakeButton("TICK ALL SHOWN", 170, false, OnTickAll);
            all.Location = new Point(560, 64);
            Button none = Theme.MakeButton("CLEAR", 100, false, OnClear);
            none.Location = new Point(740, 64);
            Controls.Add(all);
            Controls.Add(none);

            Panel gridHost = new Panel { Location = new Point(20, 110), Size = new Size(960, 430) };
            Theme.StyleGrid(_grid);
            _grid.AutoGenerateColumns = false;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.Dock = DockStyle.Fill;

            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "colPick",
                HeaderText = "ORDER",
                DataPropertyName = "Selected",
                FillWeight = 8
            });
            _grid.Columns.Add(TextCol("colReq", "REQUEST", "RefNo", 17, true, false));
            _grid.Columns.Add(TextCol("colBranch", "BRANCH", "Branch", 12, true, false));
            _grid.Columns.Add(TextCol("colItem", "ITEM", "ProductName", 26, true, false));
            _grid.Columns.Add(TextCol("colAsked", "REQUESTED", "RequestedQty", 10, true, true));
            _grid.Columns.Add(TextCol("colLeft", "LEFT TO ORDER", "RemainingQty", 11, true, true));
            _grid.Columns.Add(TextCol("colQty", "QTY TO ORDER", "OrderQty", 11, false, true));
            DataGridViewTextBoxColumn price = TextCol("colPrice", "PRICE (PHP)", "UnitPrice", 11, true, true);
            price.DefaultCellStyle.Format = "N2";
            _grid.Columns.Add(price);
            DataGridViewTextBoxColumn line = TextCol("colLine", "AMOUNT (PHP)", "LineTotal", 12, true, true);
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
                return pick == null || pick == AllRequests || (r.RefNo + "  -  " + r.Branch) == pick;
            });
            _view = new BindingList<PendingItemRow>(rows);
            _grid.DataSource = _view;
            UpdateTotal();
        }

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