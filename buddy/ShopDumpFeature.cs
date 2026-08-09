using System;
using UnityEngine;

namespace HeartopiaMod
{
    // What is left of the old shop-dump developer tool (deleted 2026-08-07 — its entry point
    // StartShopResearchDump had no caller, and the whole table-walk below it went with it).
    //
    // These three AuraMono readers survived because live features share them:
    //   FindAuraMonoTableDataClass   — AutoIceSkating, FaceShopBuyAll, SandSculpture, ItemDump
    //   TryGetMonoDictionaryEntryValue — FaceShopBuyAll
    //   TryReadMonoIntMember         — ItemDump
    // The file keeps its name only because renaming it would touch the csproj Compile list.
    public partial class HeartopiaComplete
    {
        private IntPtr FindAuraMonoTableDataClass()
        {
            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }

            return tableDataClass;
        }

        private IntPtr TryGetMonoDictionaryEntryValue(IntPtr entryObj)
        {
            if (entryObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr value) && value != IntPtr.Zero)
            {
                return value;
            }

            if (this.TryGetMonoObjectMember(entryObj, "value", out value) && value != IntPtr.Zero)
            {
                return value;
            }

            return IntPtr.Zero;
        }

        private int TryReadMonoIntMember(IntPtr obj, string memberName)
        {
            if (obj == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryGetMonoInt32Member(obj, memberName, out int value))
            {
                return value;
            }

            if (this.TryGetMonoIntMember(obj, memberName, out int fallback))
            {
                return fallback;
            }

            return 0;
        }
    }
}
