// ADFS-Test-Tool - WinForms, .NET Framework 4.5+
// Kompilieren: Build-AdfsTester.bat

using System;
using System.Net;
using System.Windows.Forms;

namespace AdfsTester
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Moderne TLS-Versionen aktivieren (Standard auf .NET 4.5 ist veraltet).
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { /* aeltere Frameworks kennen Tls12 evtl. nicht */ }
            ServicePointManager.DefaultConnectionLimit = 20;
            ServicePointManager.Expect100Continue = false;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "Unerwarteter Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(e.ExceptionObject != null ? e.ExceptionObject.ToString() : "?",
                    "Unerwarteter Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.Run(new MainForm());
        }
    }
}
