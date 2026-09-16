using System;
using System.IO;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// The system file and folder pickers, IFileOpenDialog called through its vtable. No COM interop
    /// layer is involved, so there is nothing for NativeAOT to generate or trim.
    /// </summary>
    internal static unsafe class FileDialogs
    {
        // IFileOpenDialog vtable slots: IUnknown 0-2, IModalWindow::Show 3, then IFileDialog in order.
        private const int Show = 3, SetFileTypes = 4, SetOptions = 9, GetOptions = 10, SetFolder = 12,
                          SetTitle = 17, GetResult = 20;
        private const int ShellItemGetDisplayName = 5;
        private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        private static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
        private static readonly Guid IidFileOpenDialog = new Guid("d57c7288-d4ad-4768-be02-9d969532d960");
        private static readonly Guid IidShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        internal static string PickFile(nint owner, string title, string filterName, string[] extensions)
        {
            string spec = extensions == null || extensions.Length == 0
                ? "*.*"
                : string.Join(";", Array.ConvertAll(extensions, e => e.StartsWith("*", StringComparison.Ordinal) ? e : "*." + e.TrimStart('.')));
            return Pick(owner, title ?? "Select file", false, null, filterName ?? "Files", spec);
        }

        internal static string PickFolder(nint owner, string title, string current) =>
            Pick(owner, title ?? "Select folder", true, Directory.Exists(current) ? current : null, null, null);

        private static string Pick(nint owner, string title, bool folders, string startIn, string filterName, string filterSpec)
        {
            Guid clsid = ClsidFileOpenDialog, iid = IidFileOpenDialog;
            nint dialog;
            if (CoCreateInstance(&clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, &iid, &dialog) < 0)
                return null;

            nint* vt = *(nint**)dialog;
            try
            {
                uint options;
                ((delegate* unmanaged<nint, uint*, int>)vt[GetOptions])(dialog, &options);
                options |= FOS_FORCEFILESYSTEM | (folders ? FOS_PICKFOLDERS : 0);
                ((delegate* unmanaged<nint, uint, int>)vt[SetOptions])(dialog, options);

                fixed (char* t = title)
                    ((delegate* unmanaged<nint, char*, int>)vt[SetTitle])(dialog, t);

                if (startIn != null)
                {
                    Guid itemIid = IidShellItem;
                    nint folder;
                    fixed (char* p = startIn)
                    {
                        if (SHCreateItemFromParsingName(p, 0, &itemIid, &folder) >= 0)
                        {
                            ((delegate* unmanaged<nint, nint, int>)vt[SetFolder])(dialog, folder);
                            Release(folder);
                        }
                    }
                }

                int shown;
                fixed (char* name = filterName ?? "")
                fixed (char* spec = filterSpec ?? "")
                {
                    if (!folders)
                    {
                        var filter = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                        ((delegate* unmanaged<nint, uint, COMDLG_FILTERSPEC*, int>)vt[SetFileTypes])(dialog, 1, &filter);
                    }
                    shown = ((delegate* unmanaged<nint, nint, int>)vt[Show])(dialog, owner);
                }
                if (shown < 0)
                    return null;   // cancelled, or failed - either way nothing was picked

                nint item;
                if (((delegate* unmanaged<nint, nint*, int>)vt[GetResult])(dialog, &item) < 0)
                    return null;

                char* path;
                int hr = ((delegate* unmanaged<nint, uint, char**, int>)(*(nint**)item)[ShellItemGetDisplayName])(item, SIGDN_FILESYSPATH, &path);
                Release(item);
                if (hr < 0)
                    return null;

                string result = new string(path);
                CoTaskMemFree((nint)path);
                return result;
            }
            finally
            {
                Release(dialog);
            }
        }
    }
}
