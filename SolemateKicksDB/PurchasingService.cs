using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SolemateKicks
{
    /// <summary>One row of the purchase order list.</summary>
    public class PurchaseOrderRow
    {
        public int PurchaseOrderId { get; set; }
        public string PoNo { get; set; }
        public DateTime DateCreated { get; set; }
        public string Supplier { get; set; }
        public string Status { get; set; }
        public decimal Total { get; set; }
        public string RequisitionRef { get; set; }
        public string PaymentTerms { get; set; }
        public DateTime? DateSent { get; set; }

        // Shown in the SENT column (worked out from DateSent, not read from the database).
        // The empty setter is needed: LINQ to SQL (ExecuteQuery) fails on properties without one.
        public string SentText
        {
            get { return DateSent.HasValue ? "Sent " + DateSent.Value.ToString("d MMM yyyy") : "Not sent"; }
            set { }
        }
    }

    /// <summary>An approved requisition that does not have an active purchase order yet.</summary>
    public class ApprovedRequisitionRow
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Branch { get; set; }
        public DateTime DateCreated { get; set; }
        public int ItemCount { get; set; }

        // Label shown in the requisition dropdown.
        public override string ToString()
        {
            return RefNo + "  -  " + Branch + "  (" + ItemCount + " items)";
        }
    }

    /// <summary>One supplier (name, address, email, country).</summary>
    public class SupplierRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Address { get; set; }
        public string Email { get; set; }
        public string Country { get; set; }

        // Label shown in the supplier dropdown.
        public override string ToString()
        {
            return string.IsNullOrEmpty(Country) ? SupplierName : SupplierName + "  (" + Country + ")";
        }
    }

    /// <summary>One supplier quotation for a requisition (with its total).</summary>
    public class QuotationRow
    {
        public int QuotationId { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Country { get; set; }
        public string PaymentTerms { get; set; }
        public DateTime QuotationDate { get; set; }
        public decimal Total { get; set; }
        public bool IsLowest { get; set; }

        // Shown in the LOWEST column (worked out from IsLowest, not read from the database).
        // The empty setter is needed: LINQ to SQL (ExecuteQuery) fails on properties without one.
        public string LowestText
        {
            get { return IsLowest ? "LOWEST" : ""; }
            set { }
        }
    }

    /// <summary>One requisition item with the unit price quoted by a supplier.</summary>
    public class QuotationItemRow
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        // Worked out from Quantity and UnitPrice. The empty setter is needed: LINQ to SQL fails on properties without one.
        public decimal LineTotal
        {
            get { return Quantity * UnitPrice; }
            set { }
        }
    }

    /// <summary>Header of a purchase order, used for the written PO.</summary>
    public class PurchaseOrderHeader
    {
        public string PoNo { get; set; }
        public DateTime DateCreated { get; set; }
        public string Supplier { get; set; }
        public string SupplierAddress { get; set; }
        public string SupplierEmail { get; set; }
        public string SupplierCountry { get; set; }
        public string PaymentTerms { get; set; }
        public string RequisitionRef { get; set; }
        public string Branch { get; set; }
        public string Status { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>One line of a purchase order, used for the written PO.</summary>
    public class PurchaseOrderItemRow
    {
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal { get; set; }
    }

    /// <summary>
    /// All database work for the Purchasing module (quotations, purchase orders and suppliers).
    /// Calls the usp_Pur..., usp_Quo... and usp_Sup... procedures from 4_Purchasing.sql, 5_PurchasingRoles.sql,
    /// 8_Suppliers.sql and 9_Quotations.sql. Errors raised by THROW in SQL arrive here as exceptions whose
    /// Message is the SQL text.
    /// </summary>
    public static class PurchasingService
    {
        // ------------------------------------------------------------------
        //  Purchase orders
        // ------------------------------------------------------------------
        public static List<PurchaseOrderRow> GetPurchaseOrders()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PurchaseOrderRow>("EXEC dbo.usp_PurGetOrders").ToList();
            }
        }

        public static List<ApprovedRequisitionRow> GetApprovedRequisitions()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ApprovedRequisitionRow>("EXEC dbo.usp_PurGetApprovedRequisitions").ToList();
            }
        }

        /// <summary>
        /// Creates a PO from an approved requisition and the quotation of the chosen supplier (Head only, needs at least
        /// two quotations; checked in SQL). Returns the new PurchaseOrderId.
        /// </summary>
        public static int CreateOrder(int requisitionId, int supplierId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_PurCreateOrder {0}, {1}, {2}",
                    requisitionId, supplierId, user.UserId).First();
            }
        }

        public static PurchaseOrderHeader GetOrderHeader(int purchaseOrderId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PurchaseOrderHeader>("EXEC dbo.usp_PurGetOrderHeader {0}", purchaseOrderId).First();
            }
        }

        public static List<PurchaseOrderItemRow> GetOrderItems(int purchaseOrderId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PurchaseOrderItemRow>("EXEC dbo.usp_PurGetOrderItems {0}", purchaseOrderId).ToList();
            }
        }

        /// <summary>Records that the written PO was sent to the supplier. Head only (checked in SQL).</summary>
        public static void MarkOrderSent(int purchaseOrderId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_PurMarkSent {0}, {1}", purchaseOrderId, user.UserId);
            }
        }

        /// <summary>Receives a PO: adds its quantities to stock. Only the Manager is allowed (checked in SQL). Used by the Delivery module.</summary>
        public static void ReceiveOrder(int purchaseOrderId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_PurReceiveOrder {0}, {1}", purchaseOrderId, user.UserId);
            }
        }

        // ------------------------------------------------------------------
        //  Quotations
        // ------------------------------------------------------------------
        public static List<QuotationRow> GetQuotations(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<QuotationRow>("EXEC dbo.usp_QuoGetForRequisition {0}", requisitionId).ToList();
            }
        }

        /// <summary>Requisition items with the price this supplier already quoted (0 when there is no quotation yet).</summary>
        public static List<QuotationItemRow> GetQuotationItems(int requisitionId, int supplierId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<QuotationItemRow>("EXEC dbo.usp_QuoGetItems {0}, {1}", requisitionId, supplierId).ToList();
            }
        }

        /// <summary>Adds or updates a supplier quotation. Head only (checked in SQL). Returns the QuotationId.</summary>
        public static int SaveQuotation(int requisitionId, int supplierId, string paymentTerms,
                                        IEnumerable<QuotationItemRow> items, AppUser user)
        {
            StringBuilder xml = new StringBuilder("<items>");
            foreach (QuotationItemRow it in items)
            {
                xml.Append("<i p=\"").Append(it.ProductId.ToString(CultureInfo.InvariantCulture))
                   .Append("\" u=\"").Append(it.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture))
                   .Append("\"/>");
            }
            xml.Append("</items>");

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_QuoSave {0}, {1}, {2}, {3}, {4}",
                    requisitionId, supplierId, paymentTerms ?? string.Empty, xml.ToString(), user.UserId).First();
            }
        }

        // ------------------------------------------------------------------
        //  Suppliers (CRUD)
        // ------------------------------------------------------------------
        public static List<SupplierRow> GetSuppliers()
        {
            return GetSuppliers(false);
        }
        public static List<SupplierRow> GetSuppliers(bool activeOnly)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<SupplierRow>("EXEC dbo.usp_SupGetAll {0}", activeOnly ? 1 : 0).ToList();
            }
        }

        /// <summary>Add (supplierId = 0) or edit a supplier. Head only (checked in SQL). Returns the SupplierId.</summary>
        public static int SaveSupplier(int supplierId, string name, string address, string email, string country, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_SupSave {0}, {1}, {2}, {3}, {4}, {5}",
                    supplierId, name ?? string.Empty, address ?? string.Empty, email ?? string.Empty,
                    country ?? string.Empty, user.UserId).First();
            }
        }

        public static void DeleteSupplier(int supplierId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupDelete {0}, {1}", supplierId, user.UserId);
            }
        }

        /// <summary>All purchase orders of one supplier, newest first.</summary>
        public static List<PurchaseOrderRow> GetSupplierHistory(int supplierId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PurchaseOrderRow>("EXEC dbo.usp_SupOrderHistory {0}", supplierId).ToList();
            }
        }
    }
}