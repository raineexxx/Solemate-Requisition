using SolemateKicksDB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
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
    public partial class DeliveryForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private readonly bool _canEdit;

        private List<DeliveryRow> _all = new List<DeliveryRow>();
        private bool _loading;

        private TableLayoutPanel root;
        private DataGridView gridDel;
        private DataGridView gridItems;
        private ComboBox cmbStatus;
        private TextBox txtSearch;
        private DateTimePicker dtFrom, dtTo;
        private Label lblBanner, lblDetail;
        private Button btnNew, btnReceive, btnHistory;

        public DeliveryForm()
        {
            _canEdit = _user != null && _user.Role == Roles.Manager;

            Text = "Solemate - Delivery";
            ClientSize = new Size(1280, 720);
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
            cmbStatus.Items.AddRange(new object[] { "All", "Expected", "Overdue", "Partial", "Complete", "Short" });
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

        /// <summary>Search box and date range, shown above the deliveries grid.</summary>
        private Control BuildFilterBar()
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.White };

            Label lblSearch = new Label { Text = "Search ref / PO / supplier:", Font = Theme.FontBold, AutoSize = true, Location = new Point(0, 13) };
            txtSearch = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, Location = new Point(190, 9), Width = 230 };
            txtSearch.TextChanged += (s, e) => { if (!_loading) ApplyFilter(); };

            Label lblFrom = new Label { Text = "From:", Font = Theme.FontBold, AutoSize = true, Location = new Point(440, 13) };
            dtFrom = MakeFilterDate(new Point(488, 9));

            Label lblTo = new Label { Text = "To:", Font = Theme.FontBold, AutoSize = true, Location = new Point(650, 13) };
            dtTo = MakeFilterDate(new Point(682, 9));

            Button btnClear = Theme.MakeButton("CLEAR", 90, false, OnClearFilters);
            btnClear.Location = new Point(850, 6);
            btnClear.Height = 30;

            bar.Controls.Add(lblSearch);
            bar.Controls.Add(txtSearch);
            bar.Controls.Add(lblFrom);
            bar.Controls.Add(dtFrom);
            bar.Controls.Add(lblTo);
            bar.Controls.Add(dtTo);
            bar.Controls.Add(btnClear);
            return bar;
        }

        private DateTimePicker MakeFilterDate(Point location)
        {
            DateTimePicker dt = new DateTimePicker
            {
                Font = Theme.FontBody,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "MMM dd, yyyy",
                ShowCheckBox = true,
                Location = location,
                Width = 150
            };
            dt.Value = DateTime.Today;
            dt.Checked = false;
            dt.ValueChanged += (s, e) => { if (!_loading) ApplyFilter(); };
            return dt;
        }

        private void OnClearFilters(object sender, EventArgs e)
        {
            _loading = true;
            txtSearch.Text = string.Empty;
            dtFrom.Checked = false;
            dtTo.Checked = false;
            cmbStatus.SelectedIndex = 0;
            _loading = false;
            ApplyFilter();
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
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));

            // ---- top card: deliveries ----
            Panel cardTop = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridDel = new DataGridView();
            Theme.StyleGrid(gridDel);
            gridDel.ReadOnly = true;
            Theme.AddColumn(gridDel, "DeliveryRef", "Delivery Ref", "DeliveryRef", 19f);
            Theme.AddColumn(gridDel, "PoRef", "PO Ref", "PoRef", 14f);
            Theme.AddColumn(gridDel, "Supplier", "Supplier", "SupplierName", 19f);
            Theme.AddColumn(gridDel, "Requisition", "Requisition", "RequisitionRef", 19f);
            Theme.AddColumn(gridDel, "ExpectedDate", "Expected", "ExpectedDate", 11f, "MMM dd, yyyy");
            Theme.AddColumn(gridDel, "Created", "Created", "DateCreated", 11f, "MMM dd, yyyy");
            Theme.AddColumn(gridDel, "Ordered", "Ordered", "OrderedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Accepted", "Accepted", "AcceptedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Rejected", "Rejected", "RejectedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Outstanding", "Outstanding", "OutstandingQty", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridDel, "Value", "Value", "TotalValue", 10f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridDel, "Status", "Status", "Status", 10f);

            gridDel.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string col = gridDel.Columns[e.ColumnIndex].Name;
                DeliveryRow row = gridDel.Rows[e.RowIndex].DataBoundItem as DeliveryRow;
                if (row == null) return;

                bool overdue = row.IsOverdue == 1;
                if (overdue) e.CellStyle.BackColor = Theme.RedSoft;

                if (col == "Status")
                {
                    if (overdue && row.Status == "Expected")
                    {
                        e.Value = "Overdue";
                        e.FormattingApplied = true;
                    }
                    e.CellStyle.Font = Theme.FontBold;
                    e.CellStyle.ForeColor = overdue ? Theme.Red : StatusColor(row.Status);
                }
                else if (col == "ExpectedDate")
                {
                    if (!row.ExpectedDate.HasValue)
                    {
                        e.Value = "-";
                        e.FormattingApplied = true;
                    }
                    else if (overdue)
                    {
                        e.CellStyle.Font = Theme.FontBold;
                        e.CellStyle.ForeColor = Theme.Red;
                    }
                }
                else if (col == "Value" && row.TotalValue <= 0)
                {
                    e.Value = "-";
                    e.FormattingApplied = true;
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

            cardTop.Controls.Add(gridDel);                                 // fill first
            cardTop.Controls.Add(BuildFilterBar());                        // then the filter bar
            cardTop.Controls.Add(Theme.MakeSectionTitle("Deliveries"));    // then the title on the very top

            // ---- bottom card: items of the selected delivery ----
            Panel cardBottom = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridItems = new DataGridView();
            Theme.StyleGrid(gridItems);
            gridItems.ReadOnly = true;
            Theme.AddColumn(gridItems, "Sku", "SKU", "Sku", 12f);
            Theme.AddColumn(gridItems, "Brand", "Brand", "Brand", 12f);
            Theme.AddColumn(gridItems, "Product", "Model", "Product", 20f);
            Theme.AddColumn(gridItems, "Size", "Size (US)", "Size", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "OrderedQty", "Ordered", "OrderedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "AcceptedQty", "Accepted", "AcceptedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "RejectedQty", "Rejected", "RejectedQty", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "OutstandingQty", "Outstanding", "OutstandingQty", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridItems, "UnitCost", "Unit cost", "UnitCost", 10f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridItems, "LineValue", "Value", "LineValue", 10f, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(gridItems, "Note", "Inspection note", "Note", 18f);

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
                else if ((col == "UnitCost" && row.UnitCost <= 0) || (col == "LineValue" && row.LineValue <= 0))
                {
                    e.Value = "-";
                    e.FormattingApplied = true;
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

        private static bool Has(string source, string text)
        {
            return source != null && source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyFilter()
        {
            string status = Convert.ToString(cmbStatus.SelectedItem);

            IEnumerable<DeliveryRow> query = _all;

            if (status == "Overdue")
                query = query.Where(d => d.IsOverdue == 1);
            else if (!string.IsNullOrEmpty(status) && status != "All")
                query = query.Where(d => d.Status == status);

            string text = txtSearch.Text.Trim();
            if (text.Length > 0)
                query = query.Where(d => Has(d.DeliveryRef, text) || Has(d.PoRef, text)
                                      || Has(d.SupplierName, text) || Has(d.RequisitionRef, text));

            if (dtFrom.Checked)
            {
                DateTime from = dtFrom.Value.Date;
                query = query.Where(d => d.DateCreated.Date >= from);
            }
            if (dtTo.Checked)
            {
                DateTime to = dtTo.Value.Date;
                query = query.Where(d => d.DateCreated.Date <= to);
            }

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
            int overdue = _all.Count(d => d.IsOverdue == 1);
            int pending = _all.Count(d => (d.Status == "Partial" || d.Status == "Short") && d.OutstandingQty > 0);

            List<string> parts = new List<string>();
            if (overdue > 0)
                parts.Add(string.Format("{0} delivery(ies) are overdue - the expected date has passed.", overdue));
            if (pending > 0)
                parts.Add(string.Format("{0} delivery(ies) still have outstanding items - follow up with the supplier.", pending));

            string msg = parts.Count == 0 ? null : "   " + string.Join("     |     ", parts);

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
            if (sel.ExpectedDate.HasValue) parts.Add("Expected " + sel.ExpectedDate.Value.ToString("MMM dd, yyyy"));
            if (sel.DateReceived.HasValue) parts.Add("Last received " + sel.DateReceived.Value.ToString("MMM dd, yyyy h:mm tt"));
            if (sel.TotalValue > 0) parts.Add("Order value " + sel.TotalValue.ToString("N2"));
            if (!string.IsNullOrWhiteSpace(sel.Remarks)) parts.Add("Remarks: " + sel.Remarks);
            if (sel.IsOverdue == 1) parts.Add("OVERDUE - follow up with the supplier");
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
            if (!string.IsNullOrEmpty(r.ReportRef)) lines.Add("Receiving report: " + r.ReportRef + "  (print it from REPORTS & ISSUES)");

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
        private List<DeliverySupplier> _suppliers = new List<DeliverySupplier>();

        private ComboBox cmbSupplier, cmbReq, cmbProduct;
        private TextBox txtPo, txtRemarks;
        private DateTimePicker dtExpected;
        private NumericUpDown nudQty, nudCost;
        private DataGridView grid;
        private Button btnSave;
        private Label lblTotal;

        public string CreatedRef { get; private set; }

        public NewDeliveryDialog()
        {
            Text = "Solemate - New delivery";
            ClientSize = new Size(1060, 660);
            MinimumSize = new Size(1060, 620);
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

            // ---- row 1: supplier, PO, expected date, requisition ----
            Label lblSupplier = new Label { Text = "Supplier (pick or type a new one)", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 24) };
            cmbSupplier = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = Theme.FontBody,
                Location = new Point(20, 46),
                Width = 230,
                DisplayMember = "SupplierName",
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            Button btnSupplier = Theme.MakeButton("+", 36, false, OnNewSupplier);
            btnSupplier.Location = new Point(256, 42);
            btnSupplier.Height = 32;

            Label lblPo = new Label { Text = "PO reference (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(305, 24) };
            txtPo = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, MaxLength = 30, Location = new Point(305, 46), Width = 170 };

            Label lblExpected = new Label { Text = "Expected date", Font = Theme.FontBold, AutoSize = true, Location = new Point(490, 24) };
            dtExpected = new DateTimePicker
            {
                Font = Theme.FontBody,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "MMM dd, yyyy",
                ShowCheckBox = true,
                MinDate = DateTime.Today,
                Location = new Point(490, 46),
                Width = 150
            };
            dtExpected.Value = DateTime.Today.AddDays(7);
            dtExpected.Checked = true;

            Label lblReq = new Label { Text = "Linked approved requisition (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(655, 24) };
            cmbReq = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.FontBody, Location = new Point(655, 46), Width = 240, DisplayMember = "Display" };
            Button btnLoad = Theme.MakeButton("LOAD SHORTAGE", 140, false, OnLoadShortage);
            btnLoad.Location = new Point(900, 42);
            btnLoad.Height = 32;

            // ---- row 2 ----
            Label lblRemarks = new Label { Text = "Remarks (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 90) };
            txtRemarks = new TextBox { Font = Theme.FontBody, BorderStyle = BorderStyle.FixedSingle, MaxLength = 200, Location = new Point(20, 112), Width = 1020 };

            // ---- row 3: add an item ----
            Label lblProduct = new Label { Text = "Add item", Font = Theme.FontBold, AutoSize = true, Location = new Point(20, 156) };
            cmbProduct = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = Theme.FontBody,
                Location = new Point(20, 178),
                Width = 640,
                DisplayMember = "Display",
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            Label lblQty = new Label { Text = "Qty", Font = Theme.FontBold, AutoSize = true, Location = new Point(680, 156) };
            nudQty = new NumericUpDown { Font = Theme.FontBody, Minimum = 1, Maximum = 9999, Value = 1, Location = new Point(680, 178), Width = 80 };
            Label lblCost = new Label { Text = "Unit cost (optional)", Font = Theme.FontBold, AutoSize = true, Location = new Point(775, 156) };
            nudCost = new NumericUpDown
            {
                Font = Theme.FontBody,
                DecimalPlaces = 2,
                Minimum = 0,
                Maximum = 9999999,
                Value = 0,
                ThousandsSeparator = true,
                Location = new Point(775, 178),
                Width = 110
            };
            Button btnAdd = Theme.MakeButton("ADD ITEM", 140, true, OnAddItem);
            btnAdd.Location = new Point(900, 174);
            btnAdd.Height = 32;

            // ---- grid ----
            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.None;
            grid.Location = new Point(20, 222);
            grid.Size = new Size(1020, 340);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ReadOnly = false;
            grid.DataSource = _lines;
            Theme.AddColumn(grid, "Sku", "SKU", "Sku", 14f);
            Theme.AddColumn(grid, "Display", "Product", "Display", 38f);
            DataGridViewTextBoxColumn colQty =
                Theme.AddColumn(grid, "OrderedQty", "Ordered qty", "OrderedQty", 12f, null, false, DataGridViewContentAlignment.MiddleCenter);
            colQty.DefaultCellStyle.Font = Theme.FontBold;
            colQty.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 246);

            DataGridViewTextBoxColumn colCost =
                Theme.AddColumn(grid, "UnitCost", "Unit cost", "UnitCost", 13f, "N2", false, DataGridViewContentAlignment.MiddleRight);
            colCost.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 246);

            Theme.AddColumn(grid, "LineTotal", "Line total", "LineTotal", 14f, "N2", true, DataGridViewContentAlignment.MiddleRight);

            DataGridViewButtonColumn colRemove = new DataGridViewButtonColumn
            {
                Name = "Remove",
                HeaderText = string.Empty,
                Text = "Remove",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 12f,
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
            grid.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                grid.Rows[e.RowIndex].ErrorText = string.Empty;
                if (e.RowIndex < _lines.Count) _lines.ResetItem(e.RowIndex);   // refresh the line total
                RefreshTotal();
            };
            grid.DataError += (s, e) => { e.ThrowException = false; };
            _lines.ListChanged += (s, e) => RefreshTotal();

            // ---- footer ----
            lblTotal = new Label
            {
                Font = Theme.FontBold,
                ForeColor = Theme.Red,
                AutoSize = false,
                Location = new Point(20, 590),
                Size = new Size(640, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            btnSave = Theme.MakeButton("SAVE DELIVERY", 190, true, OnSave);
            btnSave.Location = new Point(850, 586);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Button btnCancel = Theme.MakeButton("CANCEL", 110, false, null);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(730, 586);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = btnCancel;

            Controls.Add(grid);
            Controls.Add(lblSupplier);
            Controls.Add(cmbSupplier);
            Controls.Add(btnSupplier);
            Controls.Add(lblPo);
            Controls.Add(txtPo);
            Controls.Add(lblExpected);
            Controls.Add(dtExpected);
            Controls.Add(lblReq);
            Controls.Add(cmbReq);
            Controls.Add(btnLoad);
            Controls.Add(lblRemarks);
            Controls.Add(txtRemarks);
            Controls.Add(lblProduct);
            Controls.Add(cmbProduct);
            Controls.Add(lblQty);
            Controls.Add(nudQty);
            Controls.Add(lblCost);
            Controls.Add(nudCost);
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
                BindSuppliers(null);

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

        /// <summary>Loads the supplier list; selects the supplier with selectId (or none).</summary>
        private void BindSuppliers(int? selectId)
        {
            _suppliers = DeliveryService.GetSuppliers();
            cmbSupplier.DataSource = null;
            cmbSupplier.DataSource = _suppliers;
            cmbSupplier.DisplayMember = "SupplierName";

            int index = -1;
            if (selectId.HasValue)
                index = _suppliers.FindIndex(s => s.SupplierId == selectId.Value);

            cmbSupplier.SelectedIndex = index;
            if (index < 0) cmbSupplier.Text = string.Empty;
        }

        private void OnNewSupplier(object sender, EventArgs e)
        {
            using (DeliverySupplierDialog dlg = new DeliverySupplierDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    BindSuppliers(dlg.SavedSupplierId);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void AddOrIncrease(int productId, string sku, string display, int qty, decimal cost)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].ProductId == productId)
                {
                    _lines[i].OrderedQty += qty;
                    if (cost > 0) _lines[i].UnitCost = cost;
                    _lines.ResetItem(i);
                    return;
                }
            }
            _lines.Add(new DeliveryLine { ProductId = productId, Sku = sku, Display = display, OrderedQty = qty, UnitCost = cost });
        }

        private void OnAddItem(object sender, EventArgs e)
        {
            ProductPick pick = cmbProduct.SelectedItem as ProductPick;
            if (pick == null)
            {
                MessageBox.Show(this, "Choose a product from the list first.", "Add item", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddOrIncrease(pick.ProductId, pick.Sku, pick.Display, (int)nudQty.Value, nudCost.Value);
            cmbProduct.SelectedIndex = -1;
            cmbProduct.Text = string.Empty;
            nudQty.Value = 1;
            nudCost.Value = 0;
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
                foreach (ShortageRow s in shortage) AddOrIncrease(s.ProductId, s.Sku, s.Display, s.Shortage, 0m);
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

            if (col == "OrderedQty")
            {
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
            else if (col == "UnitCost")
            {
                decimal cost;
                if (!decimal.TryParse(Convert.ToString(e.FormattedValue), out cost) || cost < 0 || cost > 9999999m)
                {
                    grid.Rows[e.RowIndex].ErrorText = "Unit cost must be a number from 0 to 9,999,999.";
                    e.Cancel = true;
                }
                else
                {
                    grid.Rows[e.RowIndex].ErrorText = string.Empty;
                }
            }
        }

        private void RefreshTotal()
        {
            if (_lines.Count == 0)
            {
                lblTotal.Text = "No items yet";
            }
            else
            {
                decimal value = _lines.Sum(l => l.LineTotal);
                lblTotal.Text = string.Format("{0} item(s)   |   {1} pair(s) ordered{2}",
                    _lines.Count, _lines.Sum(l => l.OrderedQty),
                    value > 0 ? "   |   Total value " + value.ToString("N2") : string.Empty);
            }
            btnSave.Enabled = _lines.Count > 0;
        }

        private void OnSave(object sender, EventArgs e)
        {
            grid.EndEdit();

            try
            {
                ApprovedRequisitionPick req = cmbReq.SelectedItem as ApprovedRequisitionPick;
                int reqId = (req == null) ? 0 : req.RequisitionId;

                string supplierText = cmbSupplier.Text.Trim();
                DeliverySupplier match = _suppliers.FirstOrDefault(
                    s => string.Equals(s.SupplierName, supplierText, StringComparison.OrdinalIgnoreCase));
                int supplierId = (match == null) ? 0 : match.SupplierId;

                DateTime? expected = dtExpected.Checked ? (DateTime?)dtExpected.Value.Date : null;

                CreatedRef = DeliveryService.CreateDelivery(_user, supplierText, supplierId, txtPo.Text, reqId,
                    expected, txtRemarks.Text, _lines);

                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ======================================================================
    //  NEW SUPPLIER  (small form opened by the "+" button next to Supplier)
    // ======================================================================
    public class DeliverySupplierDialog : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private TextBox txtName, txtContact, txtPhone, txtEmail;

        public int SavedSupplierId { get; private set; }

        public DeliverySupplierDialog()
        {
            Text = "Solemate - New supplier";
            ClientSize = new Size(460, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.White;
            Font = Theme.FontBody;

            Panel bar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Red };

            txtName = AddField("Supplier name (required)", 24, 100);
            txtContact = AddField("Contact person (optional)", 88, 100);
            txtPhone = AddField("Phone (optional)", 152, 30);
            txtEmail = AddField("Email (optional)", 216, 100);

            Button btnSave = Theme.MakeButton("SAVE SUPPLIER", 170, true, OnSave);
            btnSave.Location = new Point(270, 286);

            Button btnCancel = Theme.MakeButton("CANCEL", 110, false, null);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(150, 286);
            CancelButton = btnCancel;

            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(bar);
        }

        private TextBox AddField(string label, int top, int maxLength)
        {
            Label lbl = new Label { Text = label, Font = Theme.FontBold, AutoSize = true, Location = new Point(20, top + 10) };
            TextBox txt = new TextBox
            {
                Font = Theme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = maxLength,
                Location = new Point(20, top + 32),
                Width = 420
            };
            Controls.Add(lbl);
            Controls.Add(txt);
            return txt;
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                SavedSupplierId = DeliveryService.SaveSupplier(_user, 0, txtName.Text, txtContact.Text, txtPhone.Text, txtEmail.Text);
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
        private readonly DataGridView gridReports;
        private readonly Button btnPrint;

        public DeliveryHistoryDialog(DeliveryRow delivery)
        {
            Text = "Solemate - Reports and issues: " + delivery.DeliveryRef;
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(800, 540);
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
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));

            // ---- receiving reports ----
            Panel cardReports = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridReports = new DataGridView();
            Theme.StyleGrid(gridReports);
            gridReports.ReadOnly = true;
            Theme.AddColumn(gridReports, "ReportRef", "Receiving report", "ReportRef", 24f);
            Theme.AddColumn(gridReports, "DateReceived", "Date received", "DateReceived", 26f, "MMM dd, yyyy  h:mm tt");
            Theme.AddColumn(gridReports, "ReceivedBy", "Received by", "ReceivedBy", 24f);
            Theme.AddColumn(gridReports, "TotalAccepted", "Accepted", "TotalAccepted", 13f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReports, "TotalRejected", "Rejected", "TotalRejected", 13f, null, true, DataGridViewContentAlignment.MiddleCenter);
            gridReports.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OnPrint(s, e); };

            Panel printBar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            btnPrint = Theme.MakeButton("PRINT / PDF REPORT", 200, true, OnPrint);
            btnPrint.Location = new Point(0, 8);
            btnPrint.Height = 32;
            Label lblPrintHint = new Label
            {
                Text = "Select a receiving report, then print it or save it as PDF (choose 'Microsoft Print to PDF').",
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                AutoSize = true,
                Location = new Point(212, 16)
            };
            printBar.Controls.Add(btnPrint);
            printBar.Controls.Add(lblPrintHint);

            cardReports.Controls.Add(gridReports);                                          // fill first
            cardReports.Controls.Add(printBar);                                             // bottom edge
            cardReports.Controls.Add(Theme.MakeSectionTitle("Receiving reports (accepted goods)"));   // top edge

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
                gridDisc.ClearSelection();

                if (gridReports.Rows.Count > 0)
                {
                    gridReports.Rows[0].Selected = true;
                    gridReports.CurrentCell = gridReports.Rows[0].Cells[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
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