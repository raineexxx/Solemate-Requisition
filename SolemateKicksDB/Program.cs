using System;
using System.Windows.Forms;

namespace SolemateKicks
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Creates the SolemateKicksDB database + sample data the first time the app runs.
            try
            {
                DbInitializer.EnsureCreated();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not connect to or create the database.\r\n\r\n" + ex.Message +
                    "\r\n\r\nCheck the connection string in SolemateDataContext.cs " +
                    "(LocalDB: (localdb)\\MSSQLLocalDB, SQL Express: .\\SQLEXPRESS).",
                    "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Login -> main screen. "Log out" brings the login back.
            while (true)
            {
                using (LoginForm login = new LoginForm())
                {
                    if (login.ShowDialog() != DialogResult.OK) return;
                }

                MainForm main = new MainForm();
                Application.Run(main);

                if (!main.LogoutRequested) return;
            }
        }
    }
}
