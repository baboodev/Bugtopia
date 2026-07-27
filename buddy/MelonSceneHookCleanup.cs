using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Removes the two PERMANENT inline hooks MelonLoader installs inside GameAssembly.dll's .text.
    //
    // WHY: MelonLoader's IL2CPP support module (MelonLoader/Dependencies/SupportModules/Il2Cpp.dll)
    // does this in MelonLoader.Support.Main.Initialize -> SceneHandler.Init:
    //     Core.HarmonyInstance.Patch(SceneManager.Internal_SceneLoaded,   prefix)
    //     Core.HarmonyInstance.Patch(SceneManager.Internal_SceneUnloaded, prefix)
    // Those MethodInfos come from the Il2CppInterop wrapper, so Harmony routes them through
    // Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher.DetourTo, which does
    //     DetourProvider.Create(originalNativeMethodInfo.MethodPointer, delegate).Apply()
    // i.e. an inline JMP written at the method's NATIVE code address — inside GameAssembly.dll's
    // .text — via Dobby, and it is never disposed. BepInEx does the opposite: its only detour is on
    // the EXPORTED il2cpp_runtime_invoke and it calls RuntimeInvokeDetour.Dispose() once
    // Internal_ActiveSceneChanged is seen, and it Harmony-patches nothing.
    //
    // Both loaders share Il2CppInterop's 5 ClassInjector hooks on IL2CPP VM internals (required to
    // inject our MonoBehaviour) — we do NOT touch those. Only the 2 scene-callback hooks on real
    // engine code are removed.
    //
    // SAFETY MODEL
    //  * Loader gate: runs only when MelonLoader is the live loader. Under BepInEx (including the
    //    Universal build running under BepInEx) the lookups below fail, so this is a hard no-op.
    //    Nothing here references a MelonLoader type at COMPILE time — it is all string reflection —
    //    so the BepInEx-only flavor (which does not reference MelonLoader.dll) still compiles and
    //    never JIT-touches a MelonLoader type.
    //  * Attribution: the detour address comes from Il2CppDetourMethodPatcher.DelegateCache, whose
    //    entries are the very delegates handed to DetourProvider.Create. We require EXACTLY ONE
    //    whose dynamic-method name ("(il2cpp -> managed) <Original.Name>") matches the scene method.
    //    No unique match => we touch no memory at all.
    //    NOTE: MelonLoader's own registry (NativeStackWalk._registeredHooks) is NOT usable here —
    //    MelonDetour.Apply registers `&detourFrom`, the address of a STACK LOCAL, not the patched
    //    function address, so its keys are stale stack addresses.
    //  * Detach mirrors MelonDetour.Dispose exactly: BootstrapInterop.NativeHookDetach(nint,nint)
    //    casts its first arg to nint*, i.e. it wants a POINTER TO a slot holding the original
    //    function address, so we hand it an unmanaged slot we own rather than the address itself.
    //  * Timing: called from OnUpdate. Under MelonLoader OnUpdate is driven BY SM_Component
    //    (SM_Component.Update -> Main.Interface.Update -> MelonMod.OnUpdate), so reaching this code
    //    proves SM_Component already exists — the one thing the scene hook was still needed for
    //    (SceneHandler.OnSceneLoad calls SM_Component.Create). It is DontDestroyOnLoad and self-heals
    //    (OnDestroy -> Create), so the update/GUI pump survives. Same-thread by construction: Unity
    //    scene callbacks and Update both run on the main thread, so we cannot detach while something
    //    is executing inside the hook.
    //  * Fail-closed: the latch is set BEFORE any work, so a throw can never produce a retry loop.
    //
    // WHAT IS LOST: MelonMod.OnSceneWasLoaded / OnSceneWasUnloaded / OnSceneWasInitialized stop
    // firing. This mod overrides none of them (MelonLoaderPlugin.cs) and its world-ready gate polls
    // the GameWorld FSM rather than loader scene events, so nothing here depends on them.
    public partial class HeartopiaComplete
    {
        // Diagnostics (surfaced in the log).
        internal static int melonSceneHooksDetached;
        internal static string melonSceneHookStatus = "not run";

        private bool melonSceneHookCleanupDone;

        private static readonly string[] MelonSceneHookMethods = { "Internal_SceneLoaded", "Internal_SceneUnloaded" };

        // Il2CppDetourMethodPatcher names its generated native->managed trampoline like this.
        private const string MelonDetourDynMethodPrefix = "(il2cpp -> managed) ";

        private void TryCleanupMelonSceneHooks()
        {
            if (this.melonSceneHookCleanupDone)
            {
                return;
            }

            // Latch FIRST: one attempt for the whole session, whatever happens below.
            this.melonSceneHookCleanupDone = true;

            try
            {
                // --- Gate 1: our own loader flag (false under BepInEx, incl. Universal-on-BepInEx).
                if (!ModLoaderInfo.IsMelonLoader)
                {
                    melonSceneHookStatus = "skipped (not MelonLoader)";
                    return;
                }

                // --- Gate 2: belt and braces — MelonLoader must actually be in the AppDomain.
                Assembly melon = FindLoadedAssemblyByName("MelonLoader");
                if (melon == null)
                {
                    melonSceneHookStatus = "skipped (MelonLoader assembly absent)";
                    return;
                }

                // --- ML's detach entry point: public static void NativeHookDetach(nint, nint).
                MethodInfo detach = ResolveMelonNativeHookDetach(melon, out string detachStatus);
                if (detach == null)
                {
                    melonSceneHookStatus = "detach unavailable: " + detachStatus;
                    ModLogger.Msg("[MelonSceneHook] " + melonSceneHookStatus);
                    return;
                }

                // --- Il2CppInterop's cache of the detour delegates (gives us each detour address).
                IList cache = ResolveDetourDelegateCache(out string cacheStatus);
                if (cache == null)
                {
                    melonSceneHookStatus = "delegate cache unavailable: " + cacheStatus;
                    ModLogger.Msg("[MelonSceneHook] " + melonSceneHookStatus);
                    return;
                }

                ModLogger.Msg("[MelonSceneHook] resolved detach + delegate cache (" + cacheStatus + ")");

                int removed = 0;
                for (int i = 0; i < MelonSceneHookMethods.Length; i++)
                {
                    if (this.TryDetachMelonSceneHook(MelonSceneHookMethods[i], cache, detach))
                    {
                        removed++;
                    }
                }

                melonSceneHooksDetached = removed;
                melonSceneHookStatus = "detached " + removed + "/" + MelonSceneHookMethods.Length;
                ModLogger.Msg("[MelonSceneHook] " + melonSceneHookStatus
                              + " (GameAssembly .text scene hooks removed; SM_Component pump unaffected)");
            }
            catch (Exception ex)
            {
                melonSceneHookStatus = "failed: " + ex.Message;
                ModLogger.Msg("[MelonSceneHook] cleanup failed: " + ex);
            }
        }

        private bool TryDetachMelonSceneHook(string methodName, IList delegateCache, MethodInfo detach)
        {
            IntPtr slot = IntPtr.Zero;
            try
            {
                // 1. The Il2CppInterop wrapper method for the engine's scene callback.
                MethodInfo wrapper = ResolveSceneManagerMethod(methodName);
                if (wrapper == null)
                {
                    ModLogger.Msg("[MelonSceneHook] " + methodName + ": interop method not found — skipped");
                    return false;
                }

                // 2. Its native IL2CPP method pointer == the address Harmony/Dobby patched.
                IntPtr target = ResolveIl2CppMethodPointer(wrapper, out string ptrStatus);
                if (target == IntPtr.Zero)
                {
                    ModLogger.Msg("[MelonSceneHook] " + methodName + ": native pointer unavailable (" + ptrStatus + ") — skipped");
                    return false;
                }

                // 3. The detour delegate for THIS method (unique name match, else refuse).
                IntPtr detour = ResolveDetourPointer(delegateCache, methodName, out string detourStatus);
                if (detour == IntPtr.Zero)
                {
                    ModLogger.Msg("[MelonSceneHook] " + methodName + " @0x" + target.ToInt64().ToString("X")
                                  + ": no unique detour delegate (" + detourStatus + ") — nothing removed");
                    return false;
                }

                // 4. Snapshot the prologue so the log can prove the bytes actually changed back.
                string before = ReadPrologueHex(target, 12);

                // 5. NativeHookDetach wants a POINTER to a slot holding the original address.
                slot = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(slot, target);
                detach.Invoke(null, new object[] { slot, detour });

                string after = ReadPrologueHex(target, 12);
                bool changed = !string.Equals(before, after, StringComparison.Ordinal);

                ModLogger.Msg("[MelonSceneHook] " + methodName + " @0x" + target.ToInt64().ToString("X")
                              + " detour 0x" + detour.ToInt64().ToString("X")
                              + " -> detach " + (changed ? "OK" : "NO-CHANGE")
                              + " [" + before + " => " + after + "]");

                return changed;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MelonSceneHook] " + methodName + " detach failed: " + ex.Message);
                return false;
            }
            finally
            {
                if (slot != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(slot); } catch { }
                }
            }
        }

        // ---- resolution helpers (string-based: no MelonLoader type referenced at compile time) ----

        private static Assembly FindLoadedAssemblyByName(string simpleName)
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (string.Equals(all[i].GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return all[i];
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static Type FindTypeByShortName(Assembly asm, string shortName)
        {
            try
            {
                Type[] types = asm.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    if (string.Equals(types[i].Name, shortName, StringComparison.Ordinal))
                    {
                        return types[i];
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        // MelonLoader.InternalUtils.BootstrapInterop.NativeHookDetach(nint target, nint detour)
        private static MethodInfo ResolveMelonNativeHookDetach(Assembly melon, out string status)
        {
            status = string.Empty;
            Type boot = melon.GetType("MelonLoader.InternalUtils.BootstrapInterop", throwOnError: false)
                        ?? FindTypeByShortName(melon, "BootstrapInterop");
            if (boot == null)
            {
                status = "BootstrapInterop type not found";
                return null;
            }

            MethodInfo[] candidates = boot.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.Equals(candidates[i].Name, "NativeHookDetach", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] ps = candidates[i].GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(IntPtr) && ps[1].ParameterType == typeof(IntPtr))
                {
                    status = "ok";
                    return candidates[i];
                }
            }

            status = "NativeHookDetach(IntPtr,IntPtr) not found";
            return null;
        }

        // Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher.DelegateCache (private static List<object>)
        private static IList ResolveDetourDelegateCache(out string status)
        {
            status = string.Empty;
            Assembly hs = FindLoadedAssemblyByName("Il2CppInterop.HarmonySupport");
            if (hs == null)
            {
                status = "Il2CppInterop.HarmonySupport not loaded";
                return null;
            }

            Type patcher = hs.GetType("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher", throwOnError: false)
                           ?? FindTypeByShortName(hs, "Il2CppDetourMethodPatcher");
            if (patcher == null)
            {
                status = "Il2CppDetourMethodPatcher type not found";
                return null;
            }

            FieldInfo field = patcher.GetField("DelegateCache", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                status = "DelegateCache field not found";
                return null;
            }

            IList list = field.GetValue(null) as IList;
            if (list == null)
            {
                status = "DelegateCache null/!IList";
                return null;
            }

            status = "delegates=" + list.Count;
            return list;
        }

        // Each cached entry is the delegate passed to DetourProvider.Create for one patched method;
        // its dynamic method is named "(il2cpp -> managed) <OriginalName>". Require a UNIQUE match.
        private static IntPtr ResolveDetourPointer(IList cache, string methodName, out string status)
        {
            status = string.Empty;
            string wanted = MelonDetourDynMethodPrefix + methodName;
            Delegate match = null;
            int hits = 0;

            for (int i = 0; i < cache.Count; i++)
            {
                Delegate d = cache[i] as Delegate;
                if (d == null)
                {
                    continue;
                }

                string name;
                try
                {
                    name = d.Method != null ? d.Method.Name : null;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // Match on the exact generated name, or fall back to a contains-check in case a future
                // Il2CppInterop tweaks the prefix (still anchored on the unique engine method name).
                if (string.Equals(name, wanted, StringComparison.Ordinal)
                    || name.IndexOf(methodName, StringComparison.Ordinal) >= 0)
                {
                    hits++;
                    match = d;
                }
            }

            if (hits != 1 || match == null)
            {
                status = "matches=" + hits + " of " + cache.Count;
                return IntPtr.Zero;
            }

            try
            {
                IntPtr fp = Marshal.GetFunctionPointerForDelegate(match);
                status = fp == IntPtr.Zero ? "function pointer null" : "ok";
                return fp;
            }
            catch (Exception ex)
            {
                status = "GetFunctionPointerForDelegate: " + ex.Message;
                return IntPtr.Zero;
            }
        }

        // The interop wrapper for UnityEngine.SceneManagement.SceneManager — resolved exactly the way
        // MelonLoader.Support.Main.GetSceneManagerMethods does it, so we target the same MethodInfo.
        private static MethodInfo ResolveSceneManagerMethod(string methodName)
        {
            Type sm = null;
            string[] asmNames = { "UnityEngine.CoreModule", "UnityEngine" };
            for (int i = 0; i < asmNames.Length && sm == null; i++)
            {
                try
                {
                    Assembly a = FindLoadedAssemblyByName(asmNames[i]) ?? Assembly.Load(asmNames[i]);
                    if (a != null)
                    {
                        sm = a.GetType("UnityEngine.SceneManagement.SceneManager", throwOnError: false);
                    }
                }
                catch
                {
                }
            }

            if (sm == null)
            {
                return null;
            }

            return sm.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
                   ?? sm.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        }

        // Il2CppInterop stores each generated method's Il2CppMethodInfo* in a private static field
        // named NativeMethodInfoPtr_<Method>_<sig>. The struct's FIRST field is methodPointer
        // (IL2CPP MethodInfo layout, Unity 2020.3) — that address is what Harmony/Dobby patched.
        // NOTE: Il2CppDetourMethodPatcher redirects only its own COPY of the MethodInfo, so the real
        // struct this field points at still holds the original code address after patching.
        private static IntPtr ResolveIl2CppMethodPointer(MethodInfo wrapper, out string status)
        {
            status = string.Empty;
            try
            {
                Type declaring = wrapper.DeclaringType;
                if (declaring == null)
                {
                    status = "no declaring type";
                    return IntPtr.Zero;
                }

                string wanted = "NativeMethodInfoPtr_" + wrapper.Name;
                FieldInfo match = null;
                FieldInfo[] fields = declaring.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].FieldType == typeof(IntPtr)
                        && fields[i].Name.StartsWith(wanted, StringComparison.Ordinal))
                    {
                        if (match != null)
                        {
                            // Overloaded name — ambiguous, refuse rather than guess at an address.
                            status = "ambiguous NativeMethodInfoPtr fields";
                            return IntPtr.Zero;
                        }

                        match = fields[i];
                    }
                }

                if (match == null)
                {
                    status = "NativeMethodInfoPtr field not found";
                    return IntPtr.Zero;
                }

                IntPtr methodInfoPtr = (IntPtr)match.GetValue(null);
                if (methodInfoPtr == IntPtr.Zero)
                {
                    status = "Il2CppMethodInfo* null (method not resolved yet)";
                    return IntPtr.Zero;
                }

                IntPtr methodPointer = Marshal.ReadIntPtr(methodInfoPtr);
                if (methodPointer == IntPtr.Zero)
                {
                    status = "methodPointer null";
                    return IntPtr.Zero;
                }

                status = "ok";
                return methodPointer;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return IntPtr.Zero;
            }
        }

        private static string ReadPrologueHex(IntPtr addr, int count)
        {
            try
            {
                char[] hex = new char[count * 2];
                for (int i = 0; i < count; i++)
                {
                    byte b = Marshal.ReadByte(addr, i);
                    hex[i * 2] = GetHexNibble(b >> 4);
                    hex[i * 2 + 1] = GetHexNibble(b & 0xF);
                }

                return new string(hex);
            }
            catch
            {
                return "<unreadable>";
            }
        }

        private static char GetHexNibble(int v) => (char)(v < 10 ? ('0' + v) : ('A' + (v - 10)));
    }
}
