using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bugtopia.Launcher
{
    /// <summary>
    /// Puts text on the Windows clipboard, for both windows. Neither Photino nor the Win32 host has a
    /// clipboard of its own, and the page cannot be trusted to have one: a raw-string page in WebView2
    /// is not a secure context.
    /// </summary>
    internal static unsafe class Clipboard
    {
        private const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", ExactSpelling = true)] private static extern int OpenClipboard(nint owner);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern int EmptyClipboard();
        [DllImport("user32.dll", ExactSpelling = true)] private static extern nint SetClipboardData(uint format, nint data);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern int CloseClipboard();
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void* GlobalLock(nint memory);
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern int GlobalUnlock(nint memory);
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint GlobalFree(nint memory);

        /// <summary>
        /// Replaces the clipboard's contents with <paramref name="text"/>. The owner has to be a real
        /// window: opened with none, EmptyClipboard leaves no owner and SetClipboardData then refuses.
        /// </summary>
        internal static bool SetText(nint owner, string text)
        {
            if (owner == 0 || text == null)
                return false;

            // Another program can hold the clipboard for a moment; a few short tries ride that out.
            bool opened = false;
            for (int attempt = 0; attempt < 10 && !opened; attempt++)
            {
                opened = OpenClipboard(owner) != 0;
                if (!opened)
                    Thread.Sleep(20);
            }
            if (!opened)
                return false;

            try
            {
                EmptyClipboard();
                nint memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
                if (memory == 0)
                    return false;

                char* target = (char*)GlobalLock(memory);
                if (target == null)
                {
                    GlobalFree(memory);
                    return false;
                }
                fixed (char* source = text)
                    Buffer.MemoryCopy(source, target, (text.Length + 1) * sizeof(char), text.Length * sizeof(char));
                target[text.Length] = '\0';
                GlobalUnlock(memory);

                // Once SetClipboardData accepts it, the memory is the clipboard's; only a refusal leaves it ours.
                if (SetClipboardData(CF_UNICODETEXT, memory) == 0)
                {
                    GlobalFree(memory);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
