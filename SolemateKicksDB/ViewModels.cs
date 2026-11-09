using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SolemateKicks
{
    /// <summary>Who is logged in right now.</summary>
    public static class Session
    {
        public static AppUser CurrentUser { get; set; }
    }

    /// <summary>One row of the product catalog grid (left side of the requisition form).</summary>
    public class CatalogRow
    {
        public int ProductId { get; set; }
        public int BrandId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int ReorderLevel { get; set; }

        // Calculated from Stock / ReorderLevel. The empty setter is required because LINQ to SQL
        // tries to fill every property of a result class; the value is never taken from the database.
        public string StockStatus
        {
            get { return Stock <= 0 ? "OUT" : (Stock <= ReorderLevel ? "LOW" : "OK"); }
            set { }
        }
    }

    /// <summary>One line of the requisition being built (right side of the requisition form).</summary>
    public class CartLine : INotifyPropertyChanged
    {
        private int _quantity;

        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Stock { get; set; }

        public int Quantity
        {
            get { return _quantity; }
            set
            {
                if (_quantity == value) return;
                _quantity = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("Quantity"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One row of the requisition list on the main screen.</summary>
    public class RequisitionRow
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public DateTime DateCreated { get; set; }
        public int RequestedById { get; set; }
        public string RequestedBy { get; set; }
        public string Branch { get; set; }
        public string Status { get; set; }
        public int Lines { get; set; }
        public int TotalQty { get; set; }
        public string ReviewedBy { get; set; }
        public string Remarks { get; set; }
        public string RejectReason { get; set; }
        public string StockCheck { get; set; }
    }

    /// <summary>One item line shown under the selected requisition.</summary>
    public class RequisitionLineRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Quantity { get; set; }
        public int Available { get; set; }

        // Calculated from Available / Quantity (empty setter - see note in CatalogRow.StockStatus).
        public string Check
        {
            get { return Available >= Quantity ? "Sufficient" : "Short by " + (Quantity - Available); }
            set { }
        }
    }

    /// <summary>Data needed to re-open a requisition for revision.</summary>
    public class RevisionData
    {
        public string RefNo { get; set; }
        public string Remarks { get; set; }
        public string Status { get; set; }
        public List<CartLine> Lines { get; set; }
    }

    /// <summary>Result of approving a requisition (includes the automatic stock check).</summary>
    public class ApprovalResult
    {
        public string RefNo { get; set; }
        public bool AllAvailable { get; set; }
        public string Summary { get; set; }
        public List<string> Shortages { get; set; }
    }
}
