using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Plugin-host and MCP-op face over the AuraMono helpers. Lives outside FEATURE_MCP so a
    // shipping build can hand IHostApi.Mono to a dll in the plugins folder. The MCP ops in
    // McpOps.Mono.cs call the same methods.
    public partial class HeartopiaComplete
    {
        // ────────────────────────────────────────────────────────────────────────────────────────
        // The same reach, for `eval` snippets and sandbox plugins (IHostApi.Mono)
        // ────────────────────────────────────────────────────────────────────────────────────────
        //
        // Without this, a snippet can only touch the mod's PUBLIC surface — which excludes every
        // AuraMono helper — so `eval` could not reach a single game type. That gap is what made an
        // earlier claim ("eval already covers arbitrary invocation") false.

        internal IntPtr McpMonoFindMethod(IntPtr klass, string methodName, int paramCount)
        {
            if (klass == IntPtr.Zero || string.IsNullOrEmpty(methodName))
            {
                return IntPtr.Zero;
            }

            return this.FindAuraMonoMethodOnHierarchy(klass, methodName, paramCount);
        }

        // ── Object graph ─────────────────────────────────────────────────────────────────────────
        // The three capabilities that make AuraMono usable from sandbox code, and they only work as
        // a set: enumerate instances, read fields off them, and hold them safely in between. Ship
        // any one alone and it is either useless or a trap — enumeration without pins is the
        // documented mid-loop crash (AGENTS.md §11), because every member read allocates and SGen
        // moves what you are still holding.

        internal bool McpMonoGetComponents(IntPtr componentClass, List<IntPtr> objects, List<uint> pins,
                                           out string error)
        {
            error = null;
            if (componentClass == IntPtr.Zero)
            {
                error = "component class is null";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                // Fail closed. Handing back unpinned pointers would "work" until the first GC.
                error = "pinning unavailable on this build — refusing to hand out unpinned objects";
                return false;
            }

            if (!this.TryAuraMonoGetComponentObjects(componentClass, out List<IntPtr> found,
                    out bool infrastructureOk, pins) || found == null)
            {
                // "Ran and found nothing" is a SUCCESS with zero rows. Reporting it as a failure is
                // how a caller concludes their type is wrong when the world simply has none of them
                // right now.
                if (infrastructureOk)
                {
                    return true;
                }

                error = "GetComponents could not run for that class — it is not an ECS component "
                    + "type, the world is not up, or the generic method could not be inflated";
                return false;
            }

            objects.AddRange(found);
            return true;
        }

        internal bool McpMonoEnumerateCollection(IntPtr collectionObj, List<IntPtr> objects, List<uint> pins,
                                                 out string error)
        {
            error = null;
            if (collectionObj == IntPtr.Zero)
            {
                error = "collection is null";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                error = "pinning unavailable on this build — refusing to hand out unpinned objects";
                return false;
            }

            if (!this.TryEnumerateAuraMonoCollectionItems(collectionObj, objects, pins))
            {
                error = "not an enumerable mono collection, or enumeration failed";
                return false;
            }

            return true;
        }

        internal bool McpMonoTryGetField(IntPtr obj, string fieldName, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            return this.TryGetMonoObjectMember(obj, fieldName, out valueObj);
        }

        internal uint McpMonoGetUInt(IntPtr obj, string[] fieldNames)
        {
            if (obj == IntPtr.Zero || fieldNames == null || fieldNames.Length == 0)
            {
                return 0u;
            }

            return this.TryReadAuraMonoUIntField(obj, fieldNames);
        }

        internal int McpMonoGetInt(IntPtr boxedStruct, string fieldName)
        {
            if (boxedStruct == IntPtr.Zero || string.IsNullOrEmpty(fieldName))
            {
                return 0;
            }

            return this.TryReadAuraMonoStructIntField(boxedStruct, fieldName);
        }

        // mono_string_to_utf8 allocates; the result must go back through mono_free or it leaks.
        internal static string McpMonoGetString(IntPtr monoStringObj)
        {
            if (monoStringObj == IntPtr.Zero || auraMonoStringToUtf8 == null)
            {
                return null;
            }

            IntPtr utf8 = IntPtr.Zero;
            try
            {
                utf8 = auraMonoStringToUtf8(monoStringObj);
                return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (utf8 != IntPtr.Zero)
                {
                    auraMonoFree?.Invoke(utf8);
                }
            }
        }

        internal static bool McpMonoPinningAvailable => AuraMonoPinningAvailable;

        internal static uint McpMonoPin(IntPtr obj) => AuraMonoPinningAvailable ? AuraMonoPinNew(obj) : 0u;

        internal static void McpMonoUnpin(uint handle)
        {
            if (handle != 0u)
            {
                FreeAuraMonoPins(new List<uint> { handle });
            }
        }

        // Goes through the guarded invoke, never the raw export (CI lint E1/E2): null-method guard,
        // exception check and garbage-result suppression all live there.
        //
        // ⚠️ The returned IntPtr is a MonoObject* and is NOT safe to hold: SGen moves it as soon as
        // anything else allocates. Read what you need from it immediately, or pin it.
        internal bool McpMonoInvoke(IntPtr method, IntPtr instance, IntPtr[] args,
                                    out IntPtr result, out string error)
        {
            result = IntPtr.Zero;
            error = null;

            if (method == IntPtr.Zero)
            {
                error = "method is null";
                return false;
            }

            int count = args == null ? 0 : args.Length;
            IntPtr block = IntPtr.Zero;
            try
            {
                if (count > 0)
                {
                    block = Marshal.AllocHGlobal(IntPtr.Size * count);
                    for (int i = 0; i < count; i++)
                    {
                        Marshal.WriteIntPtr(block, i * IntPtr.Size, args[i]);
                    }
                }

                return TryAuraInvoke(method, instance, block, out result, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (block != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(block);
                }
            }
        }
    }
}
