using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JLS.Services
{
    public static class ExplorerHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void ILFree(IntPtr pidlList);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        public static void OpenFolderAndSelectFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            string cleanPath = PathHelper.GetExplorerSafePath(filePath);
            string? folderPath = Path.GetDirectoryName(cleanPath);

            if (string.IsNullOrEmpty(folderPath)) return;

            bool shellSuccess = false;

            IntPtr dirPidl = IntPtr.Zero;
            IntPtr filePidl = IntPtr.Zero;

            try
            {
                uint sfgao;
                if (SHParseDisplayName(folderPath, IntPtr.Zero, out dirPidl, 0, out sfgao) == 0 &&
                    SHParseDisplayName(cleanPath, IntPtr.Zero, out filePidl, 0, out sfgao) == 0)
                {
                    IntPtr relativeFilePidl = ILFindLastID(filePidl);
                    if (SHOpenFolderAndSelectItems(dirPidl, 1, new IntPtr[] { relativeFilePidl }, 0) == 0)
                    {
                        shellSuccess = true;       
                    }
                }
            }
            catch { }
            finally
            {
                if (dirPidl != IntPtr.Zero) ILFree(dirPidl);
                if (filePidl != IntPtr.Zero) ILFree(filePidl);
            }

            if (shellSuccess) return;    


            string safePrefixPath = filePath.StartsWith(@"\\?\") ? filePath : @"\\?\" + filePath;
            var sb = new StringBuilder(1024);
            uint result = GetShortPathName(safePrefixPath, sb, (uint)sb.Capacity);

            if (result > 0 && result < sb.Capacity)
            {
                string shortPath = sb.ToString();
                if (shortPath.StartsWith(@"\\?\UNC\")) shortPath = @"\\" + shortPath.Substring(8);
                else if (shortPath.StartsWith(@"\\?\")) shortPath = shortPath.Substring(4);

                if (shortPath.Length < 260)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{shortPath}\"",
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch { }
                }
            }


            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}