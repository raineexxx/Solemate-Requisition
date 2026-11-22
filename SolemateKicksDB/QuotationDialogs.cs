using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;

namespace SolemateKicks
{
    /// <summary>
    /// A small read-only window with one grid: used to show the items of a purchase requisition or of a purchase order.
    /// The caller adds the columns in the "buildColumns" action.
    /// </summary>
    public class GridDialog : Form
    {
        public GridDialog(string title, string caption, IList data, Action<DataGridView> buildColumns)
        {
            Text = title;
            ClientSize = new Size(820, 440);
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;
            ShowInTaskbar = false;

            Label lbl = new Label
            {
                Text = caption,
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(12, 10, 12, 0),
                Font = Theme.FontBold,
                ForeColor = Theme.Ink
            };

            DataGridView grid = new DataGridView();
            Theme.StyleGrid(grid);
            buildColumns(grid);
            grid.DataSource = data;

            Button btnClose = Theme.MakeButton("CLOSE", 100, true, delegate { Close(); });
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.White, Padding = new Padding(8, 8, 8, 0) };
            btnClose.Dock = DockStyle.Right;
            bottom.Controls.Add(btnClose);

            Panel card = Theme.MakeCard(new Padding(10));
            card.Controls.Add(grid);

            Controls.Add(card);
            Controls.Add(bottom);
            Controls.Add(lbl);
            CancelButton = btnClose;
        }
    }

    /// <summary>One row of the supplier comparison grid (UI only, so get-only properties are fine here).</summary>
    public class QuoteView
    {
        public int SupplierId { get; private set; }
        public string SupplierName { get; private set; }
        public string Country { get; private set; }
        public string PaymentTerms { get; private set; }
        public string LeadTime { get; private set; }
        public string Coverage { get; private set; }
        public decimal Total { get; private set; }
        public bool Complete { get; private set; }
        public bool Lowest { get; private set; }
        public string LowestText { get { return Lowest ? "LOWEST" : ""; } }

        public QuoteView(QuoteRow q, bool lowest)
        {
            SupplierId = q.SupplierId;
            SupplierName = q.SupplierName;
            Country = q.Country;
            PaymentTerms = q.PaymentTerms;
            LeadTime = q.LeadTimeDays > 0 ? q.LeadTimeDays + " days" : "";
            Coverage = q.Coverage();
            Total = q.Total;
            Complete = q.IsComplete();
            Lowest = lowest;
        }
    }

    /// <summary>
    /// Paper 3.3 step 4: the suppliers (UK and Vietnam) are compared and the lowest price is chosen.
    /// The prices come from each supplier's price list (SUPPLIERS tab); nothing is typed per order.
    /// The Head picks the supplier and the purchase order is created from the purchase requisition.
    /// </summary>
    public class QuotesDialog : Form
    {
        private readonly PurchaseRequisitionRow _pr;
        private readonly List<QuoteView> _views = new List<QuoteView>();
        private DataGridView _grid;

        public int SupplierIdValue { get; private set; }

        public QuotesDialog(PurchaseRequisitionRow pr, List<QuoteRow> quotes)
        {
            _pr = pr;

            Text = "Supplier prices - " + pr.PrNo;
            ClientSize = new Size(900, 460);
            MinimumSize = new Size(760, 380);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;
            ShowInTaskbar = false;

            // the first complete quotation is the cheapest (the list comes sorted)
            bool lowestDone = false;
            foreach (QuoteRow q in quotes)
            {
                bool lowest = !lowestDone && q.IsComplete();
                if (lowest) lowestDone = true;
                _views.Add(new QuoteView(q, lowest));
            }

            BuildUi();
        }

        private void BuildUi()
        {
            Label lbl = new Label
            {
                Text = _pr.PrNo + "  |  " + _pr.RequisitionRef + "  |  " + _pr.Branch + "  |  " + _pr.Items + " item(s), " + _pr.Pairs + " pair(s) to buy" + Environment.NewLine +
                       "Prices are the suppliers' own price lists. The lowest complete total is marked LOWEST (the client's rule: choose the lowest price).",
                Dock = DockStyle.Top,
                Height = 58,
                Padding = new Padding(12, 8, 12, 0),
                Font = Theme.FontBold,
                ForeColor = Theme.Ink
            };

            _grid = new DataGridView();
            Theme.StyleGrid(_grid);
            Theme.AddColumn(_grid, "colSupplier", "SUPPLIER", "SupplierName", 24);
            Theme.AddColumn(_grid, "colCountry", "COUNTRY", "Country", 12);
            Theme.AddColumn(_grid, "colCover", "PRICES FOUND", "Coverage", 14, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(_grid, "colTerms", "PAYMENT TERMS", "PaymentTerms", 16);
            Theme.AddColumn(_grid, "colLead", "LEAD TIME", "LeadTime", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(_grid, "colTotal", "TOTAL (PHP)", "Total", 14, "N2", true, DataGridViewContentAlignment.MiddleRight);
            Theme.AddColumn(_grid, "colLow", "", "LowestText", 10, null, true, DataGridViewContentAlignment.MiddleCenter);
            _grid.DataSource = _views;
            _grid.CellFormatting += OnCellFormatting;
            _grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OnCreate(null, EventArgs.Empty); };

            Panel card = Theme.MakeCard(new Padding(10));
            card.Controls.Add(_grid);

            Button btnCreate = Theme.MakeButton("CREATE PO FOR SELECTED SUPPLIER", 290, true, OnCreate);
            Button btnClose = Theme.MakeButton("CLOSE", 100, false, delegate { DialogResult = DialogResult.Cancel; });
            FlowLayoutPanel bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Theme.White,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0)
            };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnCreate);

            Controls.Add(card);
            Controls.Add(bottom);
            Controls.Add(lbl);
            CancelButton = btnClose;
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            QuoteView v = _grid.Rows[e.RowIndex].DataBoundItem as QuoteView;
            if (v == null) return;

            if (v.Lowest)
            {
                e.CellStyle.ForeColor = Theme.Ok;
                e.CellStyle.Font = Theme.FontBold;
            }
            else if (!v.Complete)
            {
                e.CellStyle.ForeColor = Theme.Muted;   // this supplier has no price for some items
            }
        }

        private void OnCreate(object sender, EventArgs e)
        {
            QuoteView v = _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as QuoteView;
            if (v == null)
            {
                MessageBox.Show(this, "Select a supplier first.", "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!v.Complete)
            {
                MessageBox.Show(this, v.SupplierName + " has no price for every item (" + v.Coverage + "). " +
                    "Add the missing prices in the SUPPLIERS tab, or choose another supplier.",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = "Create the purchase order for " + v.SupplierName + "?" + Environment.NewLine +
                         "Total: PHP " + v.Total.ToString("N2");
            MessageBoxIcon icon = MessageBoxIcon.Question;

            if (!v.Lowest)
            {
                msg = v.SupplierName + " is NOT the lowest price." + Environment.NewLine +
                      "The lowest is " + LowestName() + "." + Environment.NewLine + Environment.NewLine + msg;
                icon = MessageBoxIcon.Warning;
            }

            if (MessageBox.Show(this, msg, "Create purchase order", MessageBoxButtons.YesNo, icon) != DialogResult.Yes) return;

            SupplierIdValue = v.SupplierId;
            DialogResult = DialogResult.OK;
        }

        private string LowestName()
        {
            foreach (QuoteView q in _views)
                if (q.Lowest) return q.SupplierName + " (PHP " + q.Total.ToString("N2") + ")";
            return "-";
        }
    }

    /// <summary>
    /// The written purchase order (paper 3.3 step 5: written purchase orders replace phone-only orders).
    /// Saved as an HTML page that can be printed to PDF, plus an e-mail to the supplier.
    /// </summary>
    public static class PoPrinter
    {
        private static string Enc(string s) { return WebUtility.HtmlEncode(s ?? string.Empty); }

        public static string Save(PoPrintHeader h, List<PoItemRow> items, string preparedBy)
        {
            StringBuilder b = new StringBuilder();
            b.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>").Append(Enc(h.PoNo)).Append("</title>");
            b.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:40px;color:#222}h1{color:#C8102E;margin:0}");
            b.Append("table{border-collapse:collapse;width:100%;margin-top:18px}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left}");
            b.Append("th{background:#f3f3f3}td.n,th.n{text-align:right}.meta td{border:none;padding:2px 8px 2px 0}.sig{margin-top:60px}");
            b.Append(".sig div{display:inline-block;width:260px;border-top:1px solid #333;margin-right:40px;padding-top:4px}</style></head><body>");
            b.Append("<h1>PURCHASE ORDER</h1><div>Solemate Kicks</div>");

            b.Append("<table class='meta'><tr><td><b>PO No.</b></td><td>").Append(Enc(h.PoNo)).Append("</td>");
            b.Append("<td><b>Date</b></td><td>").Append(h.DateCreated.ToString("MMMM d, yyyy")).Append("</td></tr>");
            b.Append("<tr><td><b>Supplier</b></td><td>").Append(Enc(h.Supplier)).Append(h.Country.Length > 0 ? " (" + Enc(h.Country) + ")" : "").Append("</td>");
            b.Append("<td><b>Payment terms</b></td><td>").Append(Enc(h.PaymentTerms)).Append("</td></tr>");
            b.Append("<tr><td><b>Address</b></td><td>").Append(Enc(h.SupplierAddress)).Append("</td>");
            b.Append("<td><b>Expected delivery</b></td><td>").Append(h.ExpectedDate.HasValue ? h.ExpectedDate.Value.ToString("MMMM d, yyyy") : "").Append("</td></tr>");
            b.Append("<tr><td><b>Requisition</b></td><td>").Append(Enc(h.RequisitionRef)).Append("</td>");
            b.Append("<td><b>Deliver to</b></td><td>Solemate warehouse (for ").Append(Enc(h.DeliverTo)).Append(")</td></tr></table>");

            b.Append("<table><tr><th>SKU</th><th>Brand</th><th>Product</th><th class='n'>Size (US)</th><th class='n'>Qty</th><th class='n'>Unit cost (PHP)</th><th class='n'>Amount (PHP)</th></tr>");
            foreach (PoItemRow i in items)
            {
                b.Append("<tr><td>").Append(Enc(i.Sku)).Append("</td><td>").Append(Enc(i.Brand)).Append("</td><td>").Append(Enc(i.Product))
                 .Append("</td><td class='n'>").Append(i.Size).Append("</td><td class='n'>").Append(i.Quantity)
                 .Append("</td><td class='n'>").Append(i.UnitCost.ToString("N2")).Append("</td><td class='n'>").Append(i.LineTotal.ToString("N2")).Append("</td></tr>");
            }
            b.Append("<tr><th colspan='6' class='n'>TOTAL</th><th class='n'>").Append(h.Total.ToString("N2")).Append("</th></tr></table>");

            b.Append("<div class='sig'><div>Prepared by: ").Append(Enc(preparedBy)).Append("</div><div>Approved by: ")
             .Append(Enc(h.ApprovedBy.Length > 0 ? h.ApprovedBy : preparedBy)).Append("</div></div>");
            b.Append("<p style='margin-top:30px;color:#666'>Please confirm this order and the delivery date by reply. Quote the PO number on the delivery receipt.</p>");
            b.Append("</body></html>");

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Solemate Purchase Orders");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, h.PoNo + ".html");
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(true));
            return path;
        }

        public static void Open(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static void OpenEmail(PoPrintHeader h, string fromName)
        {
            string subject = "Purchase order " + h.PoNo;
            string body = "Dear " + h.Supplier + "," + Environment.NewLine + Environment.NewLine +
                          "Please find attached our purchase order " + h.PoNo + " (payment terms: " + h.PaymentTerms + ")." + Environment.NewLine +
                          "Kindly confirm the order and the delivery date." + Environment.NewLine + Environment.NewLine +
                          "Regards," + Environment.NewLine + fromName + Environment.NewLine + "Solemate Kicks";
            string uri = "mailto:" + h.SupplierEmail + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body);
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
    }
}