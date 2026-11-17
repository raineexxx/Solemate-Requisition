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
        public DateTime? ExpectedDate { get; set; }
        public string ReviewNote { get; set; }
        public string CreatedBy { get; set; }
        public string PrNo { get; set; }
        public DateTime? DateSent { get; set; }
    }

    /// <summary>A purchase requisition: the shortage of an approved requisition, waiting for the Head's budget approval.</summary>
    public class PurchaseRequisitionRow
    {
        public int PrId { get; set; }
        public string PrNo { get; set; }
        public string RequisitionRef { get; set; }
        public string Branch { get; set; }
        public DateTime DateCreated { get; set; }
        public string Status { get; set; }
        public int Items { get; set; }
        public int Pairs { get; set; }
        public decimal? EstimatedBudget { get; set; }   // cheapest complete supplier quotation, null when nobody quoted everything
        public string ReviewNote { get; set; }
        public string ReviewedBy { get; set; }
    }

    public class PrItemRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int RequestedQty { get; set; }
        public int FreeQty { get; set; }
        public int ShortQty { get; set; }
    }

    /// <summary>Line of a purchase order.</summary>
    public class PoItemRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal { get; set; }
    }

    /// <summary>Header data used to print / e-mail a purchase order.</summary>
    public class PoPrintHeader
    {
        public string PoNo { get; set; }
        public DateTime DateCreated { get; set; }
        public string Status { get; set; }
        public string Supplier { get; set; }
        public string SupplierAddress { get; set; }
        public string SupplierEmail { get; set; }
        public string Country { get; set; }
        public string PaymentTerms { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public string CreatedBy { get; set; }
        public string ApprovedBy { get; set; }
        public string RequisitionRef { get; set; }
        public string DeliverTo { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>An approved requisition that still needs something bought and has no active PO.</summary>
    public class ApprovedRequisitionRow
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Branch { get; set; }
        public DateTime DateCreated { get; set; }
        public int ItemCount { get; set; }   // number of items that are short

        // Label shown in the "New purchase order" dropdown.
        public override string ToString()
        {
            return RefNo + "  -  " + Branch + "  (" + ItemCount + " item(s) short)";
        }
    }

    /// <summary>
    /// One item of an approved request that still has quantity left to order.
    /// Selected and OrderQty are filled in by the Head on the New PO screen.
    /// </summary>
    public class PendingItemRow
    {
        public int RequisitionItemId { get; set; }
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Branch { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int RequestedQty { get; set; }
        public int OrderedQty { get; set; }
        public int RemainingQty { get; set; }
        public decimal UnitPrice { get; set; }

        // chosen on the screen (not read from the database)
        public bool Selected { get; set; }
        public int OrderQty { get; set; }

        // Worked out from OrderQty and UnitPrice. The empty setter is needed: LINQ to SQL fails on properties without one.
        public decimal LineTotal
        {
            get { return OrderQty * UnitPrice; }
            set { }
        }
    }

    /// <summary>One supplier's quotation for the whole shortage of a requisition.</summary>
    public class QuoteRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Country { get; set; }
        public string PaymentTerms { get; set; }
        public int LeadTimeDays { get; set; }
        public int Quoted { get; set; }
        public int Needed { get; set; }
        public decimal Total { get; set; }

        // These are methods (not properties) because LINQ to SQL fills every property from the query result.
        public bool IsComplete() { return Needed > 0 && Quoted == Needed; }
        public string Coverage() { return Quoted + " of " + Needed + " items"; }
    }

    /// <summary>A supplier's quoted unit cost for a product.</summary>
    public class QuotationRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal UnitCost { get; set; }
        public DateTime QuotedDate { get; set; }
    }

    /// <summary>One supplier's formal quotation for the items to buy of a requisition (with its total).</summary>
    public class ReqQuotationRow
    {
        public int QuotationId { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Country { get; set; }
        public string PaymentTerms { get; set; }
        public DateTime QuotationDate { get; set; }
        public decimal Total { get; set; }
        public bool IsLowest { get; set; }

        // Shown in the LOWEST column (worked out from IsLowest). The empty setter is needed: LINQ to SQL fails on properties without one.
        public string LowestText
        {
            get { return IsLowest ? "LOWEST" : ""; }
            set { }
        }
    }

    /// <summary>One item to buy with the unit price quoted by a supplier (edited in the quotation entry dialog).</summary>
    public class QuotationItemRow
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        // Worked out from Quantity and UnitPrice. The empty setter is needed: LINQ to SQL (ExecuteQuery) fails on properties without one.
        public decimal LineTotal
        {
            get { return Quantity * UnitPrice; }
            set { }
        }
    }

    public class SendResult
    {
        public string DeliveryRef { get; set; }
        public DateTime ExpectedDate { get; set; }
    }

    /// <summary>One supplier (name, address, email, country, payment terms, lead time).</summary>
    public class SupplierRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Address { get; set; }
        public string Email { get; set; }
        public string Country { get; set; }
        public string PaymentTerms { get; set; }
        public int LeadTimeDays { get; set; }

        // Label shown in the supplier dropdown (the country shows which supplier is the UK one and which is the Vietnam one).
        public override string ToString()
        {
            return string.IsNullOrEmpty(Country) ? SupplierName : SupplierName + "  (" + Country + ")";
        }
    }

    /// <summary>
    /// All database work for the Purchasing module (purchase orders, quotations and suppliers).
    /// Errors raised by THROW in SQL arrive here as exceptions whose Message is the SQL text.
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

        public static List<PoItemRow> GetOrderItems(int purchaseOrderId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PoItemRow>("EXEC dbo.usp_PurGetOrderItems {0}", purchaseOrderId).ToList();
            }
        }

        public static PoPrintHeader GetPrintHeader(int purchaseOrderId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PoPrintHeader>("EXEC dbo.usp_PurGetPrintHeader {0}", purchaseOrderId).First();
            }
        }

        public static List<ApprovedRequisitionRow> GetApprovedRequisitions()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ApprovedRequisitionRow>("EXEC dbo.usp_PurGetApprovedRequisitions").ToList();
            }
        }

        /// <summary>Items of approved requests that still have quantity left to order.</summary>
        public static List<PendingItemRow> GetPendingItems()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PendingItemRow>("EXEC dbo.usp_PurGetPendingItems").ToList();
            }
        }

        /// <summary>
        /// ITEM-SELECTION FLOW: creates a PO for one supplier from the approved items the Head ticked (each with the
        /// quantity to order), at the products' current prices. Head only (checked in SQL).
        /// Payment terms may be blank. Returns the new PurchaseOrderId.
        /// Calls usp_PurCreateOrder (13_SelectItems.sql).
        /// </summary>
        public static int CreateOrder(int supplierId, string paymentTerms, IEnumerable<PendingItemRow> items, AppUser user)
        {
            StringBuilder xml = new StringBuilder("<items>");
            foreach (PendingItemRow it in items)
            {
                xml.Append("<i r=\"").Append(it.RequisitionItemId.ToString(CultureInfo.InvariantCulture))
                   .Append("\" q=\"").Append(it.OrderQty.ToString(CultureInfo.InvariantCulture))
                   .Append("\"/>");
            }
            xml.Append("</items>");

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_PurCreateOrder {0}, {1}, {2}, {3}",
                    supplierId, paymentTerms ?? string.Empty, xml.ToString(), user.UserId).First();
            }
        }

        /// <summary>Supplier quotation comparison for the shortage. The first complete row is the cheapest.</summary>
        public static List<QuoteRow> GetQuotes(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<QuoteRow>("EXEC dbo.usp_PurGetQuotes {0}", requisitionId).ToList();
            }
        }

        // ------------------------------------------------------------------
        //  Purchase requisitions (created by the system for the shortage; the Head checks the budget)
        // ------------------------------------------------------------------
        public static List<PurchaseRequisitionRow> GetPurchaseRequisitions()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PurchaseRequisitionRow>("EXEC dbo.usp_PurGetPRs").ToList();
            }
        }

        public static List<PrItemRow> GetPrItems(int prId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<PrItemRow>("EXEC dbo.usp_PurGetPRItems {0}", prId).ToList();
            }
        }

        /// <summary>Head only: approve the budget of a purchase requisition, or hold it (a reason is required).</summary>
        public static void DecidePr(int prId, bool approve, string note, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_PurDecidePR {0}, {1}, {2}, {3}", prId, user.UserId, approve ? 1 : 0, note ?? string.Empty);
            }
        }

        /// <summary>Manager: send the approved PO to the supplier. Creates the expected delivery linked to the PO.</summary>
        public static SendResult SendOrder(int purchaseOrderId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<SendResult>("EXEC dbo.usp_PurSend {0}, {1}", purchaseOrderId, user.UserId).First();
            }
        }

        public static void CancelOrder(int purchaseOrderId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_PurCancel {0}, {1}", purchaseOrderId, user.UserId);
            }
        }

        public static int GetPendingApprovalCount()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_PurGetPendingCount").First();
            }
        }

        // ------------------------------------------------------------------
        //  Suppliers (CRUD) + terms
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

        /// <summary>Add (supplierId = 0) or edit a supplier. Returns the SupplierId.</summary>
        public static int SaveSupplier(int supplierId, string name, string address, string email, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_SupSave {0}, {1}, {2}, {3}, {4}",
                    supplierId, name ?? string.Empty, address ?? string.Empty, email ?? string.Empty, user.UserId).First();
            }
        }

        public static void SaveSupplierTerms(int supplierId, string country, string paymentTerms, int leadTimeDays, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupSaveTerms {0}, {1}, {2}, {3}, {4}",
                    supplierId, country ?? string.Empty, paymentTerms ?? string.Empty, leadTimeDays, user.UserId);
            }
        }

        public static void DeleteSupplier(int supplierId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupDelete {0}, {1}", supplierId, user.UserId);
            }
        }

        // ------------------------------------------------------------------
        //  Quotations (supplier prices)
        // ------------------------------------------------------------------
        public static List<QuotationRow> GetQuotations(int supplierId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<QuotationRow>("EXEC dbo.usp_SupPriceList {0}", supplierId).ToList();
            }
        }

        public static void SaveQuotation(int supplierId, int productId, decimal unitCost, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupPriceSave {0}, {1}, {2}, {3}", supplierId, productId, unitCost, user.UserId);
            }
        }

        public static void DeleteQuotation(int supplierId, int productId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupPriceDelete {0}, {1}, {2}", supplierId, productId, user.UserId);
            }
        }

        // ------------------------------------------------------------------
        //  Formal quotations per requisition (paper 3.3: quotations from the UK and Vietnam suppliers, lowest price)
        // ------------------------------------------------------------------
        public static List<ReqQuotationRow> GetRequisitionQuotations(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<ReqQuotationRow>("EXEC dbo.usp_QuoGetForRequisition {0}", requisitionId).ToList();
            }
        }

        /// <summary>Items to buy with the price this supplier already quoted (or its price-list cost as a suggestion, 0 if none).</summary>
        public static List<QuotationItemRow> GetQuotationItems(int requisitionId, int supplierId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<QuotationItemRow>("EXEC dbo.usp_QuoGetItems {0}, {1}", requisitionId, supplierId).ToList();
            }
        }

        /// <summary>Adds or updates a supplier quotation for a requisition (Manager or Head, checked in SQL). Returns the QuotationId.</summary>
        public static int SaveRequisitionQuotation(int requisitionId, int supplierId, string paymentTerms,
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
    }
}