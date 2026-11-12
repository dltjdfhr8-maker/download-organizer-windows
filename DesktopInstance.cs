using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DownloadOrganizer
{
    internal static class DesktopInstance
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index,
            StringBuilder buffer, int length, out int required);

        private static string ObjectName(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return "";
            StringBuilder buffer = new StringBuilder(256);
            int required;
            if (GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out required))
                return buffer.ToString();
            if (required > buffer.Capacity * 2 && required < 8192)
            {
                buffer = new StringBuilder(required / 2 + 1);
                if (GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out required))
                    return buffer.ToString();
            }
            return "";
        }

        internal static string DesktopName()
        {
            return ObjectName(GetProcessWindowStation()) + "\\" +
                ObjectName(GetThreadDesktop(GetCurrentThreadId()));
        }

        internal static string ScopeKey()
        {
            string name = DesktopName();
            if (name == "\\" || name.StartsWith("\\") || name.EndsWith("\\"))
                return "process" + Process.GetCurrentProcess().Id;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(name));
                return BitConverter.ToString(bytes, 0, 8).Replace("-", "");
            }
        }
    }
}
