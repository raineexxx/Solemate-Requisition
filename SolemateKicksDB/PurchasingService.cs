using System;
using System.Collections.Generic;
using System.Linq;

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
    }

    /// <summary>An approved requisition that does not have an active purchase order yet.</summary>
    public class ApprovedRequisitionRow
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Branch { get; set; }
        public DateTime DateCreated { get; set; }
        public int ItemCount { get; set; }

        // Label shown in the "New purchase order" dropdown.
        public override string ToString()
        {
            return RefNo + "  -  " + Branch + "  (" + ItemCount + " items)";
        }
    }

    /// <summary>One supplier (name, address, email).</summary>
    public class SupplierRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string Address { get; set; }
        public string Email { get; set; }

        // Label shown in the supplier dropdown.
        public override string ToString()
        {
            return SupplierName;
        }
    }

    /// <summary>
    /// All database work for the Purchasing module (purchase orders and suppliers).
    /// Calls the usp_Pur... and usp_Sup... procedures from 4_Purchasing.sql, 5_PurchasingRoles.sql
    /// and 6_Suppliers.sql. Errors raised by THROW in SQL arrive here as exceptions whose
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

        /// <summary>Creates a PO from an approved requisition for a supplier. Returns the new PurchaseOrderId.</summary>
        public static int CreateOrder(int requisitionId, int supplierId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>("EXEC dbo.usp_PurCreateOrder {0}, {1}, {2}",
                    requisitionId, supplierId, user.UserId).First();
            }
        }

        /// <summary>Receives a PO: adds its quantities to stock. Only Head / Manager is allowed (checked in SQL). Used by the Delivery module.</summary>
        public static void ReceiveOrder(int purchaseOrderId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_PurReceiveOrder {0}, {1}", purchaseOrderId, user.UserId);
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

        public static void DeleteSupplier(int supplierId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                db.ExecuteCommand("EXEC dbo.usp_SupDelete {0}, {1}", supplierId, user.UserId);
            }
        }
    }
}
