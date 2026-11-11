using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SolemateKicks
{
    // ------------------------------------------------------------------
    //  Rows returned by the usp_Del... procedures
    //  (property names must match the column names the procedure returns)
    // ------------------------------------------------------------------
    public class DeliveryRow
    {
        public int DeliveryId { get; set; }
        public string DeliveryRef { get; set; }
        public string PoRef { get; set; }
        public string SupplierName { get; set; }
        public string RequisitionRef { get; set; }
        public string Status { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateReceived { get; set; }
        public int Lines { get; set; }
        public int OrderedQty { get; set; }
        public int AcceptedQty { get; set; }
        public int RejectedQty { get; set; }
        public int OutstandingQty { get; set; }
        public string CreatedBy { get; set; }
        public string Remarks { get; set; }
    }

    public class DeliveryItemRow
    {
        public int ItemId { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int OrderedQty { get; set; }
        public int AcceptedQty { get; set; }
        public int RejectedQty { get; set; }
        public int OutstandingQty { get; set; }
        public string Note { get; set; }
    }

    public class ProductPick
    {
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Display { get; set; }
        public int Stock { get; set; }
    }

    public class ApprovedRequisitionPick
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Display { get; set; }
        public int HasShortage { get; set; }
    }

    public class ShortageRow
    {
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Display { get; set; }
        public int Shortage { get; set; }
        public int Stock { get; set; }
    }

    public class ReceivingReportRow
    {
        public string ReportRef { get; set; }
        public DateTime DateReceived { get; set; }
        public string ReceivedBy { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
    }

    public class DiscrepancyRow
    {
        public string DiscrepancyType { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Qty { get; set; }
        public string Note { get; set; }
        public DateTime DateLogged { get; set; }
        public string ByUser { get; set; }
    }

    /// <summary>What usp_DelReceive returns after a receipt.</summary>
    public class ReceiveResult
    {
        public string ReportRef { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
        public int Outstanding { get; set; }
        public string Status { get; set; }
        public string RequisitionRef { get; set; }
        public int RequisitionReady { get; set; }
    }

    // ------------------------------------------------------------------
    //  Lines edited on screen (not read from the database directly)
    // ------------------------------------------------------------------
    public class DeliveryLine
    {
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Display { get; set; }
        public int OrderedQty { get; set; }
    }

    public class ReceiveLine
    {
        public int ItemId { get; set; }
        public string Sku { get; set; }
        public string Display { get; set; }
        public int OrderedQty { get; set; }
        public int AcceptedQty { get; set; }
        public int OutstandingQty { get; set; }
        public int AcceptNow { get; set; }
        public int RejectNow { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// All database work for the Delivery module. Same style as InventoryService:
    /// it calls the usp_Del... procedures from 4_Delivery.sql with db.ExecuteQuery,
    /// so Solemate.dbml does not need to be changed.
    /// Errors raised by THROW in SQL arrive here as exceptions whose Message is the SQL text.
    /// </summary>
    public static class DeliveryService
    {
        public static List<DeliveryRow> GetDeliveries()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DeliveryRow>("EXEC dbo.usp_DelGetList {0}", "All").ToList();
            }
        }

        public static List<DeliveryItemRow> GetItems(int deliveryId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DeliveryItemRow>("EXEC dbo.usp_DelGetItems {0}", deliveryId).ToList();
            }
        }

        public static List<ProductPick> GetProducts()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ProductPick>("EXEC dbo.usp_DelGetProducts").ToList();
            }
        }

        public static List<ApprovedRequisitionPick> GetApprovedRequisitions()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ApprovedRequisitionPick>("EXEC dbo.usp_DelGetApprovedRequisitions").ToList();
            }
        }

        public static List<ShortageRow> GetShortage(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ShortageRow>("EXEC dbo.usp_DelGetShortage {0}", requisitionId).ToList();
            }
        }

        /// <summary>Records an expected delivery. requisitionId = 0 means not linked. Returns the DEL- reference.</summary>
        public static string CreateDelivery(AppUser user, string supplier, string poRef, int requisitionId,
            string remarks, IEnumerable<DeliveryLine> lines)
        {
            List<DeliveryLine> items = lines.ToList();
            if (string.IsNullOrWhiteSpace(supplier))
                throw new InvalidOperationException("Supplier is required.");
            if (items.Count == 0)
                throw new InvalidOperationException("Add at least one item to the delivery.");
            if (items.Any(l => l.OrderedQty < 1))
                throw new InvalidOperationException("Every ordered quantity must be at least 1.");

            XElement root = new XElement("Items",
                items.Select(l => new XElement("Item",
                    new XAttribute("ProductId", l.ProductId),
                    new XAttribute("Quantity", l.OrderedQty))));

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<string>(
                    "EXEC dbo.usp_DelCreate {0}, {1}, {2}, {3}, {4}, {5}",
                    user.UserId, supplier.Trim(), poRef ?? string.Empty, requisitionId,
                    remarks ?? string.Empty, root.ToString(SaveOptions.DisableFormatting)).First();
            }
        }

        /// <summary>Inspect a delivery: accepted goes to stock, rejected is logged, the rest stays outstanding.</summary>
        public static ReceiveResult Receive(int deliveryId, AppUser user, IEnumerable<ReceiveLine> lines,
            bool closeShort, string remarks)
        {
            XElement root = new XElement("Lines",
                lines.Where(l => l.AcceptNow > 0 || l.RejectNow > 0)
                     .Select(l => new XElement("Line",
                         new XAttribute("ItemId", l.ItemId),
                         new XAttribute("Accepted", l.AcceptNow),
                         new XAttribute("Rejected", l.RejectNow),
                         new XAttribute("Note", l.Note ?? string.Empty))));

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReceiveResult>(
                    "EXEC dbo.usp_DelReceive {0}, {1}, {2}, {3}, {4}",
                    deliveryId, user.UserId, root.ToString(SaveOptions.DisableFormatting),
                    closeShort ? 1 : 0, remarks ?? string.Empty).First();
            }
        }

        public static List<ReceivingReportRow> GetReports(int deliveryId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReceivingReportRow>("EXEC dbo.usp_DelGetReports {0}", deliveryId).ToList();
            }
        }

        public static List<DiscrepancyRow> GetDiscrepancies(int deliveryId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<DiscrepancyRow>("EXEC dbo.usp_DelGetDiscrepancies {0}", deliveryId).ToList();
            }
        }
    }
}