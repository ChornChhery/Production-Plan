using System;
using System.IO;
using System.Windows.Forms;

namespace Teeth
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Must run before any database / Excel code is touched: it tells
            // the app where the .dll files live (App Files\lib).
            AppBootstrap.Init();
            RunApp();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RunApp()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                // --- Phase 1: ensure the folder structure exists on first run ---
                string logDataDir = Path.Combine(baseDir, "Log Data");
                if (!Directory.Exists(logDataDir))
                {
                    Directory.CreateDirectory(logDataDir);
                }

                // --- Phase 2: ensure Teeth.sqlite exists and has its tables ---
                var db = new DatabaseManager();
                db.InitializeDatabase();

                // --- Phase 3: launch the real dashboard ---
                Application.Run(new MainDashboard(db));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Startup failed:" + Environment.NewLine + Environment.NewLine + ex,
                    "Teeth - Fatal Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
