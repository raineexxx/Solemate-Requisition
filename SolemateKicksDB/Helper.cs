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
        // Old format: SHA-256 hex (64 characters). New format: PBKDF2$iterations$salt$hash (random salt per user).
        private const string Prefix = "PBKDF2";
        private const int Iterations = 10000;

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

        /// <summary>Creates a salted PBKDF2 hash to store in the database.</summary>
        public static string HashSalted(string password)
        {
            byte[] salt = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash = Derive(password, salt, Iterations);
            return Prefix + "$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Checks a password against the stored value. Works for salted hashes and for the old SHA-256 hashes
        /// (needsUpgrade is then true so the caller can store a salted hash).
        /// </summary>
        public static bool Verify(string password, string stored, out bool needsUpgrade)
        {
            needsUpgrade = false;
            if (string.IsNullOrEmpty(stored)) return false;

            if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
            {
                string[] parts = stored.Split('$');
                int iterations;
                if (parts.Length != 4 || !int.TryParse(parts[1], out iterations)) return false;
                byte[] salt, expected;
                try { salt = Convert.FromBase64String(parts[2]); expected = Convert.FromBase64String(parts[3]); }
                catch (FormatException) { return false; }
                return SlowEquals(Derive(password, salt, iterations), expected);
            }

            // legacy SHA-256 hex
            bool ok = string.Equals(Hash(password), stored, StringComparison.OrdinalIgnoreCase);
            needsUpgrade = ok;
            return ok;
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(password ?? string.Empty, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        private static bool SlowEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
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
