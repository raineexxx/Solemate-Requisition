using SolemateKicksDB;
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
    /// Requisition list. Cashier/Manager: create, revise, cancel.  Head: approve or reject.
    /// </summary>
    public class MainForm : Form
    {
        public bool LogoutRequested { get; private set; }

        private readonly AppUser _user = Session.CurrentUser;
        private List<RequisitionRow> _rows = new List<RequisitionRow>();
        private bool _loading;

        private TableLayoutPanel root;
        private DataGridView gridReq;
        private DataGridView gridLines;
        private ComboBox cmbStatus;
        private Label lblBanner;
        private Label lblDetail;
        private Button btnNew, btnRevise, btnApprove, btnReject, btnCancel, btnInventory, btnDelivery, btnRefresh;

        public MainForm()
        {
            Text = "Solemate - Requisition";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();

            cmbStatus.SelectedItem = (_user.Role == Roles.Head) ? Statuses.Pending : "All";
            cmbStatus.SelectedIndexChanged += (s, e) => LoadData();
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
            Panel header = Theme.MakeHeader("Requisition", "Item requests and approvals");

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

            Button btnLogout = new Button { Text = "LOG OUT", Bounds = new Rectangle(322, 22, 96, 32) };
            Theme.StyleButtonOnRed(btnLogout);
            btnLogout.Click += (s, e) => { LogoutRequested = true; Close(); };

            rightBox.Controls.Add(lblUser);
            rightBox.Controls.Add(btnLogout);
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

            btnNew = Theme.MakeButton("+ NEW REQUISITION", 180, true, OnNew);
            btnRevise = Theme.MakeButton("REVISE", 100, false, OnRevise);
            btnApprove = Theme.MakeButton("APPROVE", 120, true, OnApprove);
            btnReject = Theme.MakeButton("REJECT", 110, false, OnReject);
            btnCancel = Theme.MakeButton("CANCEL REQUEST", 150, false, OnCancelRequest);
            btnInventory = Theme.MakeButton("INVENTORY", 120, false, OnInventory);
            btnDelivery = Theme.MakeButton("DELIVERY", 110, false, OnDelivery);
            btnRefresh = Theme.MakeButton("REFRESH", 100, false, (s, e) => LoadData());

            bool isHead = _user.Role == Roles.Head;
            if (!isHead)
            {
                flow.Controls.Add(btnNew);
                flow.Controls.Add(btnRevise);
                flow.Controls.Add(btnCancel);
            }
            else
            {
                flow.Controls.Add(btnApprove);
                flow.Controls.Add(btnReject);
            }
            flow.Controls.Add(btnInventory);   // link to the Inventory module (everyone can view, only the Manager can edit)
            flow.Controls.Add(btnDelivery);    // link to the Delivery module (everyone can view, only the Manager can edit)
            flow.Controls.Add(btnRefresh);

            foreach (Control c in flow.Controls) c.Margin = new Padding(4, 0, 4, 0);

            // status filter on the right
            Panel rightBox = new Panel { Dock = DockStyle.Right, Width = 270 };
            Label lbl = new Label { Text = "Show:", Font = Theme.FontBold, AutoSize = true, Location = new Point(10, 22) };
            cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(62, 18),
                Width = 180
            };
            cmbStatus.Items.AddRange(Statuses.Filter);
            rightBox.Controls.Add(lbl);
            rightBox.Controls.Add(cmbStatus);

            toolbar.Controls.Add(flow);       // fill
            toolbar.Controls.Add(rightBox);   // right edge
            return toolbar;
        }

        private Control BuildBanner()
        {
            lblBanner = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                BackColor = Theme.RedSoft,
                ForeColor = Theme.RedDark,
                Font = Theme.FontBold,
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
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));

            // ---- top card: requisitions ----
            Panel cardTop = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridReq = new DataGridView();
            Theme.StyleGrid(gridReq);
            gridReq.ReadOnly = true;
            Theme.AddColumn(gridReq, "RefNo", "Ref No.", "RefNo", 18f);
            Theme.AddColumn(gridReq, "Date", "Date", "DateCreated", 20f, "MMM dd, yyyy  h:mm tt");
            Theme.AddColumn(gridReq, "RequestedBy", "Requested by", "RequestedBy", 18f);
            Theme.AddColumn(gridReq, "Branch", "Branch", "Branch", 13f);
            Theme.AddColumn(gridReq, "Lines", "Lines", "Lines", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "TotalQty", "Pairs", "TotalQty", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridReq, "Status", "Status", "Status", 12f);
            Theme.AddColumn(gridReq, "ReviewedBy", "Reviewed by", "ReviewedBy", 16f);

            gridReq.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridReq.Columns[e.ColumnIndex].Name != "Status") return;
                e.CellStyle.Font = Theme.FontBold;
                e.CellStyle.ForeColor = Theme.StatusColor(Convert.ToString(e.Value));
            };
            gridReq.SelectionChanged += (s, e) => { if (!_loading) OnSelectionChanged(); };

            cardTop.Controls.Add(gridReq);                                // fill first
            cardTop.Controls.Add(Theme.MakeSectionTitle("Requisitions")); // then top edge

            // ---- bottom card: items of the selected requisition ----
            Panel cardBottom = Theme.MakeCard(new Padding(14, 8, 14, 12));
            gridLines = new DataGridView();
            Theme.StyleGrid(gridLines);
            gridLines.ReadOnly = true;
            Theme.AddColumn(gridLines, "Sku", "SKU", "Sku", 14f);
            Theme.AddColumn(gridLines, "Brand", "Brand", "Brand", 16f);
            Theme.AddColumn(gridLines, "Product", "Model", "Product", 28f);
            Theme.AddColumn(gridLines, "Size", "Size (US)", "Size", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Quantity", "Qty requested", "Quantity", 12f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Available", "In stock", "Available", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridLines, "Check", "Stock check", "Check", 14f);

            gridLines.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || gridLines.Columns[e.ColumnIndex].Name != "Check") return;
                e.CellStyle.Font = Theme.FontBold;
                bool ok = Convert.ToString(e.Value) == "Sufficient";
                e.CellStyle.ForeColor = ok ? Theme.Ok : Theme.Red;
            };

            lblDetail = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                AutoSize = false,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft
            };

            cardBottom.Controls.Add(gridLines);
            cardBottom.Controls.Add(lblDetail);
            cardBottom.Controls.Add(Theme.MakeSectionTitle("Items in selected requisition"));

            content.Controls.Add(cardTop, 0, 0);
            content.Controls.Add(cardBottom, 0, 1);
            return content;
        }

        // ==================================================================
        //  Data
        // ==================================================================
        private void LoadData()
        {
            _loading = true;
            try
            {
                string status = Convert.ToString(cmbStatus.SelectedItem);
                _rows = RequisitionService.GetRequisitions(_user, status);

                gridReq.DataSource = null;
                gridReq.DataSource = _rows;
                gridReq.ClearSelection();

                UpdateBanner();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _loading = false;
            }

            if (gridReq.Rows.Count > 0)
            {
                gridReq.Rows[0].Selected = true;
                gridReq.CurrentCell = gridReq.Rows[0].Cells[0];
            }
            OnSelectionChanged();
        }

        private void UpdateBanner()
        {
            string msg = null;

            if (_user.Role == Roles.Head)
            {
                int pending = RequisitionService.GetPendingCount();
                if (pending > 0)
                    msg = string.Format("   {0} requisition(s) waiting for your approval.", pending);
            }
            else
            {
                int rejected = RequisitionService.GetRejectedCount(_user.UserId);
                if (rejected > 0)
                    msg = string.Format("   {0} of your requisition(s) were rejected. Select one, then Revise or Cancel it.", rejected);
            }

            lblBanner.Text = msg ?? string.Empty;
            root.RowStyles[2].Height = (msg == null) ? 0f : 36f;
        }

        private RequisitionRow Selected()
        {
            if (gridReq.CurrentRow == null) return null;
            return gridReq.CurrentRow.DataBoundItem as RequisitionRow;
        }

        private void OnSelectionChanged()
        {
            RequisitionRow sel = Selected();

            if (sel == null)
            {
                gridLines.DataSource = null;
                lblDetail.Text = string.Empty;
                SetButtons(null);
                return;
            }

            try
            {
                gridLines.DataSource = RequisitionService.GetLines(sel.RequisitionId);
                gridLines.ClearSelection();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(sel.Remarks)) parts.Add("Remarks: " + sel.Remarks);
            if (sel.Status == Statuses.Rejected && !string.IsNullOrWhiteSpace(sel.RejectReason)) parts.Add("Rejected: " + sel.RejectReason);
            if (!string.IsNullOrWhiteSpace(sel.StockCheck)) parts.Add("Stock check: " + sel.StockCheck);
            lblDetail.Text = string.Join("     |     ", parts);

            SetButtons(sel);
        }

        private void SetButtons(RequisitionRow sel)
        {
            bool has = sel != null;
            bool mine = has && sel.RequestedById == _user.UserId;
            bool pending = has && sel.Status == Statuses.Pending;
            bool rejected = has && sel.Status == Statuses.Rejected;

            btnApprove.Enabled = pending;
            btnReject.Enabled = pending;
            btnRevise.Enabled = mine && (pending || rejected);
            btnCancel.Enabled = mine && (pending || rejected);
        }

        // ==================================================================
        //  Button actions
        // ==================================================================
        private void OnNew(object sender, EventArgs e)
        {
            using (RequisitionForm f = new RequisitionForm())
            {
                if (f.ShowDialog(this) == DialogResult.OK) LoadData();
            }
        }

        private void OnRevise(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            using (RequisitionForm f = new RequisitionForm(sel.RequisitionId))
            {
                if (f.ShowDialog(this) == DialogResult.OK) LoadData();
            }
        }

        private void OnInventory(object sender, EventArgs e)
        {
            using (InventoryForm f = new InventoryForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // stock may have changed, so refresh the "In stock" / "Stock check" columns
        }

        private void OnDelivery(object sender, EventArgs e)
        {
            using (DeliveryForm f = new DeliveryForm())
            {
                f.ShowDialog(this);
            }
            LoadData();   // received goods change the stock, so refresh the stock check columns
        }

        private void OnApprove(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            if (MessageBox.Show(this, "Approve requisition " + sel.RefNo + "?", "Approve",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                ApprovalResult result = RequisitionService.Approve(sel.RequisitionId, _user);

                string msg = result.RefNo + " was approved.\r\n\r\nStock check: " + result.Summary;
                if (!result.AllAvailable)
                    msg += "\r\n\r\n" + string.Join("\r\n", result.Shortages);

                bool openInventory = false;
                if (result.AllAvailable)
                {
                    MessageBox.Show(this, msg, "Approved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    msg += "\r\n\r\nOpen Inventory to check or add stock?";
                    openInventory = MessageBox.Show(this, msg, "Approved - stock is short",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }

                LoadData();
                if (openInventory) OnInventory(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void OnReject(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            string reason = InputDialog.Ask(this, "Reject requisition",
                "Reason for rejecting " + sel.RefNo + " (the requester will see this):");
            if (reason == null) return;

            try
            {
                RequisitionService.Reject(sel.RequisitionId, _user, reason);
                LoadData();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void OnCancelRequest(object sender, EventArgs e)
        {
            RequisitionRow sel = Selected();
            if (sel == null) return;

            if (MessageBox.Show(this, "Cancel requisition " + sel.RefNo + "? This cannot be undone.", "Cancel request",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                RequisitionService.Cancel(sel.RequisitionId, _user);
                LoadData();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                LoadData();
            }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
