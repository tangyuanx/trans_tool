using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TransTool
{
    internal static class Program
    {
        internal static readonly Version AppVersion = Assembly.GetExecutingAssembly().GetName().Version;
        internal static readonly string Executable = Assembly.GetExecutingAssembly().Location;
        internal const string AppMutex = @"Local\TransTool-6B8D02BA-67EF-4D4C-8AD1-1CAD22649362";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B8D02BA-67EF-4D4C-8AD1-1CAD22649362}_is1";

        [STAThread]
        private static int Main(string[] arguments)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (arguments.Length == 4 && arguments[0] == "--apply")
            {
                int pid;
                if (!int.TryParse(arguments[1], out pid) || pid <= 0) return 2;
                try
                {
                    UpdateService.Apply(pid, arguments[2], arguments[3], Executable);
                    return 0;
                }
                catch (Exception error)
                {
                    if (!Environment.UserInteractive) return 1;
                    MessageBox.Show(error.Message, "更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }

            bool firstInstance;
            using (var mutex = new Mutex(true, AppMutex, out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Trans Tool 已在运行。", "Trans Tool");
                    return 0;
                }
                UpdateService.CleanOldCache();
                Application.Run(new MainWindow());
            }
            return 0;
        }

        internal static bool IsInstalled()
        {
            foreach (RegistryView view in new[] { RegistryView.Default, RegistryView.Registry32 })
            {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                using (var key = hive.OpenSubKey(UninstallKey))
                {
                    if (key == null) continue;
                    var path = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(path) &&
                        string.Equals(Path.Combine(path, "TransTool.exe"), Executable, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        internal static void OpenReleasePage()
        {
            Process.Start(new ProcessStartInfo(UpdateService.ReleasePage) { UseShellExecute = true });
        }
    }
}
