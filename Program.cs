using System;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        using (var mutex = new Mutex(true, @"Local\TabletDesk", out firstInstance))
        {
            if (!firstInstance)
            {
                MessageBox.Show("Tablet Desk is already running. Check its window or system tray icon.",
                    "Tablet Desk", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Dashboard());
        }
    }
}
