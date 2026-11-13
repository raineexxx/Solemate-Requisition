using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
  
    public class SuppliersPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canManage;

        private DataGridView grid;
        private Button btnAdd, btnEdit, btnDelete, btnHistory;

        public SuppliersPanel()
        {
            _canManage = _user != null && _user.Role == Roles.Head;

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
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

            btnAdd = Theme.MakeButton("+ ADD SUPPLIER", 160, true, OnAdd);
            btnEdit = Theme.MakeButton("EDIT", 100, false, OnEdit);
            btnDelete = Theme.MakeButton("DELETE", 100, false, OnDelete);
            btnHistory = Theme.MakeButton("ORDER HISTORY", 160, false, OnHistory);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, OnRefresh);

            if (_canManage)
            {
                flow.Controls.Add(btnAdd);
                flow.Controls.Add(btnEdit);
                flow.Controls.Add(btnDelete);
            }
            flow.Controls.Add(btnHistory);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildContent()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colName", "SUPPLIER NAME", "SupplierName", 28);
            Theme.AddColumn(grid, "colCountry", "COUNTRY", "Country", 12);
            Theme.AddColumn(grid, "colAddress", "ADDRESS", "Address", 34);
            Theme.AddColumn(grid, "colEmail", "EMAIL", "Email", 26);

            grid.SelectionChanged += OnSelectionChanged;
            grid.CellDoubleClick += OnCellDoubleClick;

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Suppliers"));
            return card;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private SupplierRow Selected
        {
            get
            {
                if (grid.CurrentRow == null) return null;
                return grid.CurrentRow.DataBoundItem as SupplierRow;
            }
        }

        private void SetButtons()
        {
            if (btnEdit == null || btnDelete == null || btnHistory == null) return;
            bool has = Selected != null;
            btnEdit.Enabled = _canManage && has;
            btnDelete.Enabled = _canManage && has;
            btnHistory.Enabled = has;
        }

        public void LoadData()
        {
            LoadDataAndSelect(Selected != null ? Selected.SupplierId : 0);
        }

        private void LoadDataAndSelect(int supplierId)
        {
            try
            {
                List<SupplierRow> list = PurchasingService.GetSuppliers();
                grid.DataSource = list;

                if (supplierId != 0)
                {
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        SupplierRow item = r.DataBoundItem as SupplierRow;
                        if (item != null && item.SupplierId == supplierId)
                        {
                            r.Selected = true;
                            grid.CurrentCell = r.Cells[0];
                            break;
                        }
                    }
                }
                SetButtons();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnRefresh(object sender, EventArgs e)
        {
            LoadData();
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            SetButtons();
        }

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && _canManage) OnEdit(sender, e);
        }

        private void OnAdd(object sender, EventArgs e)
        {
            using (SupplierDialog dlg = new SupplierDialog(null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int id = PurchasingService.SaveSupplier(0, dlg.NameValue, dlg.AddressValue, dlg.EmailValue, dlg.CountryValue, _user);
                    LoadDataAndSelect(id);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            SupplierRow sel = Selected;
            if (sel == null || !_canManage) return;

            using (SupplierDialog dlg = new SupplierDialog(sel))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    PurchasingService.SaveSupplier(sel.SupplierId, dlg.NameValue, dlg.AddressValue, dlg.EmailValue, dlg.CountryValue, _user);
                    LoadDataAndSelect(sel.SupplierId);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            SupplierRow sel = Selected;
            if (sel == null || !_canManage) return;

            DialogResult ask = MessageBox.Show(this,
                "Delete supplier \"" + sel.SupplierName + "\"? This cannot be undone.",
                "Delete supplier", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ask != DialogResult.Yes) return;

            try
            {
                PurchasingService.DeleteSupplier(sel.SupplierId, _user);
                LoadDataAndSelect(0);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnHistory(object sender, EventArgs e)
        {
            SupplierRow sel = Selected;
            if (sel == null) return;

            try
            {
                using (SupplierHistoryDialog dlg = new SupplierHistoryDialog(sel, PurchasingService.GetSupplierHistory(sel.SupplierId)))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ======================================================================
    //  Add / edit supplier (small dialog)
    // ======================================================================
    public class SupplierDialog : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _address = new TextBox();
        private readonly TextBox _email = new TextBox();
        private readonly ComboBox _country = new ComboBox();

        public string NameValue { get { return _name.Text.Trim(); } }
        public string AddressValue { get { return _address.Text.Trim(); } }
        public string EmailValue { get { return _email.Text.Trim(); } }
        public string CountryValue { get { return _country.Text.Trim(); } }

        public SupplierDialog(SupplierRow existing)
        {
            bool isNew = existing == null;

            Text = isNew ? "Add supplier" : "Edit supplier";
            ClientSize = new Size(520, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label { Text = "Supplier name", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 34) });
            _name.MaxLength = 100;
            _name.BorderStyle = BorderStyle.FixedSingle;
            _name.Location = new Point(150, 30);
            _name.Width = 350;
            Controls.Add(_name);

            Controls.Add(new Label { Text = "Address", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 84) });
            _address.MaxLength = 200;
            _address.Multiline = true;
            _address.BorderStyle = BorderStyle.FixedSingle;
            _address.Location = new Point(150, 80);
            _address.Size = new Size(350, 70);
            Controls.Add(_address);

            Controls.Add(new Label { Text = "Email", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 174) });
            _email.MaxLength = 100;
            _email.BorderStyle = BorderStyle.FixedSingle;
            _email.Location = new Point(150, 170);
            _email.Width = 350;
            Controls.Add(_email);

            Controls.Add(new Label { Text = "Country", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 224) });
            _country.DropDownStyle = ComboBoxStyle.DropDown;    // pick a suggestion or type another country
            _country.MaxLength = 50;
            _country.Items.AddRange(new object[] { "United Kingdom", "Vietnam" });
            _country.Location = new Point(150, 220);
            _country.Width = 350;
            Controls.Add(_country);

            if (!isNew)
            {
                _name.Text = existing.SupplierName;
                _address.Text = existing.Address;
                _email.Text = existing.Email;
                _country.Text = existing.Country;
            }

            Button save = Theme.MakeButton("SAVE", 130, true, OnSave);
            save.Location = new Point(370, 322);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(250, 322);

            Controls.Add(save);
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void OnSave(object sender, EventArgs e)
        {
            string problem = null;
            if (NameValue.Length == 0)
                problem = "Supplier name is required.";
            else if (CountryValue.Length == 0)
                problem = "Supplier country is required.";
            else if (EmailValue.Length > 0 && (EmailValue.IndexOf('@') < 1 || EmailValue.IndexOf('.') < 0))
                problem = "Please enter a valid email address, for example name@company.com.";

            if (problem != null)
            {
                MessageBox.Show(this, problem, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // ======================================================================
    //  Order history of one supplier (view only)
    // ======================================================================
    public class SupplierHistoryDialog : Form
    {
        public SupplierHistoryDialog(SupplierRow supplier, List<PurchaseOrderRow> orders)
        {
            Text = "Order history - " + supplier.SupplierName;
            ClientSize = new Size(860, 460);
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
                Text = orders.Count == 0 ? "No purchase orders for this supplier yet."
                                         : orders.Count + " purchase order(s) for " + supplier.SupplierName,
                Font = Theme.FontBold,
                AutoSize = true,
                Location = new Point(20, 24)
            });

            Panel gridHost = new Panel { Location = new Point(20, 54), Size = new Size(820, 340) };
            DataGridView grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colPo", "PO NO.", "PoNo", 16);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 16, "g");
            Theme.AddColumn(grid, "colReq", "REQUISITION", "RequisitionRef", 16);
            Theme.AddColumn(grid, "colTerms", "PAYMENT TERMS", "PaymentTerms", 18);
            Theme.AddColumn(grid, "colSent", "SENT", "SentText", 14, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colStatus", "STATUS", "Status", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colTotal", "TOTAL (PHP)", "Total", 14, "N2", true, DataGridViewContentAlignment.MiddleRight);
            grid.Dock = DockStyle.Fill;
            grid.DataSource = orders;
            gridHost.Controls.Add(grid);
            Controls.Add(gridHost);

            Button close = Theme.MakeButton("CLOSE", 110, false, null);
            close.DialogResult = DialogResult.Cancel;
            close.Location = new Point(730, 410);
            Controls.Add(close);
            CancelButton = close;
        }
    }
}