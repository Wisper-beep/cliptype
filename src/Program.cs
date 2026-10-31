using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipType
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "Local\\ClipType.Desktop.1", out first))
            {
                if (!first)
                {
                    MessageBox.Show("ClipType 已在运行。请双击系统托盘里的蓝色剪贴板图标打开设置。", "ClipType");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                mutex.ReleaseMutex();
            }
        }
    }
}
