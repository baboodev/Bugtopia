using System;
using System.Runtime.InteropServices;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// The Windows API the native window uses, and nothing else. Every signature is blittable -
    /// pointers, handles and plain structs - so NativeAOT generates no marshalling for any of it.
    /// </summary>
    internal static unsafe class Native
    {
        // ---- messages --------------------------------------------------------

        internal const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_SETFOCUS = 0x0007,
                            WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014, WM_SETCURSOR = 0x0020,
                            WM_GETMINMAXINFO = 0x0024, WM_DRAWITEM = 0x002B, WM_SETFONT = 0x0030,
                            WM_NCCREATE = 0x0081, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104,
                            WM_COMMAND = 0x0111, WM_TIMER = 0x0113, WM_VSCROLL = 0x0115,
                            WM_CTLCOLOREDIT = 0x0133, WM_CTLCOLORSTATIC = 0x0138,
                            WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
                            WM_MOUSEWHEEL = 0x020A, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0,
                            WM_APP = 0x8000;

        internal const uint BN_CLICKED = 0;
        internal const int IDOK = 1, IDCANCEL = 2;

        // ---- styles ----------------------------------------------------------

        internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_POPUP = 0x80000000, WS_CHILD = 0x40000000,
                            WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_VSCROLL = 0x00200000,
                            WS_TABSTOP = 0x00010000, WS_BORDER = 0x00800000, WS_CAPTION = 0x00C00000,
                            WS_SYSMENU = 0x00080000;
        internal const uint WS_EX_DLGMODALFRAME = 0x0001, WS_EX_CONTROLPARENT = 0x00010000;
        internal const uint BS_OWNERDRAW = 0x000B;
        internal const uint ES_MULTILINE = 0x0004, ES_AUTOVSCROLL = 0x0040, ES_AUTOHSCROLL = 0x0080, ES_READONLY = 0x0800;
        internal const uint CS_VREDRAW = 0x0001, CS_HREDRAW = 0x0002, CS_DROPSHADOW = 0x00020000;
        internal const uint EM_SETSEL = 0x00B1, EM_SETMARGINS = 0x00D3, EM_SETCUEBANNER = 0x1501;

        internal const uint ODS_SELECTED = 0x0001, ODS_DISABLED = 0x0004, ODS_FOCUS = 0x0010, ODS_NOFOCUSRECT = 0x0200;

        internal const int SW_HIDE = 0, SW_SHOW = 5;
        internal const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
                            SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080;

        internal const int SB_VERT = 1;
        internal const uint SIF_RANGE = 0x1, SIF_PAGE = 0x2, SIF_POS = 0x4, SIF_TRACKPOS = 0x10, SIF_ALL = 0x17;
        internal const int SB_LINEUP = 0, SB_LINEDOWN = 1, SB_PAGEUP = 2, SB_PAGEDOWN = 3, SB_THUMBPOSITION = 4,
                           SB_THUMBTRACK = 5, SB_TOP = 6, SB_BOTTOM = 7;

        internal const uint TME_LEAVE = 0x0002;
        internal const nint IDC_ARROW = 32512, IDC_HAND = 32649;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;
        internal const uint RDW_INVALIDATE = 0x0001, RDW_ALLCHILDREN = 0x0080;
        internal const int SM_CXICON = 11, SM_CYICON = 12, SM_CXSMICON = 49, SM_CYSMICON = 50;
        internal const uint WM_SETICON = 0x0080;

        // ---- structs ---------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        internal struct WNDCLASSEXW
        {
            public uint cbSize, style;
            public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public nint hInstance, hIcon, hCursor, hbrBackground;
            public char* lpszMenuName, lpszClassName;
            public nint hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int left, top, right, bottom;
            public int Width => right - left;
            public int Height => bottom - top;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CREATESTRUCTW
        {
            public nint lpCreateParams, hInstance, hMenu, hwndParent;
            public int cy, cx, y, x, style;
            public char* lpszName, lpszClass;
            public uint dwExStyle;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PAINTSTRUCT
        {
            public nint hdc;
            public int fErase;
            public RECT rcPaint;
            public int fRestore, fIncUpdate;
            public fixed byte rgbReserved[32];
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DRAWITEMSTRUCT
        {
            public uint CtlType, CtlID, itemID, itemAction, itemState;
            public nint hwndItem, hDC;
            public RECT rcItem;
            public nuint itemData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TEXTMETRICW
        {
            public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth,
                       tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
            public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
            public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct COMDLG_FILTERSPEC { public char* pszName, pszSpec; }

        // ---- user32 ----------------------------------------------------------

        [DllImport("user32.dll", ExactSpelling = true)] internal static extern ushort RegisterClassExW(WNDCLASSEXW* wc);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint CreateWindowExW(uint exStyle, char* cls, char* name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int DestroyWindow(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int ShowWindow(nint hwnd, int cmd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int TranslateMessage(MSG* msg);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint DispatchMessageW(MSG* msg);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int IsDialogMessageW(nint hwnd, MSG* msg);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int PostMessageW(nint hwnd, uint msg, nint w, nint l);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint SendMessageW(nint hwnd, uint msg, nint w, nint l);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint LoadCursorW(nint instance, nint id);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint SetCursor(nint cursor);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetClientRect(nint hwnd, RECT* rect);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetWindowRect(nint hwnd, RECT* rect);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int InvalidateRect(nint hwnd, RECT* rect, int erase);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int RedrawWindow(nint hwnd, RECT* rect, nint region, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int EndPaint(nint hwnd, PAINTSTRUCT* ps);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint GetDC(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int ReleaseDC(nint hwnd, nint dc);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int KillTimer(nint hwnd, nuint id);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetMonitorInfoW(nint monitor, MONITORINFO* info);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int EnableWindow(nint hwnd, int enable);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int IsWindowEnabled(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int IsWindowVisible(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint SetFocus(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern nint GetFocus();
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int SetForegroundWindow(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int TrackMouseEvent(TRACKMOUSEEVENT* tme);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int SetScrollInfo(nint hwnd, int bar, SCROLLINFO* info, int redraw);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetScrollInfo(nint hwnd, int bar, SCROLLINFO* info);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int SetWindowTextW(nint hwnd, char* text);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetWindowTextW(nint hwnd, char* text, int max);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetWindowTextLengthW(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetCursorPos(POINT* pt);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int ScreenToClient(nint hwnd, POINT* pt);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int SystemParametersInfoW(uint action, uint param, void* value, uint winIni);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int UpdateWindow(nint hwnd);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int ShowScrollBar(nint hwnd, int bar, int show);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int IsChild(nint parent, nint hwnd);

        internal const uint EM_GETLINECOUNT = 0x00BA;
        internal const uint SPI_GETWHEELSCROLLLINES = 0x0068;
        internal const uint ICON_SMALL = 0, ICON_BIG = 1;
        internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

        // ---- gdi32 -----------------------------------------------------------

        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern nint SelectObject(nint dc, nint obj);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int DeleteObject(nint obj);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int DeleteDC(nint dc);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, char* face);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern uint SetTextColor(nint dc, uint color);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern uint SetBkColor(nint dc, uint color);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int SetBkMode(nint dc, int mode);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int ExtTextOutW(nint dc, int x, int y, uint options, RECT* rect, char* text, uint count, int* dx);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int GetTextExtentPoint32W(nint dc, char* text, int count, SIZE* size);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int GetTextExtentExPointW(nint dc, char* text, int count, int maxExtent, int* fit, int* dx, SIZE* size);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int GetTextMetricsW(nint dc, TEXTMETRICW* tm);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern int GetTextFaceW(nint dc, int count, char* face);
        [DllImport("gdi32.dll", ExactSpelling = true)] internal static extern nint CreateSolidBrush(uint color);

        internal const uint SRCCOPY = 0x00CC0020;
        internal const int TRANSPARENT = 1;

        // ---- kernel32, dwmapi, uxtheme, comctl32, ole32, shell32, shlwapi ----

        [DllImport("kernel32.dll", ExactSpelling = true)] internal static extern nint GetModuleHandleW(char* name);
        [DllImport("kernel32.dll", ExactSpelling = true)] internal static extern nint LoadLibraryW(char* name);
        [DllImport("kernel32.dll", ExactSpelling = true)] internal static extern nint GetProcAddress(nint module, nint nameOrOrdinal);

        [DllImport("dwmapi.dll", ExactSpelling = true)] internal static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);
        [DllImport("uxtheme.dll", ExactSpelling = true)] internal static extern int SetWindowTheme(nint hwnd, char* app, char* idList);

        [DllImport("comctl32.dll", ExactSpelling = true)] internal static extern int SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
        [DllImport("comctl32.dll", ExactSpelling = true)] internal static extern nint DefSubclassProc(nint hwnd, uint msg, nint w, nint l);

        [DllImport("ole32.dll", ExactSpelling = true)] internal static extern int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);
        [DllImport("ole32.dll", ExactSpelling = true)] internal static extern void CoTaskMemFree(nint p);
        [DllImport("shell32.dll", ExactSpelling = true)] internal static extern int SHCreateItemFromParsingName(char* path, nint bindContext, Guid* iid, nint* item);
        [DllImport("shlwapi.dll", ExactSpelling = true)] internal static extern nint SHCreateMemStream(byte* data, uint size);

        // ---- small helpers ---------------------------------------------------

        internal static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

        internal static int LoWord(nint value) => (short)((long)value & 0xFFFF);
        internal static int HiWord(nint value) => (short)(((long)value >> 16) & 0xFFFF);

        /// <summary>Releases a COM interface through its vtable.</summary>
        internal static void Release(nint unknown)
        {
            if (unknown != 0)
                ((delegate* unmanaged<nint, uint>)(*(nint**)unknown)[2])(unknown);
        }

        internal static string WindowText(nint hwnd)
        {
            int length = GetWindowTextLengthW(hwnd);
            if (length <= 0)
                return "";
            char* buffer = stackalloc char[length + 1];
            int read = GetWindowTextW(hwnd, buffer, length + 1);
            return new string(buffer, 0, read);
        }

        internal static void SetText(nint hwnd, string text)
        {
            fixed (char* p = text ?? "")
                SetWindowTextW(hwnd, p);
        }

        /// <summary>
        /// Asks for the dark variants of the system-drawn parts - scrollbars, the read-only text box.
        /// Undocumented uxtheme ordinals, which is what Explorer itself uses, looked up by ordinal so a
        /// Windows without them simply keeps the light ones instead of failing to start.
        /// </summary>
        internal static void PreferDarkMode()
        {
            fixed (char* name = "uxtheme.dll")
            {
                nint uxtheme = LoadLibraryW(name);
                if (uxtheme == 0)
                    return;
                nint setPreferredAppMode = GetProcAddress(uxtheme, 135);
                if (setPreferredAppMode != 0)
                    ((delegate* unmanaged<int, int>)setPreferredAppMode)(2);   // ForceDark
            }
        }

        internal static void DarkControl(nint hwnd)
        {
            fixed (char* name = "uxtheme.dll")
            {
                nint uxtheme = GetModuleHandleW(name);
                nint allowDark = uxtheme == 0 ? 0 : GetProcAddress(uxtheme, 133);
                if (allowDark != 0)
                    ((delegate* unmanaged<nint, int, int>)allowDark)(hwnd, 1);
            }
            fixed (char* theme = "DarkMode_Explorer")
                SetWindowTheme(hwnd, theme, null);
        }

        /// <summary>A dark title bar: attribute 20 since Windows 10 20H1, 19 on the builds before it.</summary>
        internal static void DarkTitleBar(nint hwnd)
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, &on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, &on, sizeof(int));
        }
    }
}
