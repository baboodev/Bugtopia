using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bugtopia.Launch;
using Photino.NET;

namespace Bugtopia.Launcher
{
    /// <summary>
    /// The window. Photino owns the native shell, the message loop and the page bridge, so what is
    /// left here is unpacking that shell, wiring the bridge to <see cref="Api"/>, and handing back
    /// file pickers.
    ///
    /// Photino rather than the WebView2 .NET SDK for one measured reason: the SDK marshals its
    /// completion handler through COM from managed code, which NativeAOT cannot do, while Photino
    /// does the webview work in C++ behind plain P/Invoke and compiles AOT cleanly. That is 12.0 MB
    /// down to roughly 6, and it also drops the built-in COM interop that trimming kept switching
    /// off underneath us.
    /// </summary>
    internal sealed class PhotinoHost : IDialogs
    {
        private PhotinoWindow window;
        private Api api;

        /// <summary>
        /// Where the window waits until the page has something to show.
        ///
        /// Photino shows the window from inside its own constructor and only creates WebView2
        /// afterwards - measured at 80 ms for the first and 360 ms for the page reporting in, and
        /// far longer on a cold start - so for that gap there is an empty window on screen painted
        /// with the class brush, which is black. No hook runs early enough to stop it, so it is
        /// created off any monitor instead and brought back by <see cref="Reveal"/>.
        ///
        /// Off-screen rather than hidden on purpose: a window this far out is not drawn, but it
        /// still has a taskbar button, and that button is the only sign the launcher is starting.
        /// -32000 is the corner Windows itself parks minimised windows in, so no arrangement of
        /// monitors reaches it.
        /// </summary>
        private const int ParkedPosition = -32000;

        /// <summary>
        /// How long the window waits for the page before showing itself regardless. A safety net,
        /// not a schedule: a page that never reports in must not leave a launcher with no window.
        /// Reaching it shows the empty window this exists to avoid, which is only where it started.
        /// </summary>
        private static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(5);

        private int revealed;

        internal static int Run()
        {
            NativeShell.Install();
            return new PhotinoHost().Start();
        }

        private int Start()
        {
            api = new Api(Send, this);

            PhotinoWindow shell = new PhotinoWindow()
                .SetTitle("Bugtopia")
                .SetUseOsDefaultSize(false)
                .SetSize(Api.WindowWidth, Api.WindowHeight(api.Expert))
                .SetMinSize(560, 480)
                .SetUseOsDefaultLocation(false)
                .SetLeft(ParkedPosition)
                .SetTop(ParkedPosition)
                .SetResizable(true)
                .SetContextMenuEnabled(false)
                .SetDevToolsEnabled(false);

            // Photino takes the window icon as a path, not a handle, so the .ico has to be on disk
            // even though it ships inside the exe. It is written beside the native shell, in the
            // folder the launcher already owns and can delete wholesale.
            //
            // The exe's own icon comes from ApplicationIcon in the csproj instead — that one is a
            // Win32 resource, which is what Explorer and a pinned shortcut read.
            string icon = NativeShell.WriteIcon();
            if (icon != null)
                shell.SetIconFile(icon);

            window = shell
                .RegisterWebMessageReceivedHandler((sender, message) => api.Dispatch(message))
                .LoadRawString(LoadUi());

            _ = Task.Delay(RevealTimeout).ContinueWith(_ => Reveal());

            window.WaitForClose();
            return 0;
        }

        /// <summary>Pushes a JSON message to the page from whatever thread produced it.</summary>
        private void Send(string json)
        {
            PhotinoWindow w = window;
            if (w == null)
                return;

            try
            {
                w.Invoke(() => w.SendWebMessage(json));
            }
            catch (Exception)
            {
                // The window is closing; a dropped status line is not worth taking the process down.
            }
        }

        public string PickFolder(string title, string current)
        {
            string[] chosen = window.ShowOpenFolder(title ?? "Select folder",
                                                    Directory.Exists(current) ? current : null);
            return chosen != null && chosen.Length > 0 ? chosen[0] : null;
        }

        /// <summary>Resizes and re-centres, for the switch between the two views.</summary>
        public void Resize(int width, int height)
        {
            PhotinoWindow w = window;
            if (w == null)
                return;

            try
            {
                w.Invoke(() => w.SetSize(width, height).Center());
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Brings the parked window onto the screen, once. The page asks for this after its first
        /// render, so what appears is the finished launcher rather than an empty frame of it.
        ///
        /// The page is told once the window is actually up. It has no other way to know - it was
        /// drawn while still off the screen - and the countdown to launching on its own must not
        /// start before anyone can see it.
        /// </summary>
        public void Reveal()
        {
            if (Interlocked.Exchange(ref revealed, 1) != 0)
                return;

            PhotinoWindow w = window;
            if (w == null)
                return;

            try
            {
                w.Invoke(() =>
                {
                    w.Center();
                    w.SendWebMessage("{\"event\":\"revealed\"}");
                });
            }
            catch (Exception)
            {
                // Closing already, or never opened. Either way there is nothing to show.
            }
        }

        /// <summary>Shuts the launcher down. Called once the game is running and injected.</summary>
        public void Close()
        {
            PhotinoWindow w = window;
            if (w == null)
                return;

            try
            {
                w.Invoke(() => w.Close());
            }
            catch (Exception)
            {
                // Already going away.
            }
        }

        public bool CopyText(string text)
        {
            PhotinoWindow w = window;
            return w != null && Clipboard.SetText(w.WindowHandle, text);
        }

        public string PickFile(string title, string filterName, string[] extensions)
        {
            string[] chosen = window.ShowOpenFile(
                title ?? "Select file",
                null,
                false,
                new[] { (filterName ?? "Files", extensions ?? new[] { "*" }) });
            return chosen != null && chosen.Length > 0 ? chosen[0] : null;
        }

        /// <summary>
        /// The page, exactly as it is embedded.
        ///
        /// Nothing is folded into it. The string handed to LoadRawString is what Photino passes to
        /// the native side at window creation, and a page carrying the logo as a base64 data URI —
        /// about 58 KB against 26 KB without — access-violates inside Photino.Native.dll every
        /// time, measured 2 for 2 against a build identical but for that one string. The logo comes
        /// over the message bridge after load instead, where size is not a problem.
        /// </summary>
        private static string LoadUi()
        {
            using Stream stream = typeof(PhotinoHost).Assembly.GetManifestResourceStream("ui.html");
            if (stream == null)
                return "<html><body>ui.html is missing from this build.</body></html>";

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Unpacks the native webview shell and points Photino's P/Invokes at it.
    ///
    /// NativeAOT cannot bundle a native dependency and the package ships no static library, so the
    /// DLL rides along as a resource and is written out on first run. That is what keeps the shipped
    /// artefact one file.
    ///
    /// Two things this gets right the hard way. It must run <b>before the first Photino call</b>,
    /// because a missing P/Invoke target under NativeAOT does not throw <c>DllNotFoundException</c> —
    /// it fail-fasts the process with <c>0xC0000409</c> and says nothing. And the folder is keyed by
    /// the DLL's content hash, so a new build writes a new copy instead of fighting one an older
    /// instance still holds open.
    ///
    /// Both DLLs, not one. Photino.Native imports WebView2Loader, and carrying only the first
    /// produces a launcher that starts on a machine with a copy of the second somewhere on PATH —
    /// the Windows SDK ships one — and dies with 0xC0000409 before its own log exists on every
    /// machine that does not. Measured: with the SDK's folder taken off PATH, the build that
    /// carried one DLL failed exactly that way.
    /// </summary>
    internal static class NativeShell
    {
        private const string ResourceName = "Photino.Native.dll";
        private const string LoaderName = "WebView2Loader.dll";
        private const string IconName = "bugtopia.ico";
        private static IntPtr handle;

        internal static void Install()
        {
            NativeLibrary.SetDllImportResolver(typeof(PhotinoWindow).Assembly, Resolve);
        }

        private static byte[] ReadResource(string name)
        {
            using Stream source = typeof(NativeShell).Assembly.GetManifestResourceStream(name);
            if (source == null)
                return null;

            var bytes = new byte[source.Length];
            source.ReadExactly(bytes);
            return bytes;
        }

        /// <summary>Writes one file into the cache folder, and returns where it went.</summary>
        private static string Write(string folder, string name, byte[] bytes)
        {
            string file = Path.Combine(folder, name);
            if (!File.Exists(file) || new FileInfo(file).Length != bytes.Length)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(file, bytes);
            }
            return file;
        }

        /// <summary>
        /// Writes the window icon out beside the shell and returns its path, or null when this
        /// build does not carry one. Rewritten whenever the size differs, which is enough to catch
        /// a changed icon without hashing a file that is read once per run.
        /// </summary>
        internal static string WriteIcon()
        {
            try
            {
                using Stream source = typeof(NativeShell).Assembly.GetManifestResourceStream(IconName);
                if (source == null)
                    return null;

                var bytes = new byte[source.Length];
                source.ReadExactly(bytes);

                string file = Path.Combine(KnownPaths.NativeShellRoot, IconName);
                if (!File.Exists(file) || new FileInfo(file).Length != bytes.Length)
                {
                    Directory.CreateDirectory(KnownPaths.NativeShellRoot);
                    File.WriteAllBytes(file, bytes);
                }
                return file;
            }
            catch (Exception)
            {
                // A window without its icon is still a window.
                return null;
            }
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
        {
            if (!name.StartsWith("Photino.Native", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;
            if (handle != IntPtr.Zero)
                return handle;

            byte[] shell = ReadResource(ResourceName);
            if (shell == null)
                return IntPtr.Zero;

            string tag = Convert.ToHexString(SHA256.HashData(shell)).Substring(0, 16);
            string folder = Path.Combine(KnownPaths.NativeShellRoot, tag);

            // The dependency is loaded first, by absolute path. Unpacking it beside the shell is not
            // enough on its own: Windows resolves a DLL's imports through the standard search order,
            // which does not include the folder the DLL itself came from. Loading it first puts it
            // in the process by name, and the import then resolves to it wherever it came from.
            byte[] loader = ReadResource(LoaderName);
            if (loader != null)
                NativeLibrary.Load(Write(folder, LoaderName, loader));

            handle = NativeLibrary.Load(Write(folder, ResourceName, shell));
            return handle;
        }
    }
}
