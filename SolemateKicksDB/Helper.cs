using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SolemateKicks
{
    // ------------------------------------------------------------------
    //  Constants (these used to live in Models.cs)
    // ------------------------------------------------------------------
    public static class Roles
    {
        public const string Cashier = "Cashier";
        public const string Head = "Head";
        public const string Manager = "Manager";
    }

    public static class Statuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Closed = "Closed";

        // used by the status filter on the main screen
        public static readonly string[] Filter = { "All", Pending, Approved, Rejected, Cancelled, Closed };
    }

    public static class PasswordHelper
    {
        public static string Hash(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Startup check. The database and procedures are created by Database.sql and StoredProcedures.sql,
    /// so here we only make sure they are really there.
    /// </summary>
    public static class DbInitializer
    {
        public static void EnsureCreated()
        {
            using (SolemateDataContext db = new SolemateDataContext())
            {
                if (!db.DatabaseExists())
                    throw new InvalidOperationException(
                        "The database SolemateKicksDB was not found.\r\n" +
                        "Run Database.sql and then StoredProcedures.sql in SQL Server Object Explorer.");

                int procCount = db.ExecuteQuery<int>(
                    "SELECT COUNT(*) FROM sys.procedures WHERE name LIKE 'usp[_]%'").First();

                if (procCount < 14)
                    throw new InvalidOperationException(
                        "The stored procedures are missing (found " + procCount + " of 14).\r\n" +
                        "Run StoredProcedures.sql in SQL Server Object Explorer, then start the app again.");
            }
        }
    }
}
