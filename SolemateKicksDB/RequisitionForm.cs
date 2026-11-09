using SolemateKicks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// Create (or revise) a requisition.
    /// LEFT  : product catalog   -> drag a shoe row ...
    /// RIGHT : requisition items -> ... and drop it here (or double-click a row).
    /// Drag a line from the right back onto the catalog to remove it.
    /// </summary>
    public class RequisitionForm : Form
    {
        // custom drag-and-drop data formats
        private const string DragCatalogFormat = "SolemateCatalogRow";
        private const string DragCartFormat = "SolemateCartLine";

        private readonly AppUser _user = Session.CurrentUser;
        private readonly int? _editingId;
        private string _editingRef;

        private List<CatalogRow> _allProducts = new List<CatalogRow>();
        private readonly BindingList<CartLine> _cart = new BindingList<CartLine>();
        private bool _ready;

        // controls
        private ComboBox cmbBrand;
        private TextBox txtSearch;
        private DataGridView gridCatalog;
        private DataGridView gridCart;
        private Panel cartHost;
        private Panel dropZone;
        private NumericUpDown nudQty;
        private TextBox txtRemarks;
        private Label lblSummary;
        private Button btnSubmit, btnClear, btnClose;

        // drag bookkeeping
        private Point _catalogDragStart;
        private int _catalogDragRow = -1;
        private Point _cartDragStart;
        private int _cartDragRow = -1;

        public RequisitionForm(int? requisitionId = null)
        {
            _editingId = requisitionId;

            Text = requisitionId.HasValue ? "Solemate - Revise requisition" : "Solemate - New requisition";
            ClientSize = new Size(1200, 740);
            MinimumSize = new Size(1040, 660);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            BuildUi();
            WireEvents();

            // ---- load data ----
            try
            {
                List<Brand> brands = RequisitionService.GetBrands();
                brands.Insert(0, new Brand { BrandId = 0, BrandName = "All brands" });
                cmbBrand.DisplayMember = "BrandName";
                cmbBrand.ValueMember = "BrandId";
                cmbBrand.DataSource = brands;

                _allProducts = RequisitionService.GetCatalog();

                if (requisitionId.HasValue)
                {
                    RevisionData data = RequisitionService.GetForRevision(requisitionId.Value);
                    _editingRef = data.RefNo;
                    txtRemarks.Text = data.Remarks ?? string.Empty;
                    foreach (CartLine line in data.Lines) _cart.Add(line);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            _ready = true;
            ApplyFilter();
            RefreshCartState();
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // body
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));   // footer

            string title = _editingId.HasValue ? "Revise requisition" : "New requisition";
            root.Controls.Add(Theme.MakeHeader(title, "Drag shoes from the catalog into the request list"), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);

            Controls.Add(root);
        }

        private Control BuildBody()
        {
            TableLayoutPanel body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8),
                Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            body.Controls.Add(BuildCatalogCard(), 0, 0);
            body.Controls.Add(BuildCartCard(), 1, 0);
            return body;
        }

        // ---------------- LEFT: catalog ----------------
        private Control BuildCatalogCard()
        {
            Panel card = Theme.MakeCard(new Padding(14, 8, 14, 12));

            // filter row
            Panel filter = new Panel { Dock = DockStyle.Top, Height = 46 };
            Label lblBrand = new Label { Text = "Brand", Font = Theme.FontBold, AutoSize = true, Location = new Point(0, 14) };
            cmbBrand = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.FontBody,
                Location = new Point(52, 10),
                Width = 170
            };
            Label lblSearch = new Label { Text = "Search", Font = Theme.FontBold, AutoSize = true, Location = new Point(244, 14) };
            txtSearch = new TextBox
            {
                Font = Theme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(302, 10),
                Width = 220
            };
            filter.Controls.Add(lblBrand);
            filter.Controls.Add(cmbBrand);
            filter.Controls.Add(lblSearch);
            filter.Controls.Add(txtSearch);

            // grid
            gridCatalog = new DataGridView();
            Theme.StyleGrid(gridCatalog);
            gridCatalog.ReadOnly = true;
            gridCatalog.AllowDrop = true;       // lets you drag a request line back here to remove it
            Theme.AddColumn(gridCatalog, "Brand", "Brand", "Brand", 18f);
            Theme.AddColumn(gridCatalog, "Product", "Model", "Product", 30f);
            Theme.AddColumn(gridCatalog, "Size", "Size (US)", "Size", 11f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCatalog, "Stock", "Stock", "Stock", 9f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCatalog, "StockStatus", "Level", "StockStatus", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCatalog, "Price", "Price", "Price", 14f, "\u20B1#,##0", true, DataGridViewContentAlignment.MiddleRight);

            Label hint = new Label
            {
                Text = "Tip: drag a row to the right, or double-click it.  Red / amber stock means it is out of stock / low.",
                Dock = DockStyle.Bottom,
                Height = 30,
                AutoSize = false,
                ForeColor = Theme.Muted,
                Font = Theme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // fill first, then edge-docked controls (the last one added is docked first = top-most)
            card.Controls.Add(gridCatalog);
            card.Controls.Add(hint);
            card.Controls.Add(filter);
            card.Controls.Add(Theme.MakeSectionTitle("Product catalog"));
            return card;
        }

        // ---------------- RIGHT: request list ----------------
        private Control BuildCartCard()
        {
            Panel card = Theme.MakeCard(new Padding(14, 8, 14, 12));

            // info block
            Panel info = new Panel { Dock = DockStyle.Top, Height = 100 };
            Label lblWho = new Label
            {
                Text = string.Format("Requested by: {0}   |   {1}   |   {2:MMM dd, yyyy}", _user.FullName, _user.Branch, DateTime.Now),
                Font = Theme.FontBold,
                ForeColor = Theme.Ink,
                AutoSize = false,
                Location = new Point(0, 2),
                Size = new Size(520, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label lblRem = new Label { Text = "Remarks (optional)", AutoSize = true, Location = new Point(0, 38), ForeColor = Theme.Muted };
            txtRemarks = new TextBox
            {
                Font = Theme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 200,
                Location = new Point(140, 34),
                Width = 360,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label lblQty = new Label { Text = "Quantity per drop", AutoSize = true, Location = new Point(0, 70), ForeColor = Theme.Muted };
            nudQty = new NumericUpDown
            {
                Font = Theme.FontBody,
                Minimum = 1,
                Maximum = 999,
                Value = 1,
                Location = new Point(140, 66),
                Width = 80
            };
            info.Controls.Add(lblWho);
            info.Controls.Add(lblRem);
            info.Controls.Add(txtRemarks);
            info.Controls.Add(lblQty);
            info.Controls.Add(nudQty);

            // summary line
            lblSummary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                AutoSize = false,
                Font = Theme.FontBold,
                ForeColor = Theme.Red,
                TextAlign = ContentAlignment.MiddleRight
            };

            // cart grid + empty-state drop zone share one host panel
            cartHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.White };

            gridCart = new DataGridView();
            Theme.StyleGrid(gridCart);
            gridCart.ReadOnly = false;
            gridCart.DataSource = _cart;
            Theme.AddColumn(gridCart, "Brand", "Brand", "Brand", 17f);
            Theme.AddColumn(gridCart, "Product", "Model", "Product", 28f);
            Theme.AddColumn(gridCart, "Size", "Size", "Size", 8f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(gridCart, "Stock", "In stock", "Stock", 10f, null, true, DataGridViewContentAlignment.MiddleCenter);
            DataGridViewTextBoxColumn colQty =
                Theme.AddColumn(gridCart, "Quantity", "Qty", "Quantity", 11f, null, false, DataGridViewContentAlignment.MiddleCenter);
            colQty.DefaultCellStyle.Font = Theme.FontBold;
            colQty.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 246);

            DataGridViewButtonColumn colRemove = new DataGridViewButtonColumn
            {
                Name = "Remove",
                HeaderText = string.Empty,
                Text = "Remove",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 14f,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRemove.DefaultCellStyle.ForeColor = Theme.Red;
            colRemove.DefaultCellStyle.BackColor = Theme.White;
            colRemove.DefaultCellStyle.Padding = new Padding(0);
            gridCart.Columns.Add(colRemove);

            dropZone = new Panel { Dock = DockStyle.Fill, BackColor = Theme.White };
            dropZone.Paint += DropZone_Paint;
            dropZone.Resize += (s, e) => dropZone.Invalidate();

            cartHost.Controls.Add(gridCart);
            cartHost.Controls.Add(dropZone);

            card.Controls.Add(cartHost);   // fill first
            card.Controls.Add(lblSummary); // bottom
            card.Controls.Add(info);       // top (below the title)
            card.Controls.Add(Theme.MakeSectionTitle("Requisition items"));
            return card;
        }

        private Control BuildFooter()
        {
            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.White,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(14, 14, 14, 0),
                Margin = new Padding(0)
            };

            // RightToLeft: the first control added ends up on the far right
            btnSubmit = Theme.MakeButton(_editingId.HasValue ? "RESUBMIT REQUISITION" : "SUBMIT REQUISITION", 230, true, OnSubmit);
            btnClear = Theme.MakeButton("CLEAR LIST", 130, false, OnClear);
            btnClose = Theme.MakeButton("CLOSE", 110, false, (s, e) => Close());

            footer.Controls.Add(btnSubmit);
            footer.Controls.Add(btnClear);
            footer.Controls.Add(btnClose);
            foreach (Control c in footer.Controls) c.Margin = new Padding(6, 0, 6, 0);
            return footer;
        }

        private void DropZone_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(8, 8, Math.Max(10, dropZone.Width - 17), Math.Max(10, dropZone.Height - 17));
            using (Pen pen = new Pen(Theme.Red, 2f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, r);

            using (StringFormat top = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far })
            using (StringFormat bottom = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            using (Font big = new Font("Segoe UI", 18f, FontStyle.Bold | FontStyle.Italic))
            using (SolidBrush red = new SolidBrush(Theme.Red))
            using (SolidBrush grey = new SolidBrush(Theme.Muted))
            {
                int half = r.Height / 2;
                g.DrawString("DROP SHOES HERE", big, red, new RectangleF(r.X, r.Y, r.Width, half), top);
                g.DrawString("Drag a row from the catalog and release it on this box.",
                    Theme.FontBody, grey, new RectangleF(r.X, r.Y + half + 6, r.Width, half), bottom);
            }
        }

        // ==================================================================
        //  Events
        // ==================================================================
        private void WireEvents()
        {
            // ---- filters (LINQ to Objects on the cached catalog) ----
            cmbBrand.SelectedIndexChanged += (s, e) => ApplyFilter();
            txtSearch.TextChanged += (s, e) => ApplyFilter();

            // ---- catalog colouring ----
            gridCatalog.CellFormatting += GridCatalog_CellFormatting;

            // ---- catalog: start a drag (mouse down + move) / double-click to add ----
            gridCatalog.MouseDown += GridCatalog_MouseDown;
            gridCatalog.MouseMove += GridCatalog_MouseMove;
            gridCatalog.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                CatalogRow row = gridCatalog.Rows[e.RowIndex].DataBoundItem as CatalogRow;
                if (row != null) AddToCart(row, (int)nudQty.Value);
            };

            // ---- catalog is also a drop target: dropping a request line here removes it ----
            gridCatalog.DragEnter += Catalog_DragEnterOver;
            gridCatalog.DragOver += Catalog_DragEnterOver;
            gridCatalog.DragDrop += Catalog_DragDrop;

            // ---- request list = drop target for catalog rows ----
            foreach (Control target in new Control[] { dropZone, gridCart, cartHost })
            {
                target.AllowDrop = true;
                target.DragEnter += Cart_DragEnterOver;
                target.DragOver += Cart_DragEnterOver;
                target.DragLeave += Cart_DragLeave;
                target.DragDrop += Cart_DragDrop;
            }

            // ---- cart grid behaviour ----
            _cart.ListChanged += (s, e) => RefreshCartState();
            gridCart.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && gridCart.Columns[e.ColumnIndex].Name == "Remove") _cart.RemoveAt(e.RowIndex);
            };
            gridCart.CellValidating += GridCart_CellValidating;
            gridCart.CellEndEdit += (s, e) => { if (e.RowIndex >= 0) gridCart.Rows[e.RowIndex].ErrorText = string.Empty; };
            gridCart.DataError += (s, e) => { e.ThrowException = false; };
            gridCart.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !gridCart.IsCurrentCellInEditMode && gridCart.CurrentRow != null)
                {
                    _cart.RemoveAt(gridCart.CurrentRow.Index);
                    e.Handled = true;
                }
            };
            gridCart.MouseDown += GridCart_MouseDown;
            gridCart.MouseMove += GridCart_MouseMove;

            // ---- warn before throwing away work ----
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK && _cart.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this, "Discard this requisition?", "Close",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) e.Cancel = true;
                }
            };
        }

        // ---------------- catalog filter ----------------
        private void ApplyFilter()
        {
            if (!_ready) return;

            Brand selected = cmbBrand.SelectedItem as Brand;
            int brandId = (selected == null) ? 0 : selected.BrandId;
            string term = txtSearch.Text.Trim();

            IEnumerable<CatalogRow> query = _allProducts;
            if (brandId > 0) query = query.Where(p => p.BrandId == brandId);
            if (term.Length > 0)
                query = query.Where(p => Has(p.Product, term) || Has(p.Brand, term) || Has(p.Sku, term) || p.Size.ToString() == term);

            gridCatalog.DataSource = query.ToList();
            gridCatalog.ClearSelection();
        }

        private static bool Has(string text, string term)
        {
            return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void GridCatalog_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = gridCatalog.Columns[e.ColumnIndex].Name;
            if (col != "Stock" && col != "StockStatus") return;

            CatalogRow row = gridCatalog.Rows[e.RowIndex].DataBoundItem as CatalogRow;
            if (row == null) return;

            e.CellStyle.Font = Theme.FontBold;
            e.CellStyle.ForeColor = row.Stock <= 0 ? Theme.Red : (row.Stock <= row.ReorderLevel ? Theme.Warn : Theme.Ok);
        }

        // ---------------- drag from catalog ----------------
        private void GridCatalog_MouseDown(object sender, MouseEventArgs e)
        {
            DataGridView.HitTestInfo hit = gridCatalog.HitTest(e.X, e.Y);
            _catalogDragRow = (e.Button == MouseButtons.Left && hit.Type == DataGridViewHitTestType.Cell) ? hit.RowIndex : -1;
            _catalogDragStart = e.Location;
        }

        private void GridCatalog_MouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != MouseButtons.Left || _catalogDragRow < 0) return;
            if (!MovedFarEnough(_catalogDragStart, e.Location)) return;

            int index = _catalogDragRow;
            _catalogDragRow = -1;
            if (index >= gridCatalog.Rows.Count) return;

            CatalogRow row = gridCatalog.Rows[index].DataBoundItem as CatalogRow;
            if (row == null) return;

            gridCatalog.Rows[index].Selected = true;
            gridCatalog.DoDragDrop(new DataObject(DragCatalogFormat, row), DragDropEffects.Copy);
        }

        // ---------------- drop onto request list ----------------
        private void Cart_DragEnterOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DragCatalogFormat);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) dropZone.BackColor = Theme.RedSoft;
        }

        private void Cart_DragLeave(object sender, EventArgs e)
        {
            dropZone.BackColor = Theme.White;
        }

        private void Cart_DragDrop(object sender, DragEventArgs e)
        {
            dropZone.BackColor = Theme.White;
            CatalogRow row = e.Data.GetData(DragCatalogFormat) as CatalogRow;
            if (row != null) AddToCart(row, (int)nudQty.Value);
        }

        // ---------------- drag a request line back to the catalog = remove ----------------
        private void GridCart_MouseDown(object sender, MouseEventArgs e)
        {
            DataGridView.HitTestInfo hit = gridCart.HitTest(e.X, e.Y);
            bool draggable = e.Button == MouseButtons.Left
                             && hit.Type == DataGridViewHitTestType.Cell
                             && gridCart.Columns[hit.ColumnIndex].Name != "Quantity"
                             && gridCart.Columns[hit.ColumnIndex].Name != "Remove";
            _cartDragRow = draggable ? hit.RowIndex : -1;
            _cartDragStart = e.Location;
        }

        private void GridCart_MouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != MouseButtons.Left || _cartDragRow < 0) return;
            if (!MovedFarEnough(_cartDragStart, e.Location)) return;

            int index = _cartDragRow;
            _cartDragRow = -1;
            if (index >= _cart.Count) return;

            gridCart.DoDragDrop(new DataObject(DragCartFormat, _cart[index]), DragDropEffects.Move);
        }

        private void Catalog_DragEnterOver(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DragCartFormat) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void Catalog_DragDrop(object sender, DragEventArgs e)
        {
            CartLine line = e.Data.GetData(DragCartFormat) as CartLine;
            if (line != null) _cart.Remove(line);
        }

        private static bool MovedFarEnough(Point start, Point now)
        {
            Size drag = SystemInformation.DragSize;
            Rectangle box = new Rectangle(start.X - drag.Width / 2, start.Y - drag.Height / 2, drag.Width, drag.Height);
            return !box.Contains(now);
        }

        // ---------------- quantity editing ----------------
        private void GridCart_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || gridCart.Columns[e.ColumnIndex].Name != "Quantity") return;

            int qty;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out qty) || qty < 1 || qty > 9999)
            {
                gridCart.Rows[e.RowIndex].ErrorText = "Quantity must be a whole number from 1 to 9999.";
                e.Cancel = true;
            }
            else
            {
                gridCart.Rows[e.RowIndex].ErrorText = string.Empty;
            }
        }

        // ==================================================================
        //  Cart logic
        // ==================================================================
        private void AddToCart(CatalogRow row, int qty)
        {
            // Out-of-stock items are allowed on purpose: that is exactly what a requisition is for.
            CartLine existing = _cart.FirstOrDefault(c => c.ProductId == row.ProductId);
            if (existing != null)
            {
                existing.Quantity += qty;
            }
            else
            {
                _cart.Add(new CartLine
                {
                    ProductId = row.ProductId,
                    Sku = row.Sku,
                    Brand = row.Brand,
                    Product = row.Product,
                    Size = row.Size,
                    Stock = row.Stock,
                    Quantity = qty
                });
            }
        }

        private void RefreshCartState()
        {
            bool has = _cart.Count > 0;

            gridCart.Visible = has;
            dropZone.Visible = !has;
            btnSubmit.Enabled = has;
            btnClear.Enabled = has;

            int pairs = _cart.Sum(c => c.Quantity);
            lblSummary.Text = has
                ? string.Format("{0} item(s)   |   {1} pair(s) requested", _cart.Count, pairs)
                : "No items yet";
        }

        private void OnClear(object sender, EventArgs e)
        {
            if (_cart.Count == 0) return;
            if (MessageBox.Show(this, "Remove all items from this requisition?", "Clear list",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _cart.Clear();
        }

        private void OnSubmit(object sender, EventArgs e)
        {
            gridCart.EndEdit();

            if (_cart.Count == 0)
            {
                MessageBox.Show(this, "Add at least one item first.", "Requisition", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string remarks = txtRemarks.Text.Trim();
                string refNo;

                if (_editingId.HasValue)
                {
                    RequisitionService.Revise(_editingId.Value, _user, _cart, remarks);
                    refNo = _editingRef;
                }
                else
                {
                    refNo = RequisitionService.Create(_user, _cart, remarks);
                }

                MessageBox.Show(this,
                    string.Format("Requisition {0} was submitted.\r\n\r\nStatus: Pending\r\nThe head can now review it.", refNo),
                    "Submitted", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;     // closes the form, main screen refreshes
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
