using System;
using System.Collections.Generic;
using System.Linq;

namespace SolemateKicks
{
    public class IssueRequisitionRow
    {
        public int RequisitionId { get; set; }
        public string RefNo { get; set; }
        public string Branch { get; set; }
        public DateTime DateCreated { get; set; }
        public string RequestedBy { get; set; }
        public int Requested { get; set; }
        public int Reserved { get; set; }
        public int Issued { get; set; }
    }

    public class IssueItemRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Requested { get; set; }
        public int Issued { get; set; }
        public int Reserved { get; set; }
        public int Outstanding { get; set; }
        public int Available { get; set; }
    }

    public class ReserveResult
    {
        public int Reserved { get; set; }
        public int ShortLines { get; set; }
    }

    public class IssueResult
    {
        public string IssueNo { get; set; }
        public bool Closed { get; set; }
    }

    public class SlipRow
    {
        public int IssuanceId { get; set; }
        public string IssueNo { get; set; }
        public string RequisitionRef { get; set; }
        public string Branch { get; set; }
        public DateTime DateIssued { get; set; }
        public string IssuedBy { get; set; }
        public int TotalQty { get; set; }
        public string Status { get; set; }
        public string ReceivedBy { get; set; }
        public DateTime? DateAcknowledged { get; set; }
    }

    public class SlipItemRow
    {
        public string Sku { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public int Size { get; set; }
        public int Quantity { get; set; }
    }

    /// <summary>Database work for the Issuance module (Database\10_Issuance.sql).</summary>
    public static class IssuanceService
    {
        public static List<IssueRequisitionRow> GetRequisitions()
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<IssueRequisitionRow>("EXEC dbo.usp_IssGetRequisitions").ToList();
        }

        public static List<IssueItemRow> GetItems(int requisitionId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<IssueItemRow>("EXEC dbo.usp_IssGetItems {0}", requisitionId).ToList();
        }

        public static ReserveResult Reserve(int requisitionId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<ReserveResult>("EXEC dbo.usp_IssReserve {0}, {1}", requisitionId, user.UserId).First();
        }

        public static IssueResult Issue(int requisitionId, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<IssueResult>("EXEC dbo.usp_IssCreate {0}, {1}", requisitionId, user.UserId).First();
        }

        public static List<SlipRow> GetSlips(AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<SlipRow>("EXEC dbo.usp_IssGetSlips {0}", user.UserId).ToList();
        }

        public static List<SlipItemRow> GetSlipItems(int issuanceId)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<SlipItemRow>("EXEC dbo.usp_IssGetSlipItems {0}", issuanceId).ToList();
        }

        public static void Acknowledge(int issuanceId, string receivedBy, AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                db.ExecuteCommand("EXEC dbo.usp_IssAcknowledge {0}, {1}, {2}", issuanceId, user.UserId, receivedBy ?? string.Empty);
        }
    }
}
