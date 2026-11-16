using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// These aliases prevent "ambiguous reference" errors with System.Windows.Forms.VisualStyles
using Button = System.Windows.Forms.Button;

namespace SolemateKicks
{
    public class NotificationRow
    {
        public int NotificationId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedOn { get; set; }
        public bool IsRead { get; set; }
    }

    /// <summary>In-app notifications (requisition decisions, PO approvals, deliveries, low stock).</summary>
    public static class NotificationService
    {
        public static List<NotificationRow> GetMine(AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<NotificationRow>("EXEC dbo.usp_NotGetMine {0}", user.UserId).ToList();
        }

        public static int CountUnread(AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                return db.ExecuteQuery<int>("EXEC dbo.usp_NotCountUnread {0}", user.UserId).First();
        }

        public static void MarkAllRead(AppUser user)
        {
            using (SolemateDataContext db = new SolemateDataContext())
                db.ExecuteCommand("EXEC dbo.usp_NotMarkAllRead {0}", user.UserId);
        }
    }

    public class NotificationsForm : Form
    {
        private readonly AppUser _user = Session.CurrentUser;
        private DataGridView grid;

        public NotificationsForm()
        {
            Text = "Solemate - Notifications";
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Canvas;
            Font = Theme.FontBody;

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

            Label title = Theme.MakeSectionTitle("Notifications for " + _user.FullName + " (" + _user.Role + ")");
            title.Dock = DockStyle.Fill;
            t.Controls.Add(title, 0, 0);

            grid = new DataGridView();
            Theme.StyleGrid(grid);
            grid.ReadOnly = true;
            Theme.AddColumn(grid, "nNew", "", "IsRead", 7f, null, true, DataGridViewContentAlignment.MiddleCenter);
            Theme.AddColumn(grid, "nDate", "DATE", "CreatedOn", 20f, "MMM dd, h:mm tt");
            Theme.AddColumn(grid, "nMsg", "MESSAGE", "Message", 73f);
            grid.Columns["nMsg"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                NotificationRow r = grid.Rows[e.RowIndex].DataBoundItem as NotificationRow;
                if (r == null) return;
                if (grid.Columns[e.ColumnIndex].Name == "nNew")
                {
                    e.Value = r.IsRead ? "" : "NEW";
                    e.CellStyle.ForeColor = Theme.Red;
                    e.CellStyle.Font = Theme.FontBold;
                    e.FormattingApplied = true;
                }
                else if (!r.IsRead)
                {
                    e.CellStyle.Font = Theme.FontBold;
                }
            };
            t.Controls.Add(grid, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button close = Theme.MakeButton("CLOSE", 100, true, (s, e) => Close());
            buttons.Controls.Add(close);
            t.Controls.Add(buttons, 0, 2);

            Controls.Add(t);
            Shown += (s, e) => LoadData();
        }

        private void LoadData()
        {
            try
            {
                grid.DataSource = NotificationService.GetMine(_user);
                grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
                NotificationService.MarkAllRead(_user);   // the NEW tags stay visible until this window is closed
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Solemate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
