using System;
using System.Reflection;
using System.Windows.Forms;

[assembly:AssemblyTitle("0xEdit")]
[assembly:AssemblyDescription("Open-source binary editor for Windows")]
[assembly:AssemblyProduct("0xEdit")]
[assembly:AssemblyCopyright("Copyright (c) 2026 0xEdit contributors. MIT License.")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace ZeroXEdit
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Initialize();
            using (var form = new MainForm(args)) Application.Run(form);
        }
    }
}
