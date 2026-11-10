using System;
using System.Collections.Generic;
using System.Linq;

namespace SolemateKicks
{
    /// <summary>One row of the stock history.</summary>
    public class StockMovementRow
    {
        public int MovementId { get; set; }
        public DateTime DateCreated { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int QtyChange { get; set; }
        public int StockAfter { get; set; }
        public string Reason { get; set; }
        public string ByUser { get; set; }
    }

    /// <summary>
    /// All database work for the Inventory module. Same idea as RequisitionService, but it calls the
    /// usp_Inv... procedures from Database\Inventory.sql with db.ExecuteQuery / db.ExecuteCommand,
    /// so Solemate.dbml does not need to be changed.
    /// Errors raised by THROW in SQL arrive here as exceptions whose Message is the SQL text.
    /// </summary>
    public static class InventoryService
    {
        /// <summary>Every product with its stock (reuses CatalogRow, so StockStatus = OK / LOW / OUT).</summary>
        public static List<CatalogRow> GetStock()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<CatalogRow>("EXEC dbo.usp_InvGetStock").ToList();
            }
        }

        /// <summary>Add (productId = 0) or edit a product. Returns the ProductId.</summary>
        public static int SaveProduct(int productId, int brandId, string sku, string productName,
            int size, decimal price, int reorderLevel, int openingStock, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>(
                    "EXEC dbo.usp_InvSaveProduct {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}",
                    productId, brandId, sku ?? string.Empty, productName ?? string.Empty,
                    size, price, reorderLevel, openingStock, user.UserId).First();
            }
        }

        /// <summary>Stock in (positive) or correction (negative). Returns the new stock level.</summary>
        public static int AdjustStock(int productId, int qtyChange, string reason, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<int>(
                    "EXEC dbo.usp_InvAdjustStock {0}, {1}, {2}, {3}",
                    productId, qtyChange, reason ?? string.Empty, user.UserId).First();
            }
        }

        /// <summary>Recent stock history. productId = 0 returns all products.</summary>
        public static List<StockMovementRow> GetMovements(int productId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<StockMovementRow>(
                    "EXEC dbo.usp_InvGetMovements {0}", productId).ToList();
            }
        }
    }
}
