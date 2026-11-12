using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Purchasing page with two tabs in the same window: PURCHASE ORDERS and SUPPLIERS.
    /// Receiving the goods into stock is the Delivery module's job (it comes after purchasing).
    /// </summary>
    public partial class PurchasingForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;

        private PurchaseOrdersPanel pnlOrders;
        private SuppliersPanel pnlSuppliers;
        private Button btnTabOrders, btnTabSuppliers;

        public PurchasingForm()
        {
            Text = "Solemate - Purchasing";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
            Shown += PurchasingForm_Shown;
        }

        private void PurchasingForm_Shown(object sender, EventArgs e)
        {
            ShowTab(true);
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
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));   // tabs
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // page content

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildTabBar(), 0, 1);
            root.Controls.Add(BuildPages(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Panel header = Theme.MakeHeader("Purchasing", "Purchase orders and suppliers");

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
            btnBack.Click += OnBack;

            rightBox.Controls.Add(lblUser);
            rightBox.Controls.Add(btnBack);
            header.Controls.Add(rightBox);
            return header;
        }

        private Control BuildTabBar()
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

            btnTabOrders = Theme.MakeButton("PURCHASE ORDERS", 190, true, OnTabOrders);
            btnTabSuppliers = Theme.MakeButton("SUPPLIERS", 150, false, OnTabSuppliers);

            flow.Controls.Add(btnTabOrders);
            flow.Controls.Add(btnTabSuppliers);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildPages()
        {
            Panel host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };

            pnlOrders = new PurchaseOrdersPanel();
            pnlSuppliers = new SuppliersPanel();
            pnlSuppliers.Visible = false;

            host.Controls.Add(pnlOrders);
            host.Controls.Add(pnlSuppliers);
            return host;
        }

        // ==================================================================
        //  Tabs
        // ==================================================================
        private void ShowTab(bool orders)
        {
            pnlOrders.Visible = orders;
            pnlSuppliers.Visible = !orders;

            Theme.StyleButton(btnTabOrders, orders);
            Theme.StyleButton(btnTabSuppliers, !orders);

            if (orders) pnlOrders.LoadData();
            else pnlSuppliers.LoadData();
        }

        private void OnTabOrders(object sender, EventArgs e)
        {
            ShowTab(true);
        }

        private void OnTabSuppliers(object sender, EventArgs e)
        {
            ShowTab(false);
        }

        private void OnBack(object sender, EventArgs e)
        {
            Close();
        }
    }

    // ======================================================================
    //  Tab 1: purchase orders
    //  Head / Manager: create a purchase order.  Everyone else: view only.
    // ======================================================================
    public class PurchaseOrdersPanel : UserControl
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canManage;

        private DataGridView grid;
        private Button btnNew;

        public PurchaseOrdersPanel()
        {
            _canManage = _user != null && (_user.Role == Roles.Head || _user.Role == Roles.Manager);

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
        }

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

            btnNew = Theme.MakeButton("+ NEW PO", 130, true, OnNew);
            Button btnRefresh = Theme.MakeButton("REFRESH", 100, false, OnRefresh);

            if (_canManage) flow.Controls.Add(btnNew);
            flow.Controls.Add(btnRefresh);
            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);
            return flow;
        }

        private Control BuildContent()
        {
            Panel card = Theme.MakeCard(new Padding(10));

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            Theme.AddColumn(grid, "colPo", "PO NO.", "PoNo", 18);
            Theme.AddColumn(grid, "colDate", "DATE", "DateCreated", 16, "g");
            Theme.AddColumn(grid, "colSupplier", "SUPPLIER", "Supplier", 24);
            Theme.AddColumn(grid, "colReq", "REQUISITION", "RequisitionRef", 18);
            Theme.AddColumn(grid, "colStatus", "STATUS", "Status", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "colTotal", "TOTAL (PHP)", "Total", 14, "N2", true, DataGridViewContentAlignment.MiddleRight);

            grid.CellFormatting += OnCellFormatting;

            card.Controls.Add(grid);
            card.Controls.Add(Theme.MakeSectionTitle("Purchase orders"));
            return card;
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "colStatus") return;

            PurchaseOrderRow row = grid.Rows[e.RowIndex].DataBoundItem as PurchaseOrderRow;
            if (row == null) return;

            e.CellStyle.ForeColor = row.Status == "Received" ? Theme.Ok
                                  : row.Status == "Ordered" ? Theme.Warn : Theme.Muted;
            e.CellStyle.Font = Theme.FontBold;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private PurchaseOrderRow Selected
        {
            get
            {
                if (grid.CurrentRow == null) return null;
                return grid.CurrentRow.DataBoundItem as PurchaseOrderRow;
            }
        }

        public void LoadData()
        {
            try
            {
                int keepId = Selected != null ? Selected.PurchaseOrderId : 0;
                List<PurchaseOrderRow> list = PurchasingService.GetPurchaseOrders();
                grid.DataSource = list;

                // keep the same PO selected after a refresh
                if (keepId != 0)
                {
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        PurchaseOrderRow item = r.DataBoundItem as PurchaseOrderRow;
                        if (item != null && item.PurchaseOrderId == keepId)
                        {
                            r.Selected = true;
                            grid.CurrentCell = r.Cells[0];
                            break;
                        }
                    }
                }
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

        private void OnNew(object sender, EventArgs e)
        {
            try
            {
                List<ApprovedRequisitionRow> reqs = PurchasingService.GetApprovedRequisitions();
                if (reqs.Count == 0)
                {
                    MessageBox.Show(this, "There are no approved requisitions waiting for a purchase order.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (suppliers.Count == 0)
                {
                    MessageBox.Show(this, "There are no suppliers yet. Open the SUPPLIERS tab and add one first.",
                        "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (NewPoDialog dlg = new NewPoDialog(reqs, suppliers))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    PurchasingService.CreateOrder(dlg.RequisitionId, dlg.SupplierIdValue, _user);
                    LoadData();
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
    //  New purchase order: pick an approved requisition + a supplier
    // ======================================================================
    public class NewPoDialog : Form
    {
        private readonly ComboBox _req = new ComboBox();
        private readonly ComboBox _supplier = new ComboBox();

        public int RequisitionId { get { return ((ApprovedRequisitionRow)_req.SelectedItem).RequisitionId; } }
        public int SupplierIdValue { get { return ((SupplierRow)_supplier.SelectedItem).SupplierId; } }

        public NewPoDialog(List<ApprovedRequisitionRow> reqs, List<SupplierRow> suppliers)
        {
            Text = "New purchase order";
            ClientSize = new Size(520, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red });

            Controls.Add(new Label { Text = "Requisition", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 34) });
            _req.DropDownStyle = ComboBoxStyle.DropDownList;
            _req.Location = new Point(140, 30);
            _req.Width = 360;
            foreach (ApprovedRequisitionRow r in reqs) _req.Items.Add(r);   // label comes from ApprovedRequisitionRow.ToString()
            _req.SelectedIndex = 0;
            Controls.Add(_req);

            Controls.Add(new Label { Text = "Supplier", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 84) });
            _supplier.DropDownStyle = ComboBoxStyle.DropDownList;
            _supplier.Location = new Point(140, 80);
            _supplier.Width = 360;
            foreach (SupplierRow s in suppliers) _supplier.Items.Add(s);    // label comes from SupplierRow.ToString()
            _supplier.SelectedIndex = 0;
            Controls.Add(_supplier);

            Controls.Add(new Label
            {
                Text = "All items of the requisition are copied into the purchase order at their current price.",
                Font = Theme.FontSmall,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(20, 124)
            });

            Button save = Theme.MakeButton("CREATE PO", 130, true, OnSave);
            save.Location = new Point(370, 190);
            Button cancel = Theme.MakeButton("CANCEL", 110, false, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(250, 190);

            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void OnSave(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }
    }
}
