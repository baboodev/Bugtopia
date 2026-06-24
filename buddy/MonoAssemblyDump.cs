using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    /// <summary>
    /// Dumps the game's decrypted, proprietary .NET assemblies from its embedded Mono runtime
    /// (<c>xdt_Data\Plugins\x86_64\mono-2.0-sgen.dll</c>) — only the XDENCODE game modules
    /// (EcsClient, EcsSystem, XDT*, Plugins, EngineWrapper, ScriptBridge, …); the BCL and other
    /// non-game assemblies in that runtime are skipped.
    ///
    /// These live in a SEPARATE runtime from the BepInEx/helper CoreCLR, so a managed
    /// <c>AssemblyLoadContext</c> hook never sees them. Here we talk to the Mono C API directly:
    /// enumerate every loaded assembly, find the raw PE buffer the game handed Mono after XDENCODE
    /// decryption, and write it out. <c>mono_image_get_raw_data</c> is not exported by this build, so
    /// the raw buffer is recovered from the <c>MonoImage</c> struct (offset discovered once, every
    /// native read guarded by VirtualQuery so a bad pointer can never crash the process).
    ///
    /// Output: <c>LocalLow/HelperSettings/DecryptedAssemblies/</c>. The folder's existence is an
    /// opt-in switch: present → auto-dump when the Mono runtime is ready (into an empty folder),
    /// then a second pass 60 s later for lazily loaded modules; absent → do nothing and never create it.
    /// </summary>
    internal static class MonoAssemblyDump
    {
        private const string MonoModule = "mono-2.0-sgen.dll";

        // Path computed WITHOUT creating the folder (an empty folder is the opt-in switch).
        private static string OutputDir => Path.Combine(HelperPaths.Root, "DecryptedAssemblies");

        // The dump runs only into an EMPTY DecryptedAssemblies folder: the folder must exist and
        // contain no entries. If it is missing, or already has files (a previous dump), nothing runs.
        private static bool Enabled
        {
            get
            {
                try
                {
                    return Directory.Exists(OutputDir)
                        && Directory.GetFileSystemEntries(OutputDir).Length == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        // The proprietary XDENCODE-encrypted game modules — the only assemblies we dump. The game's
        // BCL (System.*, mscorlib, …) and other Mono images are skipped. Any "XDT*" is also included
        // so new XDT modules in a future patch are still captured.
        private static readonly HashSet<string> GameModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EcsClient", "EcsSystem", "Plugins", "EngineWrapper", "ScriptBridge",
            "MonoShared", "MonoUniTask", "MsgPackFormatters", "XDKWPerf",
        };

        private static bool IsGameModule(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string baseName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4)
                : name;

            return GameModules.Contains(baseName)
                || baseName.StartsWith("XDT", StringComparison.OrdinalIgnoreCase);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetRootDomainDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ThreadAttachDelegate(IntPtr domain);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AssemblyForeachDelegate(IntPtr func, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AssemblyGetImageDelegate(IntPtr assembly);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ImageGetNameDelegate(IntPtr image);

        // GFunc callback: void (*)(gpointer assembly, gpointer user_data)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AssemblyForeachCallback(IntPtr assembly, IntPtr userData);

        // For IL deobfuscation: recover decrypted method bodies.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetMethodDelegate(IntPtr image, uint token, IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MethodGetHeaderDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr HeaderGetCodeDelegate(IntPtr header, out uint codeSize, out uint maxStack);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_GUARD = 0x100;
        private const uint PAGE_NOACCESS = 0x01;

        private static GetRootDomainDelegate _getRootDomain;
        private static ThreadAttachDelegate _threadAttach;
        private static AssemblyForeachDelegate _assemblyForeach;
        private static AssemblyGetImageDelegate _assemblyGetImage;
        private static ImageGetNameDelegate _imageGetName;
        private static GetMethodDelegate _getMethod;
        private static MethodGetHeaderDelegate _methodGetHeader;
        private static HeaderGetCodeDelegate _headerGetCode;

        private static AssemblyForeachCallback _collectCallbackRef; // keep alive across native call
        private static readonly List<IntPtr> _collected = new List<IntPtr>();

        private static int _rawDataOffset = -1; // discovered MonoImage->raw_data byte offset (raw_data_len at +8)
        private static bool _autoDumpDone;
        private static bool _retryScheduled;
        private const float RetryDelaySeconds = 60f;

        /// <summary>
        /// Auto-dump trigger, fired once when the game's Mono runtime/AuraFarm API first becomes ready.
        /// Runs only if the opt-in <c>DecryptedAssemblies</c> folder already exists; never creates it.
        /// </summary>
        public static void OnRuntimeReady()
        {
            if (_autoDumpDone)
            {
                return;
            }

            _autoDumpDone = true;

            if (!Directory.Exists(OutputDir))
            {
                ModLogger.Msg("[MonoDump] auto-dump skipped: DecryptedAssemblies folder is missing.");
                return;
            }

            if (Enabled)
            {
                try
                {
                    int n = DumpLoadedNow();
                    ModLogger.Msg("[MonoDump] auto-dump after runtime ready: " + n.ToString() + " game module(s).");
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[MonoDump] auto-dump error: " + ex.Message);
                }

                ScheduleDelayedRetry();
            }
            else
            {
                ModLogger.Msg("[MonoDump] initial dump skipped: folder not empty (clear to re-dump all).");
            }
        }

        private static void ScheduleDelayedRetry()
        {
            if (_retryScheduled || !Directory.Exists(OutputDir))
            {
                return;
            }

            _retryScheduled = true;
            ModCoroutines.Start(DelayedRetryRoutine());
        }

        private static IEnumerator DelayedRetryRoutine()
        {
            yield return new UnityEngine.WaitForSeconds(RetryDelaySeconds);

            if (!Directory.Exists(OutputDir))
            {
                yield break;
            }

            try
            {
                int n = DumpLoadedNow(onlyMissing: true);
                ModLogger.Msg(n > 0
                    ? "[MonoDump] delayed retry (" + RetryDelaySeconds.ToString("0") + "s): "
                        + n.ToString() + " additional game module(s)."
                    : "[MonoDump] delayed retry (" + RetryDelaySeconds.ToString("0") + "s): no new game modules.");
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[MonoDump] delayed retry error: " + ex.Message);
            }
        }

        /// <summary>Walk every loaded Mono assembly and dump the decrypted PE of each game module.</summary>
        private static int DumpLoadedNow(bool onlyMissing = false)
        {
            string outDir = OutputDir;

            IntPtr mono = GetModuleHandle(MonoModule);
            if (mono == IntPtr.Zero)
            {
                ModLogger.Warning("[MonoDump] " + MonoModule + " not loaded yet (enter the game world first).");
                return 0;
            }

            if (!ResolveExports(mono))
            {
                ModLogger.Warning("[MonoDump] could not resolve required Mono exports.");
                return 0;
            }

            try
            {
                IntPtr domain = _getRootDomain();
                if (domain != IntPtr.Zero)
                {
                    _threadAttach(domain);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[MonoDump] thread attach failed: " + ex.Message);
            }

            _collected.Clear();
            _collectCallbackRef = CollectImage;
            try
            {
                IntPtr fn = Marshal.GetFunctionPointerForDelegate(_collectCallbackRef);
                _assemblyForeach(fn, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[MonoDump] assembly_foreach failed: " + ex.Message);
                return 0;
            }

            int wrote = 0;
            int skipped = 0;
            int totalPatched = 0;
            foreach (IntPtr image in _collected)
            {
                try
                {
                    if (image == IntPtr.Zero)
                    {
                        continue;
                    }

                    string name = ReadCString(_imageGetName(image));
                    if (!IsGameModule(name))
                    {
                        continue; // skip BCL and other non-game Mono images
                    }

                    byte[] pe = ExtractRawData(image);
                    if (pe == null)
                    {
                        skipped++;
                        continue;
                    }

                    // The Mono raw_data buffer keeps a trailing XDENCODE signature block past the PE
                    // (e.g. ~298 bytes ending in "monogame@xindong.com"). Trim to the real image size
                    // so the output is a clean, byte-exact PE.
                    pe = TrimToImage(pe);

                    // Replace encrypted method bodies with the decrypted IL Mono exposes via the header
                    // API, so the written DLL is fully decompilable.
                    int patched = DeobfuscateIl(pe, image, name);
                    totalPatched += patched;

                    if (WritePe(outDir, name, pe, onlyMissing))
                    {
                        wrote++;
                    }
                }
                catch
                {
                    skipped++;
                }
            }

            ModLogger.Msg("[MonoDump] images=" + _collected.Count.ToString()
                + " gameWrote=" + wrote.ToString() + " skipped=" + skipped.ToString()
                + " ilPatched=" + totalPatched.ToString()
                + " rawOffset=" + _rawDataOffset.ToString() + " -> " + outDir);
            return wrote;
        }

        private static void CollectImage(IntPtr assembly, IntPtr userData)
        {
            try
            {
                if (assembly == IntPtr.Zero)
                {
                    return;
                }

                IntPtr image = _assemblyGetImage(assembly);
                if (image != IntPtr.Zero)
                {
                    _collected.Add(image);
                }
            }
            catch
            {
            }
        }

        private static bool ResolveExports(IntPtr mono)
        {
            _getRootDomain = GetExport<GetRootDomainDelegate>(mono, "mono_get_root_domain");
            _threadAttach = GetExport<ThreadAttachDelegate>(mono, "mono_thread_attach");
            _assemblyForeach = GetExport<AssemblyForeachDelegate>(mono, "mono_assembly_foreach");
            _assemblyGetImage = GetExport<AssemblyGetImageDelegate>(mono, "mono_assembly_get_image");
            _imageGetName = GetExport<ImageGetNameDelegate>(mono, "mono_image_get_name");

            // Optional — only needed for IL deobfuscation; dump still works without them.
            _getMethod = GetExport<GetMethodDelegate>(mono, "mono_get_method");
            _methodGetHeader = GetExport<MethodGetHeaderDelegate>(mono, "mono_method_get_header");
            _headerGetCode = GetExport<HeaderGetCodeDelegate>(mono, "mono_method_header_get_code");

            return _getRootDomain != null && _threadAttach != null && _assemblyForeach != null
                && _assemblyGetImage != null && _imageGetName != null;
        }

        private static T GetExport<T>(IntPtr module, string name) where T : class
        {
            IntPtr p = GetProcAddress(module, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        // Recover MonoImage->raw_data / raw_data_len. The struct layout is version-specific, so the
        // offset is discovered once by scanning the struct for a pointer to an 'MZ' buffer whose paired
        // length field yields a valid managed PE, then reused for every image.
        private static byte[] ExtractRawData(IntPtr image)
        {
            if (_rawDataOffset >= 0)
            {
                byte[] known = TryReadPe(image, _rawDataOffset);
                if (known != null)
                {
                    return known;
                }
            }

            for (int off = 0; off <= 0x400; off += IntPtr.Size)
            {
                if (off == _rawDataOffset)
                {
                    continue;
                }

                byte[] pe = TryReadPe(image, off);
                if (pe != null)
                {
                    _rawDataOffset = off;
                    return pe;
                }
            }

            return null;
        }

        private static byte[] TryReadPe(IntPtr image, int off)
        {
            IntPtr slot = IntPtr.Add(image, off);
            if (!IsReadable(slot, IntPtr.Size + 4))
            {
                return null;
            }

            IntPtr data = Marshal.ReadIntPtr(slot);
            if (data == IntPtr.Zero)
            {
                return null;
            }

            uint len = (uint)Marshal.ReadInt32(IntPtr.Add(slot, IntPtr.Size));
            if (len < 0x80 || len > 200u * 1024u * 1024u)
            {
                return null;
            }

            if (!IsReadable(data, (int)Math.Min(len, 0x1000u)))
            {
                return null;
            }

            // 'MZ'
            if (Marshal.ReadByte(data) != 0x4D || Marshal.ReadByte(data, 1) != 0x5A)
            {
                return null;
            }

            if (!IsReadable(data, (int)len))
            {
                return null;
            }

            byte[] buffer = new byte[len];
            Marshal.Copy(data, buffer, 0, (int)len);
            return HasBsjb(buffer) ? buffer : null;
        }

        // Compute the real PE file size from the section table and trim any trailing bytes
        // (the XDENCODE signature block) beyond it. Pure managed parsing — no native reads.
        private static byte[] TrimToImage(byte[] b)
        {
            try
            {
                if (b == null || b.Length < 0x40)
                {
                    return b;
                }

                int pe = BitConverter.ToInt32(b, 0x3C);
                if (pe <= 0 || pe + 24 > b.Length)
                {
                    return b;
                }

                if (b[pe] != 0x50 || b[pe + 1] != 0x45 || b[pe + 2] != 0 || b[pe + 3] != 0) // 'PE\0\0'
                {
                    return b;
                }

                int sections = BitConverter.ToUInt16(b, pe + 6);
                int optSize = BitConverter.ToUInt16(b, pe + 20);
                int sectionTable = pe + 24 + optSize;

                int end = 0;
                for (int i = 0; i < sections; i++)
                {
                    int sh = sectionTable + i * 40;
                    if (sh + 24 > b.Length)
                    {
                        return b;
                    }

                    int rawSize = BitConverter.ToInt32(b, sh + 16);
                    int rawPtr = BitConverter.ToInt32(b, sh + 20);
                    if (rawSize > 0 && rawPtr >= 0)
                    {
                        int sectionEnd = rawPtr + rawSize;
                        if (sectionEnd > end)
                        {
                            end = sectionEnd;
                        }
                    }
                }

                if (end > 0 && end < b.Length)
                {
                    byte[] trimmed = new byte[end];
                    Array.Copy(b, trimmed, end);
                    return trimmed;
                }
            }
            catch
            {
            }

            return b;
        }

        // ---- IL deobfuscation ------------------------------------------------------------------

        // Some method bodies are XDENCODE-encrypted in the static image (their IL is ciphertext that
        // only the game's Mono resolves). Mono exposes the DECRYPTED IL via mono_method_get_header /
        // mono_method_header_get_code, and the encryption is same-length, so we overwrite each
        // encrypted method's IL bytes in place. Tokens already match this assembly's metadata, so no
        // metadata rewrite is needed. Returns the number of methods patched.
        private static int DeobfuscateIl(byte[] pe, IntPtr image, string moduleName)
        {
            if (_getMethod == null || _methodGetHeader == null || _headerGetCode == null
                || image == IntPtr.Zero || pe == null)
            {
                return 0;
            }

            int patched = 0;
            int mismatched = 0;

            try
            {
                using (var ms = new MemoryStream(pe, writable: false))
                using (var per = new PEReader(ms))
                {
                    MetadataReader mdr = per.GetMetadataReader();
                    var sections = per.PEHeaders.SectionHeaders;

                    foreach (MethodDefinitionHandle handle in mdr.MethodDefinitions)
                    {
                        try
                        {
                            MethodDefinition def = mdr.GetMethodDefinition(handle);
                            int rva = def.RelativeVirtualAddress;
                            if (rva == 0)
                            {
                                continue; // abstract / native / pinvoke — no body
                            }

                            int bodyOffset = RvaToOffset(sections, rva);
                            if (bodyOffset < 0 || bodyOffset >= pe.Length)
                            {
                                continue;
                            }

                            int ilOffset, ilSize;
                            byte b0 = pe[bodyOffset];
                            if ((b0 & 0x3) == 0x2) // tiny header
                            {
                                ilSize = b0 >> 2;
                                ilOffset = bodyOffset + 1;
                            }
                            else if ((b0 & 0x3) == 0x3) // fat header
                            {
                                ilSize = BitConverter.ToInt32(pe, bodyOffset + 4);
                                ilOffset = bodyOffset + 12;
                            }
                            else
                            {
                                continue;
                            }

                            if (ilSize <= 0 || ilOffset + ilSize > pe.Length)
                            {
                                continue;
                            }

                            uint token = 0x06000000u | (uint)MetadataTokens.GetRowNumber(handle);
                            byte[] dec = GetDecryptedIl(image, token);
                            if (dec == null)
                            {
                                continue;
                            }

                            if (dec.Length != ilSize)
                            {
                                mismatched++;
                                continue; // same-length assumption broken — leave as-is
                            }

                            if (!RegionEquals(pe, ilOffset, dec))
                            {
                                Array.Copy(dec, 0, pe, ilOffset, ilSize);
                                patched++;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[MonoDump] deobf " + moduleName + " failed: " + ex.Message);
                return patched;
            }

            if (patched > 0 || mismatched > 0)
            {
                ModLogger.Msg("[MonoDump] deobf " + moduleName + ": patched=" + patched.ToString()
                    + (mismatched > 0 ? " (lengthMismatch=" + mismatched.ToString() + ")" : ""));
            }

            return patched;
        }

        private static byte[] GetDecryptedIl(IntPtr image, uint token)
        {
            try
            {
                IntPtr method = _getMethod(image, token, IntPtr.Zero);
                if (method == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr header = _methodGetHeader(method);
                if (header == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr code = _headerGetCode(header, out uint size, out uint _);
                if (code == IntPtr.Zero || size == 0 || size > 0x100000)
                {
                    return null;
                }

                byte[] il = new byte[size];
                Marshal.Copy(code, il, 0, (int)size);
                return il;
            }
            catch
            {
                return null;
            }
        }

        private static int RvaToOffset(System.Collections.Immutable.ImmutableArray<SectionHeader> sections, int rva)
        {
            foreach (SectionHeader s in sections)
            {
                int size = s.VirtualSize != 0 ? s.VirtualSize : s.SizeOfRawData;
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + size)
                {
                    return s.PointerToRawData + (rva - s.VirtualAddress);
                }
            }

            return -1;
        }

        private static bool RegionEquals(byte[] buffer, int offset, byte[] other)
        {
            for (int i = 0; i < other.Length; i++)
            {
                if (buffer[offset + i] != other[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsReadable(IntPtr addr, int size)
        {
            if (addr == IntPtr.Zero || size <= 0)
            {
                return false;
            }

            IntPtr cur = addr;
            long remaining = size;
            int guard = 0;
            while (remaining > 0 && guard++ < 4096)
            {
                if (VirtualQuery(cur, out MEMORY_BASIC_INFORMATION mbi,
                        (UIntPtr)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == UIntPtr.Zero)
                {
                    return false;
                }

                if (mbi.State != MEM_COMMIT)
                {
                    return false;
                }

                if ((mbi.Protect & PAGE_NOACCESS) != 0 || (mbi.Protect & PAGE_GUARD) != 0)
                {
                    return false;
                }

                long regionStart = mbi.BaseAddress.ToInt64();
                long regionEnd = regionStart + mbi.RegionSize.ToInt64();
                long covered = regionEnd - cur.ToInt64();
                if (covered <= 0)
                {
                    return false;
                }

                remaining -= covered;
                cur = new IntPtr(regionEnd);
            }

            return remaining <= 0;
        }

        private static bool HasBsjb(byte[] bytes)
        {
            // CLI metadata signature 'BSJB'; scan a generous prefix (header + metadata usually early).
            int limit = bytes.Length - 4;
            for (int i = 0; i < limit; i++)
            {
                if (bytes[i] == 0x42 && bytes[i + 1] == 0x53 && bytes[i + 2] == 0x4A && bytes[i + 3] == 0x42)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritePe(string outDir, string name, byte[] pe, bool skipExisting = false)
        {
            // Opt-in gate: write only when the folder exists; never create it implicitly.
            if (!Directory.Exists(outDir))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "mono_unknown_" + pe.Length.ToString();
            }

            string fileName = Path.GetFileName(name.Trim());
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".dll";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            string path = Path.Combine(outDir, fileName);
            if (skipExisting && File.Exists(path))
            {
                return false;
            }

            try
            {
                File.WriteAllBytes(path, pe);
                ModLogger.Msg("[MonoDump] saved " + fileName + " (" + pe.Length.ToString() + " bytes)");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[MonoDump] save " + fileName + " failed: " + ex.Message);
                return false;
            }
        }

        private static string ReadCString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringAnsi(ptr);
            }
            catch
            {
                return null;
            }
        }
    }
}
