using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace SolemateKicks
{
    /// <summary>
    /// Builds the written purchase order (an HTML page that opens in the browser and can be printed or saved as PDF)
    /// and opens a new email to the supplier. This replaces phone-only orders.
    /// </summary>
    public static class PoDocument
    {
        /// <summary>Saves the written PO under Documents\Solemate Purchase Orders and returns the file path.</summary>
        public static string Save(PurchaseOrderHeader h, List<PurchaseOrderItemRow> items, string preparedBy)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Solemate Purchase Orders");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, h.PoNo + ".html");
            File.WriteAllText(path, Build(h, items, preparedBy), Encoding.UTF8);
            return path;
        }

        public static void Open(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        /// <summary>Opens the default mail program with a new message to the supplier (the PO file is attached by hand).</summary>
        public static void OpenEmail(PurchaseOrderHeader h, string preparedBy)
        {
            string subject = "Purchase Order " + h.PoNo + " - Solemate Kicks";
            string body = "Dear " + h.Supplier + ","
                + "\r\n\r\nPlease find attached our purchase order " + h.PoNo + " (total PHP "
                + h.Total.ToString("N2", CultureInfo.InvariantCulture) + ")."
                + "\r\nPayment terms: " + h.PaymentTerms + "."
                + "\r\n\r\nPlease confirm receipt of this order."
                + "\r\n\r\nThank you,\r\n" + preparedBy + "\r\nSolemate Kicks";

            string mailto = "mailto:" + Uri.EscapeDataString(h.SupplierEmail)
                + "?subject=" + Uri.EscapeDataString(subject)
                + "&body=" + Uri.EscapeDataString(body);
            Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
        }

        private static string Build(PurchaseOrderHeader h, List<PurchaseOrderItemRow> items, string preparedBy)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>")
              .Append(E(h.PoNo)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:40px;color:#222}")
              .Append("h1{margin:0;color:#b71c1c}h2{margin:4px 0 24px;font-weight:normal;color:#555}")
              .Append("table{border-collapse:collapse;width:100%;margin-top:18px}")
              .Append("th,td{border:1px solid #bbb;padding:8px;text-align:left}th{background:#f2f2f2}")
              .Append(".r{text-align:right}.meta td{border:none;padding:3px 0}.sig{margin-top:60px}")
              .Append("</style></head><body>");

            sb.Append("<h1>Solemate Kicks</h1><h2>PURCHASE ORDER</h2>");

            sb.Append("<table class=\"meta\"><tr><td><b>PO No.:</b> ").Append(E(h.PoNo)).Append("</td>")
              .Append("<td><b>Date:</b> ").Append(h.DateCreated.ToString("d MMMM yyyy")).Append("</td></tr>")
              .Append("<tr><td><b>Requisition:</b> ").Append(E(h.RequisitionRef)).Append("</td>")
              .Append("<td><b>Deliver to:</b> ").Append(E(h.Branch)).Append("</td></tr>")
              .Append("<tr><td colspan=\"2\"><b>Payment terms:</b> ").Append(E(h.PaymentTerms)).Append("</td></tr></table>");

            sb.Append("<p><b>Supplier</b><br>").Append(E(h.Supplier)).Append("<br>")
              .Append(E(h.SupplierAddress)).Append("<br>").Append(E(h.SupplierEmail)).Append("</p>");

            sb.Append("<table><tr><th>#</th><th>Item</th><th class=\"r\">Qty</th><th class=\"r\">Unit price (PHP)</th><th class=\"r\">Amount (PHP)</th></tr>");
            int n = 1;
            foreach (PurchaseOrderItemRow i in items)
            {
                sb.Append("<tr><td>").Append(n++).Append("</td><td>").Append(E(i.ProductName)).Append("</td>")
                  .Append("<td class=\"r\">").Append(i.Quantity).Append("</td>")
                  .Append("<td class=\"r\">").Append(i.UnitCost.ToString("N2", CultureInfo.InvariantCulture)).Append("</td>")
                  .Append("<td class=\"r\">").Append(i.LineTotal.ToString("N2", CultureInfo.InvariantCulture)).Append("</td></tr>");
            }
            sb.Append("<tr><td colspan=\"4\" class=\"r\"><b>TOTAL</b></td><td class=\"r\"><b>")
              .Append(h.Total.ToString("N2", CultureInfo.InvariantCulture)).Append("</b></td></tr></table>");

            sb.Append("<div class=\"sig\">Approved by: ______________________<br>")
              .Append(E(preparedBy)).Append(", Head</div>");

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string E(string s)
        {
            return WebUtility.HtmlEncode(s ?? string.Empty);
        }
    }
}
