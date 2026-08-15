using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace HeartopiaMod
{
    // Carpet Stamp (New Features → Extra) — research tool for the party "stampede" carpets
    // (Slippery Rug 260242 / Slime Rug 260243, prefab p_mechanism_party_*carpet_1).
    //
    // Scan: walks the static UGCWorld._uActors dictionary (XDTGame.UGC.UGCWorld, one UActor per
    // UGC mechanism entity on the map) via AuraMono and snapshots every actor with
    // UgcType.StampedeInteraction (=1002) or a known carpet staticId. Everything found is logged.
    //
    // Step send: replays the exact wire command the game emits when the local player's capsule
    // enters/leaves the carpet trigger collider (UGCTriggerCase → LocalPlayerComponent.TriggerEnter
    // → PhysInteractionSystem → PhysEventSkill.Cast → Action_Command_UgcOperate):
    //   UgcOperateCommand { Type = actor UgcType, NetId = carpet netId, OperateMethod = (UgcOperateMethod)skillId }
    // The server resolves the skill's UgcServerAction itself: step-on = AddBuff 1003 (+20% move
    // speed, no expiry), step-off = AddBuff 1005 (+20% for 3 s) + RemoveBuff 1003. Skill ids come
    // from the decrypted Mechanism/Ugcskill tables and are per-staticId constants of this build.
    // WebRequestUtility + UgcOperateCommand are embedded-Mono only, so this reuses the shipped
    // AuraMono generic-inflation SendCommand<T> path (same as EnterDialogNode/PlayerEnterAreaCommand).
    public partial class HeartopiaComplete
    {
        private struct CarpetStampEntry
        {
            public uint NetId;
            public int StaticId;
            public int UgcTypeValue;
            public Vector3 Position;
            public bool HasPosition;
            public float Distance;
            public string Label;
            public bool HasSkills;
        }

        private sealed class CarpetStampSkillSet
        {
            public string Label;
            public int EnterSkillId;       // PhysEvent PlayerEnter (500003) → server AddBuff (permanent)
            public int ExitLingerSkillId;  // PhysEvent PlayerExit (500004) → server AddBuff (3 s linger)
            public int ExitRemoveSkillId;  // PhysEvent PlayerExit (500004) → server RemoveBuff (permanent one)
        }

        // Skill ids per carpet staticId, recovered from the decrypted cn.bytes tables
        // (Mechanism.ugcSkills → Ugcskill.trigger/_serverAction → UgcServerAction/BuffConfig).
        private static readonly Dictionary<int, CarpetStampSkillSet> CarpetStampSkillMap = new Dictionary<int, CarpetStampSkillSet>
        {
            // 260242 滑溜溜地毯 "Slippery Rug": enter → action 10117 AddBuff 1003 (+20% speed, t=-1);
            // exit → action 10119 AddBuff 1005 (+20%, 3 s) + action 10129 RemoveBuff 1003.
            { 260242, new CarpetStampSkillSet { Label = "Slippery Rug (speed+)", EnterSkillId = 500100065, ExitLingerSkillId = 500100071, ExitRemoveSkillId = 500100080 } },
            // 260243 史莱姆地毯 "Slime Rug": mirrored slow-down (buffs 1004/1006, -20% speed).
            { 260243, new CarpetStampSkillSet { Label = "Slime Rug (speed-)", EnterSkillId = 500100066, ExitLingerSkillId = 500100072, ExitRemoveSkillId = 500100081 } },
        };

        // Start/end point carpets share the stampede prefab family but drive race timing, not
        // buffs — listed by the scan for completeness, no step buttons.
        private static readonly Dictionary<int, string> CarpetStampKnownLabels = new Dictionary<int, string>
        {
            { 260240, "Start Point Rug" },
            { 260241, "End Point Rug" },
        };

        private const int CarpetStampStampedeUgcType = 1002; // UgcType.StampedeInteraction
        private const int CarpetStampMaxRowsShown = 12;

        private readonly List<CarpetStampEntry> carpetStampScanResults = new List<CarpetStampEntry>();
        private string carpetStampStatus = "Not scanned yet.";
        private int carpetStampScanTotalActors;

        private IntPtr carpetStampUgcWorldClass = IntPtr.Zero;

        private static void CarpetStampLog(string message)
        {
            ModLogger.Msg("[CarpetStamp] " + message);
        }

        // ===== Scan =====

        private bool TryCarpetStampScan(out string status)
        {
            Stopwatch sw = Stopwatch.StartNew();
            this.carpetStampScanResults.Clear();
            this.carpetStampScanTotalActors = 0;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono API unavailable (enter a world first).";
                CarpetStampLog("Scan aborted: " + status);
                return false;
            }

            if (this.carpetStampUgcWorldClass == IntPtr.Zero)
            {
                // UGCWorld lives in the XDTDataAndProtocol image despite the XDTGame.UGC namespace.
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    this.carpetStampUgcWorldClass = auraMonoClassFromName(dataImage, "XDTGame.UGC", "UGCWorld");
                }

                if (this.carpetStampUgcWorldClass == IntPtr.Zero)
                {
                    this.carpetStampUgcWorldClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UGC", "UGCWorld");
                }

                CarpetStampLog("Resolve: UGCWorld class=0x" + this.carpetStampUgcWorldClass.ToInt64().ToString("X"));
            }

            if (this.carpetStampUgcWorldClass == IntPtr.Zero)
            {
                status = "UGCWorld class unavailable.";
                CarpetStampLog("Scan aborted: " + status);
                return false;
            }

            if (!this.TryGetAuraMonoStaticObjectField(this.carpetStampUgcWorldClass, "_uActors", out IntPtr actorsDict)
                || actorsDict == IntPtr.Zero)
            {
                status = "UGCWorld._uActors unavailable (no UGC mechanisms loaded?).";
                CarpetStampLog("Scan aborted: " + status);
                return false;
            }

            bool playerPosKnown = this.TryGetLocalPlayerPosition(out Vector3 playerPos);
            CarpetStampLog("Scan: _uActors dict=0x" + actorsDict.ToInt64().ToString("X")
                + " playerPos=" + (playerPosKnown ? FormatCarpetStampVector(playerPos) : "unknown"));

            List<IntPtr> entries = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            if (!this.TryEnumerateAuraMonoCollectionItems(actorsDict, entries, pins) || entries.Count == 0)
            {
                FreeAuraMonoPins(pins);
                status = "No UGC actors on this map (dictionary empty).";
                CarpetStampLog("Scan done: " + status);
                return false;
            }

            int carpets = 0;
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    this.carpetStampScanTotalActors++;

                    uint netId = 0U;
                    if (!this.TryGetMonoUInt32Member(entry, "Key", out netId) && !this.TryGetMonoUInt32Member(entry, "key", out netId))
                    {
                        CarpetStampLog($"actor[{i}]: Key read failed, skipped.");
                        continue;
                    }

                    IntPtr actorObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entry, "Value", out actorObj) || actorObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "value", out actorObj) || actorObj == IntPtr.Zero))
                    {
                        CarpetStampLog($"actor[{i}]: netId={netId} Value read failed, skipped.");
                        continue;
                    }

                    // The pins list only covers the boxed KVP entries; the UActor / entity objects
                    // read out of them are separate heap objects — pin them across their member
                    // reads (each read allocates mono-side, so the moving SGen GC could relocate
                    // them mid-loop otherwise).
                    int staticId = 0;
                    bool staticIdKnown;
                    int ugcType = -1;
                    bool ugcTypeKnown;
                    Vector3 pos = Vector3.zero;
                    bool hasPos = false;
                    uint actorPin = AuraMonoPinNew(actorObj);
                    try
                    {
                        staticIdKnown = this.TryGetMonoInt32Member(actorObj, "StaticId", out staticId);
                        ugcTypeKnown = this.TryGetMonoInt32Member(actorObj, "UgcType", out ugcType);

                        if (this.TryGetMonoObjectMember(actorObj, "_entity", out IntPtr entityObj) && entityObj != IntPtr.Zero)
                        {
                            uint entityPin = AuraMonoPinNew(entityObj);
                            try
                            {
                                hasPos = this.TryGetAuraMonoEntityPosition(entityObj, out pos);
                            }
                            finally
                            {
                                AuraMonoPinFree(entityPin);
                            }
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(actorPin);
                    }

                    float dist = (hasPos && playerPosKnown) ? Vector3.Distance(playerPos, pos) : -1f;

                    bool hasSkills = staticIdKnown && CarpetStampSkillMap.ContainsKey(staticId);
                    bool isCarpet = hasSkills
                        || (staticIdKnown && CarpetStampKnownLabels.ContainsKey(staticId))
                        || (ugcTypeKnown && ugcType == CarpetStampStampedeUgcType);

                    string label = hasSkills
                        ? CarpetStampSkillMap[staticId].Label
                        : (staticIdKnown && CarpetStampKnownLabels.TryGetValue(staticId, out string known)
                            ? known
                            : (isCarpet ? "Stampede mechanism" : "UGC mechanism"));

                    CarpetStampLog($"actor[{i}]: netId={netId} staticId={(staticIdKnown ? staticId.ToString() : "?")}"
                        + $" ugcType={(ugcTypeKnown ? ugcType.ToString() : "?")}"
                        + $" pos={(hasPos ? FormatCarpetStampVector(pos) : "?")}"
                        + $" dist={(dist >= 0f ? dist.ToString("F1") + "m" : "?")}"
                        + $" carpet={(isCarpet ? "YES (" + label + ")" : "no")}");

                    if (!isCarpet)
                    {
                        continue;
                    }

                    carpets++;
                    this.carpetStampScanResults.Add(new CarpetStampEntry
                    {
                        NetId = netId,
                        StaticId = staticIdKnown ? staticId : 0,
                        UgcTypeValue = ugcTypeKnown ? ugcType : CarpetStampStampedeUgcType,
                        Position = pos,
                        HasPosition = hasPos,
                        Distance = dist,
                        Label = label,
                        HasSkills = hasSkills,
                    });
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            this.carpetStampScanResults.Sort((a, b) =>
            {
                float da = a.Distance >= 0f ? a.Distance : float.MaxValue;
                float db = b.Distance >= 0f ? b.Distance : float.MaxValue;
                return da.CompareTo(db);
            });

            sw.Stop();
            status = $"{carpets} carpet(s) of {this.carpetStampScanTotalActors} UGC actor(s), {sw.ElapsedMilliseconds} ms.";
            CarpetStampLog("Scan done: " + status);
            return carpets > 0;
        }

        private static string FormatCarpetStampVector(Vector3 v)
        {
            return $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
        }

        // ===== Send =====

        private bool TryCarpetStampSendOperate(uint carpetNetId, int ugcTypeValue, int skillId, string actionLabel, out string status)
        {
            CarpetStampLog($"Send {actionLabel}: UgcOperateCommand Type={ugcTypeValue} NetId={carpetNetId} OperateMethod={skillId}"
                + $" needAuthed=1 channel=Reliable ({AuraChannelReliable}), Params=null (game sends it null too)");

            // Type is int, NetId and OperateMethod are uint on the command — the shared sender takes
            // the write width from each value's runtime type, so these three casts are load-bearing.
            // OperateMethod keeps its unchecked cast: skill ids arrive as int and must reinterpret,
            // not range-check.
            if (!this.TryAuraSendCommand("XDT.Scene.Shared.Modules.Build.UgcOperateCommand",
                    new Dictionary<string, object>
                    {
                        ["Type"] = ugcTypeValue,
                        ["NetId"] = carpetNetId,
                        ["OperateMethod"] = unchecked((uint)skillId),
                    },
                    AuraChannelReliable, true, out status))
            {
                CarpetStampLog("Send " + actionLabel + " failed: " + status);
                return false;
            }

            status = actionLabel + " sent (netId=" + carpetNetId + ", skill=" + skillId + ").";
            CarpetStampLog("Send " + actionLabel + " OK.");
            return true;
        }

        // Single step-on: the PlayerEnter skill → server AddBuff (Slippery Rug: 1003, +20% speed, no expiry).
        private bool TryCarpetStampStepOn(CarpetStampEntry entry, out string status)
        {
            if (!entry.HasSkills || !CarpetStampSkillMap.TryGetValue(entry.StaticId, out CarpetStampSkillSet skills))
            {
                status = "No mapped skills for staticId " + entry.StaticId + ".";
                return false;
            }

            CarpetStampLog($"Step ON {entry.Label}: netId={entry.NetId} staticId={entry.StaticId}"
                + $" dist={(entry.Distance >= 0f ? entry.Distance.ToString("F1") + "m" : "?")}"
                + $" enterSkill={skills.EnterSkillId} (trigger PlayerEnter 500003 → server AddBuff)");
            return this.TryCarpetStampSendOperate(entry.NetId, entry.UgcTypeValue, skills.EnterSkillId, "step-on", out status);
        }

        // Step-off completes the stamp cycle the way a real exit does: both PlayerExit skills in
        // ugcSkills order — AddBuff 3 s linger first, then RemoveBuff of the permanent one.
        private bool TryCarpetStampStepOff(CarpetStampEntry entry, out string status)
        {
            if (!entry.HasSkills || !CarpetStampSkillMap.TryGetValue(entry.StaticId, out CarpetStampSkillSet skills))
            {
                status = "No mapped skills for staticId " + entry.StaticId + ".";
                return false;
            }

            CarpetStampLog($"Step OFF {entry.Label}: netId={entry.NetId} staticId={entry.StaticId}"
                + $" lingerSkill={skills.ExitLingerSkillId} removeSkill={skills.ExitRemoveSkillId} (trigger PlayerExit 500004)");
            bool lingerOk = this.TryCarpetStampSendOperate(entry.NetId, entry.UgcTypeValue, skills.ExitLingerSkillId, "step-off linger", out string lingerStatus);
            bool removeOk = this.TryCarpetStampSendOperate(entry.NetId, entry.UgcTypeValue, skills.ExitRemoveSkillId, "step-off remove", out string removeStatus);
            status = lingerOk && removeOk
                ? "step-off sent (linger + remove)."
                : "step-off partial: linger=" + lingerStatus + " remove=" + removeStatus;
            return lingerOk && removeOk;
        }

        // ===== GUI (New Features → Extra) =====

    }
}
