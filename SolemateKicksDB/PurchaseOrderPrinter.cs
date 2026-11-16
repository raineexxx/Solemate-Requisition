using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace SolemateKicks
{
    /// <summary>
    /// Prints a purchase order (opens a clean page in the default browser, where it can be printed or saved
    /// as PDF) and prepares an e-mail to the supplier (paper 3.3: "sends it to the supplier").
    /// </summary>
    public static class PurchaseOrderPrinter
    {
        private static string H(string s)
        {
            return WebUtility.HtmlEncode(s ?? string.Empty);
        }

        public static string BuildHtml(PoPrintHeader h, List<PoItemRow> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>").Append(H(h.PoNo)).Append("</title><style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;color:#181818;margin:40px;}");
            sb.Append("h1{color:#E60000;font-style:italic;margin:0;}");
            sb.Append(".bar{height:6px;background:#E60000;margin:8px 0 18px;}");
            sb.Append("table{border-collapse:collapse;width:100%;margin-top:14px;}");
            sb.Append("th{background:#E60000;color:#fff;text-align:left;padding:8px;}");
            sb.Append("td{padding:7px 8px;border-bottom:1px solid #ddd;}");
            sb.Append(".r{text-align:right;}.meta td{border:none;padding:3px 8px 3px 0;}");
            sb.Append(".total{font-size:18px;font-weight:bold;text-align:right;margin-top:14px;}");
            sb.Append(".sign{margin-top:60px;display:flex;gap:60px;}.sign div{border-top:1px solid #181818;padding-top:6px;width:240px;}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>SOLEMATE KICKS</h1><div class='bar'></div>");
            sb.Append("<h2>PURCHASE ORDER ").Append(H(h.PoNo)).Append("</h2>");

            sb.Append("<table class='meta'><tr><td><b>Supplier:</b></td><td>").Append(H(h.Supplier)).Append("</td>");
            sb.Append("<td><b>Date:</b></td><td>").Append(h.DateCreated.ToString("MMM dd, yyyy")).Append("</td></tr>");
            sb.Append("<tr><td><b>Address:</b></td><td>").Append(H(h.SupplierAddress)).Append("</td>");
            sb.Append("<td><b>Requisition:</b></td><td>").Append(H(h.RequisitionRef)).Append("</td></tr>");
            sb.Append("<tr><td><b>E-mail:</b></td><td>").Append(H(h.SupplierEmail)).Append("</td>");
            sb.Append("<td><b>Deliver to:</b></td><td>").Append(H(h.DeliverTo)).Append("</td></tr>");
            sb.Append("<tr><td><b>Payment terms:</b></td><td>").Append(H(h.PaymentTerms)).Append("</td>");
            sb.Append("<td><b>Expected delivery:</b></td><td>")
              .Append(h.ExpectedDate.HasValue ? h.ExpectedDate.Value.ToString("MMM dd, yyyy") : "to be confirmed").Append("</td></tr></table>");

            sb.Append("<table><tr><th>SKU</th><th>Item</th><th>Size (US)</th><th class='r'>Qty</th><th class='r'>Unit cost</th><th class='r'>Amount</th></tr>");
            foreach (PoItemRow i in items)
            {
                sb.Append("<tr><td>").Append(H(i.Sku)).Append("</td><td>").Append(H(i.Brand + " " + i.Product)).Append("</td><td>")
                  .Append(i.Size).Append("</td><td class='r'>").Append(i.Quantity).Append("</td><td class='r'>")
                  .Append(i.UnitCost.ToString("N2")).Append("</td><td class='r'>").Append(i.LineTotal.ToString("N2")).Append("</td></tr>");
            }
            sb.Append("</table>");
            sb.Append("<div class='total'>TOTAL: PHP ").Append(h.Total.ToString("N2")).Append("</div>");

            sb.Append("<div class='sign'><div>Prepared by: ").Append(H(h.CreatedBy)).Append("</div>");
            sb.Append("<div>Budget approved by: ").Append(string.IsNullOrEmpty(h.ApprovedBy) ? "(pending)" : H(h.ApprovedBy)).Append("</div></div>");
            sb.Append("<script>window.onload=function(){window.print();};</script></body></html>");
            return sb.ToString();
        }

        /// <summary>Writes the PO page to a temp file and opens it (the browser's print dialog opens automatically).</summary>
        public static void Print(int purchaseOrderId)
        {
            PoPrintHeader h = PurchasingService.GetPrintHeader(purchaseOrderId);
            List<PoItemRow> items = PurchasingService.GetOrderItems(purchaseOrderId);
            string path = Path.Combine(Path.GetTempPath(), h.PoNo + ".html");
            File.WriteAllText(path, BuildHtml(h, items), new UTF8Encoding(true));
            Process.Start(path);
        }

        /// <summary>Opens the default mail program with a ready-to-send message to the supplier.</summary>
        public static void Email(int purchaseOrderId)
        {
            PoPrintHeader h = PurchasingService.GetPrintHeader(purchaseOrderId);
            if (string.IsNullOrWhiteSpace(h.SupplierEmail))
                throw new InvalidOperationException("This supplier has no e-mail address. Add it in the SUPPLIERS tab first.");

            List<PoItemRow> items = PurchasingService.GetOrderItems(purchaseOrderId);
            StringBuilder body = new StringBuilder();
            body.AppendLine("Good day,");
            body.AppendLine();
            body.AppendLine("Please find our purchase order " + h.PoNo + " dated " + h.DateCreated.ToString("MMM dd, yyyy") + ".");
            body.AppendLine();
            foreach (PoItemRow i in items.Take(30))
                body.AppendLine(i.Quantity + " x " + i.Sku + " " + i.Brand + " " + i.Product + " (US " + i.Size + ") @ " + i.UnitCost.ToString("N2"));
            if (items.Count > 30) body.AppendLine("... and " + (items.Count - 30) + " more line(s).");
            body.AppendLine();
            body.AppendLine("Total: PHP " + h.Total.ToString("N2"));
            if (!string.IsNullOrEmpty(h.PaymentTerms)) body.AppendLine("Payment terms: " + h.PaymentTerms);
            if (h.ExpectedDate.HasValue) body.AppendLine("Expected delivery: " + h.ExpectedDate.Value.ToString("MMM dd, yyyy"));
            body.AppendLine();
            body.AppendLine("Please confirm receipt of this order. Thank you.");
            body.AppendLine("Solemate Kicks");

            string uri = "mailto:" + h.SupplierEmail.Trim()
                       + "?subject=" + Uri.EscapeDataString("Purchase order " + h.PoNo)
                       + "&body=" + Uri.EscapeDataString(body.ToString());
            Process.Start(uri);
        }
    }
}
