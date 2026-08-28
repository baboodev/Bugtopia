using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Bugtopia.Launch
{
    /// <summary>Something went wrong before an HTTP status was reached.</summary>
    public sealed class WebException : Exception
    {
        public WebException(string message) : base(message) { }
    }

    /// <summary>
    /// HTTPS GET through the operating system's own client.
    ///
    /// This exists for one measured reason. The launcher makes a handful of GETs — the GitHub
    /// releases API and three file downloads — and doing them with <c>HttpClient</c> costs 3.5 MB
    /// of the published binary: HttpClient itself, TLS, sockets, QUIC, ASN.1, BigInteger and the
    /// async and generic machinery they instantiate. WinHTTP does the same work in the OS, with the
    /// system's own certificate handling and proxy settings, for the size of the declarations below.
    /// Vugtopia reached the same conclusion against the same endpoints; this is its http_get in C#.
    ///
    /// Blocking on purpose: every caller is already a background job with nothing else to do until
    /// the bytes arrive, and an async wrapper here would only add back some of what was removed.
    /// </summary>
    public static class WinHttp
    {
        private const string Agent = "Bugtopia-Launcher";

        // Only https is spoken here, and only to hosts that redirect within https, so the port is
        // fixed and WinHTTP's default redirect policy is left alone.
        private const ushort HttpsPort = 443;
        private const uint AccessTypeAutomaticProxy = 4;
        private const uint FlagSecure = 0x00800000;
        private const uint AddRequestHeaderAdd = 0x20000000;
        private const uint QueryContentLength = 5;
        private const uint QueryStatusCode = 19;
        private const uint QueryFlagNumber = 0x20000000;

        /// <summary>
        /// Fetches <paramref name="url"/> into <paramref name="destination"/> and returns the HTTP
        /// status. Redirects are followed and TLS is validated by the OS.
        /// </summary>
        /// <param name="progress">Called with (received, total); total is 0 when unannounced.</param>
        public static int Get(string url, IEnumerable<KeyValuePair<string, string>> headers,
                              Stream destination, Action<long, long> progress = null)
        {
            SplitUrl(url, out string host, out string path);

            IntPtr session = IntPtr.Zero, connection = IntPtr.Zero, request = IntPtr.Zero;
            try
            {
                session = WinHttpOpen(Agent, AccessTypeAutomaticProxy, null, null, 0);
                if (session == IntPtr.Zero)
                    throw Failure("WinHttpOpen");

                connection = WinHttpConnect(session, host, HttpsPort, 0);
                if (connection == IntPtr.Zero)
                    throw Failure("WinHttpConnect");

                request = WinHttpOpenRequest(connection, "GET", path, null, null, IntPtr.Zero, FlagSecure);
                if (request == IntPtr.Zero)
                    throw Failure("WinHttpOpenRequest");

                foreach (KeyValuePair<string, string> header in headers ??
                         Array.Empty<KeyValuePair<string, string>>())
                {
                    string line = header.Key + ": " + header.Value;
                    if (!WinHttpAddRequestHeaders(request, line, (uint)line.Length, AddRequestHeaderAdd))
                        throw Failure("WinHttpAddRequestHeaders");
                }

                if (!WinHttpSendRequest(request, null, 0, IntPtr.Zero, 0, 0, UIntPtr.Zero))
                    throw Failure("WinHttpSendRequest");
                if (!WinHttpReceiveResponse(request, IntPtr.Zero))
                    throw Failure("WinHttpReceiveResponse");

                int status = (int)QueryNumber(request, QueryStatusCode, required: true);
                long total = QueryNumber(request, QueryContentLength, required: false);

                Read(request, destination, total, progress);
                return status;
            }
            finally
            {
                // Innermost first: WinHTTP handles are a chain, and closing a parent first leaves
                // the children dangling.
                foreach (IntPtr handle in new[] { request, connection, session })
                {
                    if (handle != IntPtr.Zero)
                        WinHttpCloseHandle(handle);
                }
            }
        }

        private static void Read(IntPtr request, Stream destination, long total, Action<long, long> progress)
        {
            var buffer = new byte[81920];
            long done = 0;

            while (true)
            {
                if (!WinHttpQueryDataAvailable(request, out uint available))
                    throw Failure("WinHttpQueryDataAvailable");
                if (available == 0)
                    break;

                int wanted = (int)Math.Min(available, (uint)buffer.Length);
                if (!WinHttpReadData(request, buffer, (uint)wanted, out uint read))
                    throw Failure("WinHttpReadData");
                if (read == 0)
                    break;

                destination.Write(buffer, 0, (int)read);
                done += read;
                progress?.Invoke(done, total);
            }

            progress?.Invoke(done, total == 0 ? done : total);
        }

        /// <summary>Reads one numeric response header, e.g. the status code or the content length.</summary>
        private static long QueryNumber(IntPtr request, uint infoLevel, bool required)
        {
            uint value = 0;
            uint size = sizeof(uint);

            if (WinHttpQueryHeaders(request, infoLevel | QueryFlagNumber, null, ref value, ref size, IntPtr.Zero))
                return value;

            if (required)
                throw Failure("WinHttpQueryHeaders");

            // Content-Length is genuinely absent on a chunked response.
            return 0;
        }

        /// <summary>
        /// Splits an https URL into host and path without <see cref="Uri"/>, which would cost 65 KB
        /// to parse four known-good URLs.
        /// </summary>
        private static void SplitUrl(string url, out string host, out string path)
        {
            const string scheme = "https://";
            if (url == null || !url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                throw new WebException("Only https URLs are supported here: " + url);

            string rest = url.Substring(scheme.Length);
            int slash = rest.IndexOf('/');

            host = slash < 0 ? rest : rest.Substring(0, slash);
            path = slash < 0 ? "/" : rest.Substring(slash);

            if (host.Length == 0)
                throw new WebException("No host in " + url);
        }

        /// <summary>
        /// The Win32 error with a plain-language note for the ones that actually happen.
        /// <c>Win32Exception</c> would say it better and drag in the component model to do it.
        /// </summary>
        private static WebException Failure(string call)
        {
            int error = Marshal.GetLastWin32Error();
            string note = error switch
            {
                12002 => " (timed out)",
                12007 => " (the host name could not be resolved - no connection?)",
                12029 => " (could not connect)",
                12030 => " (the connection was closed)",
                12175 => " (a TLS error - the certificate was not accepted)",
                _ => "",
            };

            return new WebException(call + " failed with Win32 error " + error + note);
        }

        // ---- winhttp.dll -----------------------------------------------------

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpen(string agent, uint accessType, string proxy,
                                                 string proxyBypass, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpConnect(IntPtr session, string serverName, ushort port,
                                                    uint reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpenRequest(IntPtr connection, string verb, string objectName,
                                                        string version, string referrer,
                                                        IntPtr acceptTypes, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpAddRequestHeaders(IntPtr request, string headers,
                                                            uint headersLength, uint modifiers);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpSendRequest(IntPtr request, string headers, uint headersLength,
                                                      IntPtr optional, uint optionalLength,
                                                      uint totalLength, UIntPtr context);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpQueryHeaders(IntPtr request, uint infoLevel, string name,
                                                       ref uint buffer, ref uint bufferLength,
                                                       IntPtr index);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpQueryDataAvailable(IntPtr request, out uint available);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, uint toRead,
                                                   out uint read);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpCloseHandle(IntPtr handle);
    }
}
