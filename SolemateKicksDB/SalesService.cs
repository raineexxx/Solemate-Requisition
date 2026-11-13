using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SolemateKicks
{
    /// <summary>One line of the sale being rung up (right side of the Sales form).</summary>
    public class SaleLine
    {
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public decimal Price { get; set; }
        public int Available { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal { get { return Price * Quantity; } }
    }

    public class SaleRow
    {
        public int SaleId { get; set; }
        public string SaleNo { get; set; }
        public DateTime DateCreated { get; set; }
        public string Cashier { get; set; }
        public string Branch { get; set; }
        public int Lines { get; set; }
        public int Qty { get; set; }
        public decimal Total { get; set; }
    }

    public class SaleItemRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class SaleResult
    {
        public string SaleNo { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>Database work for the Sales module (Database\9_Sales.sql). Stock is deducted inside usp_SalCreate.</summary>
    public static class SalesService
    {
        /// <summary>Products with the quantity that can really be sold (stock minus reserved).</summary>
        public static List<CatalogRow> GetCatalog()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<CatalogRow>("EXEC dbo.usp_SalGetCatalog").ToList();
            }
        }

        public static SaleResult Create(IEnumerable<SaleLine> lines, string remarks, AppUser user)
        {
            List<SaleLine> list = lines.Where(l => l.Quantity > 0).ToList();
            if (list.Count == 0) throw new InvalidOperationException("Add at least one item to the sale.");

            string xml = new XElement("Items",
                list.Select(l => new XElement("Item",
                    new XAttribute("ProductId", l.ProductId),
                    new XAttribute("Quantity", l.Quantity)))).ToString(SaveOptions.DisableFormatting);

            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<SaleResult>("EXEC dbo.usp_SalCreate {0}, {1}, {2}",
                    user.UserId, string.IsNullOrWhiteSpace(remarks) ? string.Empty : remarks.Trim(), xml).First();
            }
        }

        public static List<SaleRow> GetRecent(AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<SaleRow>("EXEC dbo.usp_SalGetRecent {0}", user.UserId).ToList();
            }
        }

        public static List<SaleItemRow> GetItems(int saleId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                return db.ExecuteQuery<SaleItemRow>("EXEC dbo.usp_SalGetItems {0}", saleId).ToList();
            }
        }
    }
}