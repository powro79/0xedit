using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal static class BinaryClipboard
    {
        internal const int Limit = 16 * 1024 * 1024;
        private const string Format = "0xEdit.Bytes.v1";
        private static readonly uint formatId = RegisterClipboardFormat(Format);

        internal static void Write(byte[] bytes, bool ascii)
        {
            Clipboard.SetDataObject(CreateData(bytes, ascii), true);
        }

        internal static DataObject CreateData(byte[] bytes, bool ascii)
        {
            var stream = new MemoryStream(bytes.Length + 4);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
            stream.Position = 0;
            var data = new DataObject();
            data.SetData(Format, false, stream);
            data.SetText(ascii ? Encoding.ASCII.GetString(bytes).Replace('\0', '·') : ByteTools.Hex(bytes),
                         TextDataFormat.UnicodeText);
            return data;
        }

        // The native allocation may contain alignment padding. Trust only the
        // validated length prefix and read bytes directly from the global handle.
        internal static byte[] ReadPayload(IntPtr memory, ulong allocated)
        {
            if (allocated < 4)
                throw new FormatException("Incomplete binary clipboard data.");
            int length = Marshal.ReadInt32(memory);
            if (length < 0 || length > Limit || (ulong)length > allocated - 4)
                throw new FormatException("Invalid binary clipboard length.");
            var bytes = new byte[length];
            Marshal.Copy(IntPtr.Add(memory, 4), bytes, 0, length);
            return bytes;
        }

        internal static byte[] Read(bool ascii)
        {
            if (IsClipboardFormatAvailable(formatId))
            {
                if (!OpenClipboard(IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var handle = GetClipboardData(formatId);
                    if (handle == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    return ReadHandle(handle);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            string text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (!ascii)
                return ByteTools.ParseHex(text);
            foreach (char ch in text)
                if (ch > 127)
                    throw new FormatException("The ASCII pane accepts characters from 0 to 127.");
            return Encoding.ASCII.GetBytes(text);
        }
        internal static byte[] ReadHandle(IntPtr handle)
        {
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                return ReadPayload(ptr, GlobalSize(handle).ToUInt64());
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint format);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr handle);
    }
}
