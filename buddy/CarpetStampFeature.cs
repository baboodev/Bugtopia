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
        private const int CarpetStampChannelReliable = 1;    // ChannelType.Reliable
        private const int CarpetStampMaxRowsShown = 12;

        private readonly List<CarpetStampEntry> carpetStampScanResults = new List<CarpetStampEntry>();
        private string carpetStampStatus = "Not scanned yet.";
        private int carpetStampScanTotalActors;

        private IntPtr carpetStampUgcWorldClass = IntPtr.Zero;
        private IntPtr carpetStampWebRequestClass = IntPtr.Zero;
        private IntPtr carpetStampCommandClass = IntPtr.Zero;
        private IntPtr carpetStampSendCommandOpenMethod = IntPtr.Zero;
        private IntPtr carpetStampInflatedSendMethod = IntPtr.Zero;
        private IntPtr carpetStampFieldType = IntPtr.Zero;
        private IntPtr carpetStampFieldNetId = IntPtr.Zero;
        private IntPtr carpetStampFieldOperateMethod = IntPtr.Zero;
        private bool carpetStampSendResolveLogged;

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

        private bool TryCarpetStampEnsureSendResolved(out string status)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                status = "AuraMono API unavailable.";
                return false;
            }

            if (this.carpetStampWebRequestClass == IntPtr.Zero)
            {
                this.carpetStampWebRequestClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            }

            if (this.carpetStampCommandClass == IntPtr.Zero)
            {
                this.carpetStampCommandClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Build.UgcOperateCommand");
                if (this.carpetStampCommandClass == IntPtr.Zero)
                {
                    this.carpetStampCommandClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Build", "UgcOperateCommand");
                }
            }

            if (this.carpetStampWebRequestClass == IntPtr.Zero || this.carpetStampCommandClass == IntPtr.Zero)
            {
                status = "class missing: WebRequestUtility=" + (this.carpetStampWebRequestClass != IntPtr.Zero)
                    + " UgcOperateCommand=" + (this.carpetStampCommandClass != IntPtr.Zero);
                return false;
            }

            if (this.carpetStampSendCommandOpenMethod == IntPtr.Zero)
            {
                this.carpetStampSendCommandOpenMethod = this.FindAuraMonoMethodOnHierarchy(this.carpetStampWebRequestClass, "SendCommand", 3);
            }

            if (this.carpetStampSendCommandOpenMethod == IntPtr.Zero)
            {
                status = "SendCommand(3) missing on WebRequestUtility.";
                return false;
            }

            if (this.carpetStampInflatedSendMethod == IntPtr.Zero
                && !this.TryInstantCatchInflateAuraSendCommand(this.carpetStampSendCommandOpenMethod, this.carpetStampCommandClass, out this.carpetStampInflatedSendMethod))
            {
                status = "SendCommand<UgcOperateCommand> inflate failed.";
                return false;
            }

            if (this.carpetStampFieldType == IntPtr.Zero)
            {
                this.carpetStampFieldType = this.FindAuraMonoFieldOnHierarchy(this.carpetStampCommandClass, "Type");
            }

            if (this.carpetStampFieldNetId == IntPtr.Zero)
            {
                this.carpetStampFieldNetId = this.FindAuraMonoFieldOnHierarchy(this.carpetStampCommandClass, "NetId");
            }

            if (this.carpetStampFieldOperateMethod == IntPtr.Zero)
            {
                this.carpetStampFieldOperateMethod = this.FindAuraMonoFieldOnHierarchy(this.carpetStampCommandClass, "OperateMethod");
            }

            if (this.carpetStampFieldType == IntPtr.Zero || this.carpetStampFieldNetId == IntPtr.Zero || this.carpetStampFieldOperateMethod == IntPtr.Zero)
            {
                status = "UgcOperateCommand fields missing: Type=" + (this.carpetStampFieldType != IntPtr.Zero)
                    + " NetId=" + (this.carpetStampFieldNetId != IntPtr.Zero)
                    + " OperateMethod=" + (this.carpetStampFieldOperateMethod != IntPtr.Zero);
                return false;
            }

            if (!this.carpetStampSendResolveLogged)
            {
                this.carpetStampSendResolveLogged = true;
                CarpetStampLog("Resolve: WebRequestUtility=0x" + this.carpetStampWebRequestClass.ToInt64().ToString("X")
                    + " UgcOperateCommand=0x" + this.carpetStampCommandClass.ToInt64().ToString("X")
                    + " SendCommand(3)=0x" + this.carpetStampSendCommandOpenMethod.ToInt64().ToString("X")
                    + " inflated=0x" + this.carpetStampInflatedSendMethod.ToInt64().ToString("X")
                    + " fields Type/NetId/OperateMethod=0x" + this.carpetStampFieldType.ToInt64().ToString("X")
                    + "/0x" + this.carpetStampFieldNetId.ToInt64().ToString("X")
                    + "/0x" + this.carpetStampFieldOperateMethod.ToInt64().ToString("X"));
            }

            status = "ok";
            return true;
        }

        private unsafe bool TryCarpetStampSendOperate(uint carpetNetId, int ugcTypeValue, int skillId, string actionLabel, out string status)
        {
            if (!this.TryCarpetStampEnsureSendResolved(out status))
            {
                CarpetStampLog("Send " + actionLabel + " aborted: " + status);
                return false;
            }

            CarpetStampLog($"Send {actionLabel}: UgcOperateCommand Type={ugcTypeValue} NetId={carpetNetId} OperateMethod={skillId}"
                + $" needAuthed=1 channel=Reliable ({CarpetStampChannelReliable}), Params=null (game sends it null too)");

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.carpetStampCommandClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "command alloc failed.";
                CarpetStampLog("Send " + actionLabel + " failed: " + status);
                return false;
            }

            uint pin = AuraMonoPinNew(cmdObj);
            try
            {
                int typeValue = ugcTypeValue;
                uint netIdValue = carpetNetId;
                uint operateValue = unchecked((uint)skillId);
                auraMonoFieldSetValue(cmdObj, this.carpetStampFieldType, (IntPtr)(&typeValue));
                auraMonoFieldSetValue(cmdObj, this.carpetStampFieldNetId, (IntPtr)(&netIdValue));
                auraMonoFieldSetValue(cmdObj, this.carpetStampFieldOperateMethod, (IntPtr)(&operateValue));

                IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
                if (cmdPtr == IntPtr.Zero)
                {
                    status = "command unbox failed.";
                    CarpetStampLog("Send " + actionLabel + " failed: " + status);
                    return false;
                }

                int needAuthed = 1;
                int channel = CarpetStampChannelReliable;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = cmdPtr;
                args[1] = (IntPtr)(&needAuthed);
                args[2] = (IntPtr)(&channel);

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.carpetStampInflatedSendMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    string excText = this.TryGetMonoStringMember(exc, "Message", out string msg) && !string.IsNullOrWhiteSpace(msg)
                        ? msg
                        : ("exception 0x" + exc.ToInt64().ToString("X"));
                    status = "SendCommand threw: " + excText;
                    CarpetStampLog("Send " + actionLabel + " failed: " + status);
                    return false;
                }

                status = actionLabel + " sent (netId=" + carpetNetId + ", skill=" + skillId + ").";
                CarpetStampLog("Send " + actionLabel + " OK.");
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
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

        private float DrawCarpetStampSection(float y)
        {
            const float left = 40f;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, 460f, 24f), "Carpet Stamp (Slippery Rug)", headerStyle);
            y += 28f;

            GUI.Label(new Rect(left, y, 520f, 20f), "Scan party carpets on the map, send a single step-on (server speed buff).");
            y += 24f;

            if (GUI.Button(new Rect(left, y, 200f, 30f), "Scan Carpets", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                bool ok = this.TryCarpetStampScan(out string scanStatus);
                this.carpetStampStatus = scanStatus;
                this.AddMenuNotification("Carpet scan: " + scanStatus, ok ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.5f, 0.4f));
            }

            if (GUI.Button(new Rect(left + 210f, y, 200f, 30f), "Step On Nearest"))
            {
                CarpetStampEntry nearest = default;
                bool found = false;
                for (int i = 0; i < this.carpetStampScanResults.Count; i++)
                {
                    if (this.carpetStampScanResults[i].HasSkills)
                    {
                        nearest = this.carpetStampScanResults[i];
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    this.carpetStampStatus = "No steppable carpet in the last scan.";
                    this.AddMenuNotification(this.carpetStampStatus, new Color(1f, 0.5f, 0.4f));
                    CarpetStampLog("Step On Nearest: nothing steppable in snapshot (scan first).");
                }
                else
                {
                    bool ok = this.TryCarpetStampStepOn(nearest, out string stepStatus);
                    this.carpetStampStatus = stepStatus;
                    this.AddMenuNotification("Carpet step: " + stepStatus, ok ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.5f, 0.4f));
                }
            }
            y += 36f;

            GUI.Label(new Rect(left, y, 520f, 20f), "Status: " + this.carpetStampStatus);
            y += 26f;

            int rows = Math.Min(this.carpetStampScanResults.Count, CarpetStampMaxRowsShown);
            for (int i = 0; i < rows; i++)
            {
                CarpetStampEntry entry = this.carpetStampScanResults[i];
                string distText = entry.Distance >= 0f ? entry.Distance.ToString("F1") + "m" : "?";
                GUI.Label(new Rect(left, y, 330f, 22f), $"{entry.Label}  net={entry.NetId}  {distText}");

                if (entry.HasSkills)
                {
                    if (GUI.Button(new Rect(left + 335f, y, 55f, 22f), "On"))
                    {
                        bool ok = this.TryCarpetStampStepOn(entry, out string stepStatus);
                        this.carpetStampStatus = stepStatus;
                        this.AddMenuNotification("Carpet step: " + stepStatus, ok ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.5f, 0.4f));
                    }

                    if (GUI.Button(new Rect(left + 395f, y, 55f, 22f), "Off"))
                    {
                        bool ok = this.TryCarpetStampStepOff(entry, out string stepStatus);
                        this.carpetStampStatus = stepStatus;
                        this.AddMenuNotification("Carpet step: " + stepStatus, ok ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.5f, 0.4f));
                    }
                }
                else
                {
                    GUI.Label(new Rect(left + 335f, y, 120f, 22f), "(scan only)");
                }

                y += 24f;
            }

            if (this.carpetStampScanResults.Count > rows)
            {
                GUI.Label(new Rect(left, y, 460f, 20f), $"...and {this.carpetStampScanResults.Count - rows} more (see log).");
                y += 22f;
            }

            return y + 8f;
        }
    }
}
