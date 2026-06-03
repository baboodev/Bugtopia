using System;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    /// <summary>
    /// Minimal x64 detour for Mono unmanaged method thunks (CreateActivityBubble, etc.).
    /// </summary>
    internal static class BubbleMonoNativeHook
    {
        private const int JumpSize = 14;
        private const uint PageExecuteReadwrite = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        public static bool TryInstall(IntPtr target, IntPtr hook, out IntPtr trampoline)
        {
            trampoline = IntPtr.Zero;
            if (target == IntPtr.Zero || hook == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                byte[] stolen = new byte[JumpSize];
                Marshal.Copy(target, stolen, 0, JumpSize);

                trampoline = Marshal.AllocHGlobal(JumpSize + JumpSize);
                if (trampoline == IntPtr.Zero)
                {
                    return false;
                }

                if (!VirtualProtect(trampoline, (UIntPtr)(JumpSize + JumpSize), PageExecuteReadwrite, out _))
                {
                    Marshal.FreeHGlobal(trampoline);
                    trampoline = IntPtr.Zero;
                    return false;
                }

                Marshal.Copy(stolen, 0, trampoline, JumpSize);
                WriteAbsoluteJump(trampoline + JumpSize, target + JumpSize);

                if (!VirtualProtect(target, (UIntPtr)JumpSize, PageExecuteReadwrite, out _))
                {
                    Marshal.FreeHGlobal(trampoline);
                    trampoline = IntPtr.Zero;
                    return false;
                }

                WriteAbsoluteJump(target, hook);
                return true;
            }
            catch
            {
                if (trampoline != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(trampoline);
                    trampoline = IntPtr.Zero;
                }

                return false;
            }
        }

        private static void WriteAbsoluteJump(IntPtr at, IntPtr destination)
        {
            byte[] patch = new byte[JumpSize];
            patch[0] = 0xFF;
            patch[1] = 0x25;
            patch[2] = 0x00;
            patch[3] = 0x00;
            patch[4] = 0x00;
            patch[5] = 0x00;
            byte[] address = BitConverter.GetBytes(destination.ToInt64());
            Buffer.BlockCopy(address, 0, patch, 6, 8);
            Marshal.Copy(patch, 0, at, JumpSize);
        }
    }
}
