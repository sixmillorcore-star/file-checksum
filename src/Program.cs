using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("File Checksum")]
[assembly: AssemblyDescription("Offline SHA-256 calculator and verifier for Windows")]
[assembly: AssemblyCompany("Sixmillor")]
[assembly: AssemblyProduct("File Checksum")]
[assembly: AssemblyCopyright("Copyright 2026 Sixmillor")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace FileChecksum
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                if (args.Length == 1)
                    form.Shown += async delegate { await form.LoadFileAsync(args[0]); };
                Application.Run(form);
            }
        }
    }
}
