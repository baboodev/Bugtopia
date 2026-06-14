using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Plan B (docs/plans/2026-06-10-pad-build-api-migration.md + build-mode rotation analysis):
    // rotate a focused/placed homeland object to an ARBITRARY angle and send it straight to the
    // server, bypassing the client-side 45/90 snap.
    //
    // Why this works:
    //   - The server stores rotation as an int in BuildTransformData.Angle, packed by
    //     ToBuildingRotValue: (x<<20)|(z<<10)|y, i.e. integer degrees per axis (1° resolution).
    //     The 45/90 snap is purely client-side (CraftMath.ReducePrecision at confirm).
    //   - So we read the focused object's current local transform (element.root) + netId via the
    //     already-resolved BuildModule (AuraMono), pack the desired Y angle ourselves, and send a
    //     BuildMoveData through HomelandProtocolManager.SendBuildBatchOperation — never touching
    //     ReducePrecision.
    //
    // Scope/safety: only acts on an EXISTING focused object (server netId, not a brand-new local
    // placement) while SubState==Focus. Regular furniture has Ext=0/Curve=0 (only size-editable
    // walls/roofs differ), so we send Ext=0/Curve=0 and leave virtual links unchanged. Anything
    // unreadable ⇒ logged no-op (never sends a malformed op). Verify in-game with MasterLogPadBuild.
    public partial class HeartopiaComplete
    {
        private int buildingFreeRotateAngleY;       // desired absolute Y angle (degrees, 0..359)
        private int buildingFreeRotateStep = 15;    // +/- button step
        private string buildingFreeRotateStatus = "Focus an existing object in build mode, set Y, Apply.";

        // Free-snap toggles. While on + an object focused, the focused BuildComponent's snap config
        // is overridden to the finest step: angle = _buildBoxData.putDatas[0].rotateAngle (the 45/90
        // step source, used by interactive rotate, alignment, and confirm-ReducePrecision), grid =
        // _putitem.precision (cell = Clamp(precision,1,8)*0.25, min 0.25 m). Both are shared config
        // ref-objects, so originals are cached per object and restored when the toggle turns off.
        private int buildingFreeAngleStep = 1;     // user-set angle step (deg), 1..90
        private float buildingFreeGridCell = 0.25f; // user-set cell size (m), 0.01..0.25
        private bool buildingFreeAngleEnabled;
        private bool buildingFreeGridEnabled;
        private bool buildingFreeAnglePrev;
        private bool buildingFreeGridPrev;
        private float buildingFreeSnapNextApplyAt = -999f;
        private readonly System.Collections.Generic.Dictionary<IntPtr, int> buildingAngleOriginals = new System.Collections.Generic.Dictionary<IntPtr, int>();
        private readonly System.Collections.Generic.Dictionary<IntPtr, float> buildingGridOriginals = new System.Collections.Generic.Dictionary<IntPtr, float>();

        // Cell-size patch. The grid toggle's precision field-write (above) can only reach 0.25 m,
        // because CraftMath.PrecisionToCellSize floors the cell at Clamp(precision,1,8)*0.25. CraftMath
        // has no managed interop stub (docs/TYPE_RESOLUTION.md), so Harmony can't see it — but the build
        // logic runs on the embedded Mono runtime (proven: our AuraMono field-writes change placement).
        // So we resolve the Mono method via AuraMono, take its JIT-compiled native entry
        // (mono_compile_method, NOT the unmanaged thunk — see memory auramono-native-hook-and-settings),
        // and install a MonoMod NativeDetour (Iced-based relocation, not a hand-rolled byte steal).
        //
        // The detour is installed once and left in place: PrecisionToCellSize is a pure function, so the
        // hook fully reimplements it. When buildingFreeCellOverride == 0 it reproduces the original
        // exactly (pass-through); when > 0 it returns (v,v,v), giving a true sub-0.25 m cell.
        //
        // ABI (Windows x64, Mono static valuetype return): a 12-byte Vector3 is returned via a hidden
        // buffer pointer in the first integer slot (RCX), and `float precision` lands in XMM1. Hence the
        // native delegate is (IntPtr retBuf, float precision) -> IntPtr (returns the buffer, as in RAX).
        // The hook touches only the static override + System math — no Unity/Il2Cpp calls (GC/thread-safe).
        private static float buildingFreeCellOverride;
        private bool buildingCellPatchTried;
        private static MonoMod.RuntimeDetour.NativeDetour buildingCellDetour;
        private static PrecisionToCellSizeHookDelegate buildingCellHookDelegate; // keep alive (anti-GC)
        private static BuildingMonoCompileMethodDelegate buildingMonoCompileMethod;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PrecisionToCellSizeHookDelegate(IntPtr retBuf, float precision);

        // mono_compile_method returns the native code pointer; AuraFarm's shared delegate is declared
        // void-return (it only forces JIT), so we declare our own IntPtr-returning variant here.
        private delegate IntPtr BuildingMonoCompileMethodDelegate(IntPtr method);

        // AuraMono send cache. BuildMoveData/BuildTransformData/HomelandProtocolManager are NOT in
        // the managed interop (verified: move=False xf=False proto=False), so build + send via Mono.
        private static readonly string[] BuildOpImageNames =
        {
            "EcsClient", "EcsClient.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll"
        };
        private static readonly string[] HomelandProtoImageNames =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        private IntPtr buildMoveDataClass = IntPtr.Zero;
        private IntPtr buildTransformDataClass = IntPtr.Zero;
        private IntPtr buildProtoClass = IntPtr.Zero;
        private IntPtr buildSendMethod = IntPtr.Zero;        // SendBuildBatchOperation(uint,uint,IBuildData)
        private IntPtr buildMoveTransformDataField = IntPtr.Zero;
        private IntPtr tdLevelObjectNetIdField = IntPtr.Zero;
        private IntPtr tdLocalPosField = IntPtr.Zero;
        private IntPtr tdAngleField = IntPtr.Zero;
        private IntPtr tdExtField = IntPtr.Zero;
        private IntPtr tdCurveField = IntPtr.Zero;
        private IntPtr tdVirtualLinkHasChangeField = IntPtr.Zero;
        private float nextBuildingSendResolveAt = -999f;

        private float DrawBuildingTab(float startY)
        {
            const float left = 40f;
            float y = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, 460f, 24f), "Free Rotate (arbitrary angle)", headerStyle);
            y += 28f;

            GUIStyle descStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            descStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.85f);
            GUI.Label(new Rect(left, y, 470f, 64f),
                "Rotates the focused homeland object to any 1° angle and sends it to the server, " +
                "bypassing the 45/90 snap. Focus an existing object in build mode first (pad move / god click). " +
                "The in-hand preview still re-snaps to 90° (engine), but the saved angle is correct — " +
                "exit build mode / re-enter to see it applied.",
                descStyle);
            y += 66f;

            // Target Y angle field + slider.
            GUI.Label(new Rect(left, y, 90f, 22f), "Y angle (deg)");
            string angleText = GUI.TextField(new Rect(left + 96f, y, 70f, 22f), this.buildingFreeRotateAngleY.ToString());
            if (int.TryParse(angleText, out int parsedAngle))
            {
                this.buildingFreeRotateAngleY = ((parsedAngle % 360) + 360) % 360;
            }
            this.buildingFreeRotateAngleY = Mathf.RoundToInt(
                GUI.HorizontalSlider(new Rect(left + 176f, y + 4f, 300f, 18f), this.buildingFreeRotateAngleY, 0f, 359f));
            y += 30f;

            // Step buttons.
            GUI.Label(new Rect(left, y + 4f, 40f, 22f), "Step");
            string stepText = GUI.TextField(new Rect(left + 44f, y, 50f, 22f), this.buildingFreeRotateStep.ToString());
            if (int.TryParse(stepText, out int parsedStep))
            {
                this.buildingFreeRotateStep = Mathf.Clamp(parsedStep, 1, 180);
            }
            if (GUI.Button(new Rect(left + 104f, y, 60f, 22f), "-step"))
            {
                this.buildingFreeRotateAngleY = ((this.buildingFreeRotateAngleY - this.buildingFreeRotateStep) % 360 + 360) % 360;
            }
            if (GUI.Button(new Rect(left + 168f, y, 60f, 22f), "+step"))
            {
                this.buildingFreeRotateAngleY = (this.buildingFreeRotateAngleY + this.buildingFreeRotateStep) % 360;
            }
            y += 32f;

            if (GUI.Button(new Rect(left, y, 230f, 34f), "Apply angle to focused object",
                this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                bool ok = this.TryBuildingApplyFreeRotate(out string status);
                this.buildingFreeRotateStatus = status;
                this.AddMenuNotification(
                    ok ? ("Rotate sent: Y=" + this.buildingFreeRotateAngleY + "°") : ("Rotate failed: " + status),
                    ok ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.6f, 0.5f));
            }
            if (GUI.Button(new Rect(left + 240f, y, 110f, 34f), "Read current"))
            {
                if (this.TryBuildingReadFocusedAngle(out int currentY, out string status))
                {
                    this.buildingFreeRotateAngleY = ((currentY % 360) + 360) % 360;
                    this.buildingFreeRotateStatus = "Current Y = " + currentY + "°";
                }
                else
                {
                    this.buildingFreeRotateStatus = status;
                }
            }
            y += 42f;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.9f);
            GUI.Label(new Rect(left, y, 470f, 32f), "Status: " + this.buildingFreeRotateStatus, statusStyle);
            y += 38f;

            // --- Free-snap toggles (apply to the focused object's config while on) -------------
            GUIStyle subHeader = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            subHeader.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, 460f, 22f), "Free placement (no snap)", subHeader);
            y += 26f;

            bool prevAngle = this.buildingFreeAngleEnabled;
            this.buildingFreeAngleEnabled = GUI.Toggle(new Rect(left, y, 460f, 22f), this.buildingFreeAngleEnabled,
                "  Free angle (disables 45/90 snap)");
            y += 24f;
            GUI.Label(new Rect(left + 16f, y + 2f, 150f, 20f), "Angle step: " + this.buildingFreeAngleStep + "°", statusStyle);
            this.buildingFreeAngleStep = Mathf.RoundToInt(
                GUI.HorizontalSlider(new Rect(left + 170f, y + 6f, 280f, 18f), this.buildingFreeAngleStep, 1f, 90f));
            y += 26f;

            bool prevGrid = this.buildingFreeGridEnabled;
            this.buildingFreeGridEnabled = GUI.Toggle(new Rect(left, y, 460f, 22f), this.buildingFreeGridEnabled,
                "  Free grid (finest position snap)");
            y += 24f;
            GUI.Label(new Rect(left + 16f, y + 2f, 150f, 20f), "Cell: " + this.buildingFreeGridCell.ToString("0.00") + " m", statusStyle);
            this.buildingFreeGridCell = Mathf.Round(
                GUI.HorizontalSlider(new Rect(left + 170f, y + 6f, 280f, 18f), this.buildingFreeGridCell, 0.01f, 0.25f) * 100f) / 100f;
            y += 26f;

            if (this.buildingFreeAngleEnabled != prevAngle || this.buildingFreeGridEnabled != prevGrid)
            {
                this.AddMenuNotification(
                    "Free build: angle=" + (this.buildingFreeAngleEnabled ? "on" : "off") + " grid=" + (this.buildingFreeGridEnabled ? "on" : "off"),
                    new Color(0.45f, 1f, 0.55f));
            }

            GUI.Label(new Rect(left, y, 470f, 30f),
                "Applies to the object you have focused in build mode (config overridden while on, restored when off). " +
                "The engine always snaps to some step, so these set the finest available.",
                statusStyle);
            y += 34f;

            return y + 20f;
        }

        // --- Plan B: read focused object, pack arbitrary angle, send BuildMoveData ---------------

        private bool TryBuildingApplyFreeRotate(out string status)
        {
            this.BuildingLog("apply: begin targetY=" + this.buildingFreeRotateAngleY);

            if (!this.TryGetBuildingFocusedObject(out IntPtr entityObj, out uint netId, out uint buildRootNetId, out string focusStatus))
            {
                status = focusStatus;
                this.BuildingLog("apply: focus failed: " + status);
                return false;
            }

            if (!this.TryReadBuildingElementLocalTransform(entityObj, out Vector3 localPos, out Vector3 localEuler, out string xfStatus))
            {
                status = xfStatus;
                this.BuildingLog("apply: xform failed: " + status);
                return false;
            }

            // Keep current X/Z (rounded), override Y with the desired arbitrary angle.
            int packedAngle = PackBuildingAngle(
                Mathf.RoundToInt(localEuler.x),
                Mathf.RoundToInt(localEuler.z),
                this.buildingFreeRotateAngleY);
            this.BuildingLog("apply: packedAngle=" + packedAngle + " (x=" + Mathf.RoundToInt(localEuler.x)
                + " z=" + Mathf.RoundToInt(localEuler.z) + " y=" + this.buildingFreeRotateAngleY + ")");

            if (!this.TrySendBuildingMove(netId, buildRootNetId, localPos, packedAngle, out status))
            {
                this.BuildingLog("apply: send failed: " + status);
                return false;
            }

            // Update the local object's rotation immediately so the change is visible without a
            // reload. The server already has the arbitrary angle; this just mirrors it client-side
            // (Entity.localRotation has a setter that drives the renderer hierarchy).
            bool localOk = this.TryBuildingSetLocalRotation(entityObj, new Vector3(localEuler.x, this.buildingFreeRotateAngleY, localEuler.z));

            status = "sent Y=" + this.buildingFreeRotateAngleY + "°" + (localOk ? " (applied)" : " (server only — exit/re-enter to see)");
            this.BuildingLog("apply: sent move netId=" + netId + " root=" + buildRootNetId + " Y=" + this.buildingFreeRotateAngleY + " localVisual=" + localOk);
            return true;
        }

        private bool TryBuildingReadFocusedAngle(out int currentY, out string status)
        {
            currentY = 0;
            if (!this.TryGetBuildingFocusedObject(out IntPtr entityObj, out _, out _, out status))
            {
                return false;
            }

            if (!this.TryReadBuildingElementLocalTransform(entityObj, out _, out Vector3 localEuler, out status))
            {
                return false;
            }

            currentY = Mathf.RoundToInt(localEuler.y);
            status = "ok";
            return true;
        }

        // ToBuildingRotValue: (x<<20)|(z<<10)|y — integer degrees per axis, 10 bits each.
        private static int PackBuildingAngle(int x, int z, int y)
        {
            x = ((x % 360) + 360) % 360;
            z = ((z % 360) + 360) % 360;
            y = ((y % 360) + 360) % 360;
            return (x << 20) | (z << 10) | y;
        }

        // Resolve the focused build object via the (already AuraMono-resolved) BuildModule:
        //   BuildModule.GetCraftBox() -> BuildFocus.buildObject (BuildSingle) -> element + InstanceID;
        //   BuildModule.BuildState.mode.world.id -> buildRootNetId.
        private bool TryGetBuildingFocusedObject(out IntPtr entityObj, out uint netId, out uint buildRootNetId, out string status)
        {
            entityObj = IntPtr.Zero;
            IntPtr elementObj;
            netId = 0u;
            buildRootNetId = 0u;
            status = "build module unavailable";

            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj))
            {
                this.BuildingLog("focus: BuildModule unresolved");
                return false;
            }

            // SubState diagnostic (1=Free, 2=Focus). We don't hard-gate here so move/edit/focus all work.
            int subState = -1;
            this.TryGetPadBuildAuraSubState(moduleObj, out subState);
            this.BuildingLog("focus: subState=" + subState);

            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr craftBoxObj, "GetCraftBox") || craftBoxObj == IntPtr.Zero)
            {
                status = "craftBox unavailable";
                this.BuildingLog("focus: " + status);
                return false;
            }
            this.BuildingLog("focus: craftBox=" + this.BuildingClassName(craftBoxObj));

            IntPtr buildObj;
            if ((!this.TryInvokeAuraMonoZeroArg(craftBoxObj, out buildObj, "get_buildObject") || buildObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(craftBoxObj, "buildObject", out buildObj) || buildObj == IntPtr.Zero))
            {
                status = "buildObject unavailable (nothing focused?)";
                this.BuildingLog("focus: " + status);
                return false;
            }
            this.BuildingLog("focus: buildObject=" + this.BuildingClassName(buildObj));

            // BuildSingle.element (readonly field) or get_Element property.
            if ((!this.TryGetMonoObjectMember(buildObj, "element", out elementObj) || elementObj == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(buildObj, out elementObj, "get_Element", "get_element") || elementObj == IntPtr.Zero))
            {
                status = "element unavailable (not a single build?)";
                this.BuildingLog("focus: " + status);
                return false;
            }
            this.BuildingLog("focus: element=" + this.BuildingClassName(elementObj));

            // BuildComponent is a ViewComponent: base.entity (Entity) carries the real server netId
            // (Entity.GetNetId() = _netId) and the field-local transform (Entity.localPosition/Rotation).
            // Entity.netId (the property) is the small INSTANCE id, and BuildSingle.InstanceID() is a
            // box index (=1) — neither is the server id, so go through the entity.
            if ((!this.TryGetMonoObjectMember(elementObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(elementObj, out entityObj, "get_entity") || entityObj == IntPtr.Zero))
            {
                status = "entity unavailable";
                this.BuildingLog("focus: " + status);
                return false;
            }
            this.BuildingLog("focus: entity=" + this.BuildingClassName(entityObj));

            if (!this.TryReadBuildingNetId(entityObj, out netId) || netId == 0u)
            {
                status = "netId unavailable";
                this.BuildingLog("focus: netId unavailable (entity=" + this.BuildingClassName(entityObj) + ")");
                return false;
            }
            this.BuildingLog("focus: netId=" + netId + (this.IsBuildingLocalNetId(netId) ? " (LOCAL)" : ""));

            if (this.IsBuildingLocalNetId(netId))
            {
                status = "new local object — confirm it first, then rotate";
                return false;
            }

            if (!this.TryReadBuildingRootNetId(moduleObj, elementObj, out buildRootNetId) || buildRootNetId == 0u)
            {
                status = "build root netId unavailable";
                this.BuildingLog("focus: " + status);
                return false;
            }
            this.BuildingLog("focus: buildRootNetId=" + buildRootNetId);

            status = "ok";
            return true;
        }

        private void BuildingLog(string message)
        {
            this.PadBuildHotkeyLog("[Building] " + message);
        }

        private string BuildingClassName(IntPtr obj)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return "<null>";
            }
            string n = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(obj));
            return string.IsNullOrEmpty(n) ? "<?>" : n;
        }

        private bool TryGetPadBuildAuraSubState(IntPtr moduleObj, out int subState)
        {
            subState = -1;
            if (moduleObj == IntPtr.Zero || this.padBuildAuraGetSubStateMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(this.padBuildAuraGetSubStateMethod, moduleObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return false;
                }
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }
                unsafe { subState = *(byte*)raw; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadBuildingNetId(IntPtr entityObj, out uint netId)
        {
            netId = 0u;

            // Entity.GetNetId() -> NetId (_netId, the server id). Preferred.
            if (this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr boxed, "GetNetId") && boxed != IntPtr.Zero
                && this.TryUnboxBuildingNetId(boxed, out netId) && netId != 0u)
            {
                this.BuildingLog("netId via entity.GetNetId()=" + netId);
                return true;
            }

            // Fallbacks (Entity.netId property is the instance id — last resort only).
            foreach (string m in new[] { "get_netId", "get_NetId" })
            {
                if (this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr b2, m) && b2 != IntPtr.Zero
                    && this.TryUnboxBuildingNetId(b2, out netId) && netId != 0u)
                {
                    this.BuildingLog("netId via entity." + m + "()=" + netId);
                    return true;
                }
            }

            netId = 0u;
            return false;
        }

        private unsafe bool TryUnboxBuildingNetId(IntPtr boxed, out uint value)
        {
            value = 0u;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(uint*)raw; // NetId wraps a single uint `value` field at offset 0
            return true;
        }

        private bool IsBuildingLocalNetId(uint netId)
        {
            // NetId.Min = 4_000_000_000 — client-assigned (local/preview) ids start here; real
            // server-authoritative ids are below it.
            return netId >= 4000000000u;
        }

        private bool TryReadBuildingRootNetId(IntPtr moduleObj, IntPtr elementObj, out uint rootNetId)
        {
            rootNetId = 0u;

            // Primary (proven in Pad mode): BuildModule.BuildState -> mode (BuildSystemBaseMode) -> world -> id.
            if (this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr buildStateObj, "get_BuildState") && buildStateObj != IntPtr.Zero
                && this.TryInvokeAuraMonoZeroArg(buildStateObj, out IntPtr modeObj, "get_mode") && modeObj != IntPtr.Zero)
            {
                IntPtr worldObj;
                if (((this.TryInvokeAuraMonoZeroArg(modeObj, out worldObj, "get_world") && worldObj != IntPtr.Zero)
                        || (this.TryGetMonoObjectMember(modeObj, "world", out worldObj) && worldObj != IntPtr.Zero))
                    && (this.TryGetMonoUInt32Member(worldObj, "id", out rootNetId)
                        || this.TryGetMonoUInt32Member(worldObj, "Id", out rootNetId)
                        || this.TryGetMonoUInt32Member(worldObj, "netId", out rootNetId))
                    && rootNetId != 0u)
                {
                    this.BuildingLog("root via BuildState.mode.world.id=" + rootNetId);
                    return true;
                }
            }

            // Fallback (works in God mode, where BuildState is inactive): BuildComponent.HomeNetId /
            // GetFieldId (= _furnitureBaseData.homeNetId, the homeland field/home root).
            foreach (string m in new[] { "get_HomeNetId", "get_GetFieldId", "get_FieldId", "get_HomeId" })
            {
                if (this.TryInvokeAuraMonoZeroArg(elementObj, out IntPtr boxed, m) && boxed != IntPtr.Zero
                    && this.TryUnboxBuildingNetId(boxed, out rootNetId) && rootNetId != 0u)
                {
                    this.BuildingLog("root via element." + m + "()=" + rootNetId);
                    return true;
                }
            }

            return false;
        }

        private bool TryReadBuildingElementLocalTransform(IntPtr entityObj, out Vector3 localPos, out Vector3 localEuler, out string status)
        {
            localPos = Vector3.zero;
            localEuler = Vector3.zero;
            status = "transform unavailable";

            // Entity.localPosition / Entity.localRotation are field-local (BuildComponent.cs:586).
            if (!this.TryReadBuildingVector3Prop(entityObj, "localPosition", out localPos))
            {
                status = "entity.localPosition unavailable";
                return false;
            }

            if (!this.TryReadBuildingQuaternionProp(entityObj, "localRotation", out Quaternion localRot))
            {
                status = "entity.localRotation unavailable";
                return false;
            }

            localEuler = localRot.eulerAngles;
            this.BuildingLog("xform: localPos=" + localPos + " localEuler=" + localEuler);
            status = "ok";
            return true;
        }

        private unsafe bool TryReadBuildingVector3Prop(IntPtr obj, string propName, out Vector3 value)
        {
            value = Vector3.zero;
            if (!this.TryInvokeAuraMonoZeroArg(obj, out IntPtr boxed, "get_" + propName) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(Vector3*)raw;
            return true;
        }

        // Immediately mirror the sent rotation on the local object: Entity.set_localRotation(Quaternion)
        // drives transformComponent.hierarchy → the renderer. Quaternion is a value-type arg, so pass
        // a pointer to it. Returns false if the setter isn't found (then visual waits for reload).
        private unsafe bool TryBuildingSetLocalRotation(IntPtr entityObj, Vector3 euler)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr entityClass = auraMonoObjectGetClass(entityObj);
                IntPtr setMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "set_localRotation", 1);
                if (setMethod == IntPtr.Zero)
                {
                    this.BuildingLog("local: set_localRotation not found");
                    return false;
                }

                Quaternion q = Quaternion.Euler(euler);
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&q);
                auraMonoRuntimeInvoke(setMethod, entityObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.BuildingLog("local: set_localRotation exc");
                    return false;
                }

                this.BuildingLog("local: set_localRotation euler=" + euler);
                return true;
            }
            catch (Exception ex)
            {
                this.BuildingLog("local: set_localRotation exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryReadBuildingQuaternionProp(IntPtr obj, string propName, out Quaternion value)
        {
            value = Quaternion.identity;
            if (!this.TryInvokeAuraMonoZeroArg(obj, out IntPtr boxed, "get_" + propName) || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            value = *(Quaternion*)raw;
            return true;
        }

        // --- AuraMono send: HomelandProtocolManager.SendBuildBatchOperation(netId, root, BuildMoveData) ---

        private bool TryResolveBuildingSendApiAura(out string status)
        {
            status = string.Empty;
            if (this.buildSendMethod != IntPtr.Zero && this.buildMoveDataClass != IntPtr.Zero
                && this.buildTransformDataClass != IntPtr.Zero && this.tdAngleField != IntPtr.Zero)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.nextBuildingSendResolveAt)
            {
                status = "send api resolve throttled";
                this.BuildingLog("send: " + status);
                return false;
            }
            this.nextBuildingSendResolveAt = now + 5f;

            if (auraMonoObjectNew == null || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null || auraMonoRuntimeInvoke == null)
            {
                status = "mono api unavailable for send";
                this.BuildingLog("send: " + status);
                return false;
            }

            this.buildMoveDataClass = this.FindAuraMonoClassInImages("XDT.Scene.Shared.Modules.Build", "BuildMoveData", BuildOpImageNames);
            this.buildTransformDataClass = this.FindAuraMonoClassInImages("XDT.Scene.Shared.Modules.Build", "BuildTransformData", BuildOpImageNames);
            this.buildProtoClass = this.FindAuraMonoClassInImages("XDTDataAndProtocol.ProtocolService.Homeland", "HomelandProtocolManager", HomelandProtoImageNames);

            if (this.buildMoveDataClass == IntPtr.Zero || this.buildTransformDataClass == IntPtr.Zero || this.buildProtoClass == IntPtr.Zero)
            {
                status = "send classes unavailable move=" + (this.buildMoveDataClass != IntPtr.Zero)
                    + " xf=" + (this.buildTransformDataClass != IntPtr.Zero) + " proto=" + (this.buildProtoClass != IntPtr.Zero);
                this.BuildingLog("send: " + status);
                return false;
            }

            this.buildSendMethod = this.FindAuraMonoMethodOnHierarchy(this.buildProtoClass, "SendBuildBatchOperation", 3);
            this.buildMoveTransformDataField = auraMonoClassGetFieldFromName(this.buildMoveDataClass, "TransformData");
            this.tdLevelObjectNetIdField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "LevelObjectNetId");
            this.tdLocalPosField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "LocalPos");
            this.tdAngleField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "Angle");
            this.tdExtField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "Ext");
            this.tdCurveField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "Curve");
            this.tdVirtualLinkHasChangeField = auraMonoClassGetFieldFromName(this.buildTransformDataClass, "VirtualLinkHasChange");

            if (this.buildSendMethod == IntPtr.Zero || this.buildMoveTransformDataField == IntPtr.Zero
                || this.tdLevelObjectNetIdField == IntPtr.Zero || this.tdLocalPosField == IntPtr.Zero || this.tdAngleField == IntPtr.Zero)
            {
                status = "send members unavailable send=" + (this.buildSendMethod != IntPtr.Zero)
                    + " td=" + (this.buildMoveTransformDataField != IntPtr.Zero)
                    + " levelObj=" + (this.tdLevelObjectNetIdField != IntPtr.Zero)
                    + " pos=" + (this.tdLocalPosField != IntPtr.Zero) + " angle=" + (this.tdAngleField != IntPtr.Zero);
                this.BuildingLog("send: " + status);
                return false;
            }

            this.BuildingLog("send: api resolved (SendBuildBatchOperation + fields ok)");
            status = "ok";
            return true;
        }

        private unsafe bool TrySendBuildingMove(uint netId, uint buildRootNetId, Vector3 localPos, int packedAngle, out string status)
        {
            if (!this.TryResolveBuildingSendApiAura(out status))
            {
                return false;
            }

            try
            {
                // Build BuildTransformData (boxed struct), fill fields via mono_field_set_value.
                IntPtr transformObj = auraMonoObjectNew(this.auraMonoRootDomain, this.buildTransformDataClass);
                if (transformObj == IntPtr.Zero)
                {
                    status = "transform alloc failed";
                    this.BuildingLog("send: " + status);
                    return false;
                }

                ulong levelObj = buildRootNetId;
                ulong ext = 0UL;
                byte curve = 0;
                byte virtualChange = 0;
                Vector3 pos = localPos;
                int angle = packedAngle;

                auraMonoFieldSetValue(transformObj, this.tdLevelObjectNetIdField, (IntPtr)(&levelObj));
                auraMonoFieldSetValue(transformObj, this.tdLocalPosField, (IntPtr)(&pos));
                auraMonoFieldSetValue(transformObj, this.tdAngleField, (IntPtr)(&angle));
                if (this.tdExtField != IntPtr.Zero) auraMonoFieldSetValue(transformObj, this.tdExtField, (IntPtr)(&ext));
                if (this.tdCurveField != IntPtr.Zero) auraMonoFieldSetValue(transformObj, this.tdCurveField, (IntPtr)(&curve));
                if (this.tdVirtualLinkHasChangeField != IntPtr.Zero) auraMonoFieldSetValue(transformObj, this.tdVirtualLinkHasChangeField, (IntPtr)(&virtualChange));

                // Build BuildMoveData and copy the (unboxed) transform struct into its TransformData field.
                IntPtr moveObj = auraMonoObjectNew(this.auraMonoRootDomain, this.buildMoveDataClass);
                if (moveObj == IntPtr.Zero)
                {
                    status = "move alloc failed";
                    this.BuildingLog("send: " + status);
                    return false;
                }
                IntPtr transformRaw = auraMonoObjectUnbox(transformObj);
                if (transformRaw == IntPtr.Zero)
                {
                    status = "transform unbox failed";
                    this.BuildingLog("send: " + status);
                    return false;
                }
                auraMonoFieldSetValue(moveObj, this.buildMoveTransformDataField, transformRaw);

                // Invoke SendBuildBatchOperation(uint netId, uint buildRootNetId, IBuildData data).
                IntPtr exc = IntPtr.Zero;
                uint netIdArg = netId;
                uint rootArg = buildRootNetId;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&rootArg);
                args[2] = moveObj; // reference (interface) param: the boxed BuildMoveData object itself
                auraMonoRuntimeInvoke(this.buildSendMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "send invoke exc";
                    this.BuildingLog("send: " + status);
                    return false;
                }

                status = "ok";
                return true;
            }
            catch (Exception ex)
            {
                status = "send exc: " + (ex.InnerException ?? ex).Message;
                this.BuildingLog("send: " + status);
                return false;
            }
        }

        // --- Free-snap toggles: override focused object's angle/grid config -----------------------

        private void UpdateBuildingFreeSnapOverrides()
        {
            // Drive the static cell-size override every frame (the Harmony prefix reads it). When the
            // grid toggle is on, install the patch lazily and force the slider's cell; off ⇒ 0 (pass).
            if (this.buildingFreeGridEnabled)
            {
                this.EnsureBuildingCellPatch();
                buildingFreeCellOverride = Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f);
            }
            else
            {
                buildingFreeCellOverride = 0f;
            }

            bool anyOn = this.buildingFreeAngleEnabled || this.buildingFreeGridEnabled;
            bool anyCached = this.buildingAngleOriginals.Count > 0 || this.buildingGridOriginals.Count > 0;
            if (!anyOn && !anyCached)
            {
                return;
            }

            // Restore-on-toggle-off (edge): when a toggle goes on→off, put the originals back.
            if (this.buildingFreeAnglePrev && !this.buildingFreeAngleEnabled)
            {
                this.RestoreBuildingAngleOriginals();
            }
            if (this.buildingFreeGridPrev && !this.buildingFreeGridEnabled)
            {
                this.RestoreBuildingGridOriginals();
            }
            this.buildingFreeAnglePrev = this.buildingFreeAngleEnabled;
            this.buildingFreeGridPrev = this.buildingFreeGridEnabled;

            if (!this.buildingFreeAngleEnabled && !this.buildingFreeGridEnabled)
            {
                return;
            }

            // Throttle the focus resolve + apply (cheap, runs every frame otherwise).
            float now = Time.unscaledTime;
            if (now < this.buildingFreeSnapNextApplyAt)
            {
                return;
            }
            this.buildingFreeSnapNextApplyAt = now + 0.2f;

            if (!this.TryGetBuildingFocusedElementQuiet(out IntPtr elementObj) || elementObj == IntPtr.Zero)
            {
                return;
            }

            if (this.buildingFreeAngleEnabled)
            {
                this.TryApplyBuildingFreeAngle(elementObj);
            }
            if (this.buildingFreeGridEnabled)
            {
                this.TryApplyBuildingFreeGrid(elementObj);
            }
        }

        // BuildComponent._buildBoxData.putDatas[0] (BuildBoxData, a class) -> set rotateAngle.
        private unsafe void TryApplyBuildingFreeAngle(IntPtr elementObj)
        {
            try
            {
                if (!this.TryGetMonoObjectMember(elementObj, "_buildBoxData", out IntPtr scriptDataObj) || scriptDataObj == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(scriptDataObj, "putDatas", out IntPtr arrObj) || arrObj == IntPtr.Zero
                    || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
                {
                    return;
                }
                if (auraMonoArrayLength(arrObj).ToUInt64() == 0UL)
                {
                    return;
                }
                IntPtr slot = auraMonoArrayAddrWithSize(arrObj, IntPtr.Size, UIntPtr.Zero);
                IntPtr boxDataObj = slot != IntPtr.Zero ? Marshal.ReadIntPtr(slot) : IntPtr.Zero;
                if (boxDataObj == IntPtr.Zero)
                {
                    return;
                }

                int step = Mathf.Clamp(this.buildingFreeAngleStep, 1, 90);
                if (!this.buildingAngleOriginals.ContainsKey(boxDataObj))
                {
                    if (this.TryGetMonoInt32Member(boxDataObj, "rotateAngle", out int orig))
                    {
                        this.buildingAngleOriginals[boxDataObj] = orig;
                        this.BuildingLog("free-angle: override rotateAngle " + orig + "->" + step);
                    }
                }
                this.TrySetBuildingIntField(boxDataObj, "rotateAngle", step);
            }
            catch (Exception ex)
            {
                this.BuildingLog("free-angle exc: " + ex.Message);
            }
        }

        // BuildComponent._putitem (TablePutitem) -> set precision (cell size source).
        private void TryApplyBuildingFreeGrid(IntPtr elementObj)
        {
            try
            {
                if (!this.TryGetMonoObjectMember(elementObj, "_putitem", out IntPtr putitemObj) || putitemObj == IntPtr.Zero)
                {
                    return;
                }
                // Fallback when the CraftMath.PrecisionToCellSize patch isn't active: cell =
                // ToCellSize(precision) = Clamp(precision,1,8)*0.25 → precision = cell/0.25. The engine
                // floors cell at 0.25 m here; true sub-0.25 m comes from BuildingPrecisionToCellSizePrefix.
                float precision = Mathf.Clamp(this.buildingFreeGridCell, 0.01f, 0.25f) / 0.25f;
                if (!this.buildingGridOriginals.ContainsKey(putitemObj))
                {
                    if (this.TryGetMonoSingleMember(putitemObj, "precision", out float orig))
                    {
                        this.buildingGridOriginals[putitemObj] = orig;
                        this.BuildingLog("free-grid: override precision " + orig + "->" + precision + " (cell~" + this.buildingFreeGridCell + ")");
                    }
                }
                this.TrySetBuildingFloatField(putitemObj, "precision", precision);
            }
            catch (Exception ex)
            {
                this.BuildingLog("free-grid exc: " + ex.Message);
            }
        }

        private void RestoreBuildingAngleOriginals()
        {
            foreach (System.Collections.Generic.KeyValuePair<IntPtr, int> kv in this.buildingAngleOriginals)
            {
                this.TrySetBuildingIntField(kv.Key, "rotateAngle", kv.Value);
            }
            this.BuildingLog("free-angle: restored " + this.buildingAngleOriginals.Count + " original(s)");
            this.buildingAngleOriginals.Clear();
        }

        private void RestoreBuildingGridOriginals()
        {
            foreach (System.Collections.Generic.KeyValuePair<IntPtr, float> kv in this.buildingGridOriginals)
            {
                this.TrySetBuildingFloatField(kv.Key, "precision", kv.Value);
            }
            this.BuildingLog("free-grid: restored " + this.buildingGridOriginals.Count + " original(s)");
            this.buildingGridOriginals.Clear();
        }

        private unsafe void TrySetBuildingIntField(IntPtr obj, string fieldName, int value)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldSetValue == null)
            {
                return;
            }
            IntPtr field = auraMonoClassGetFieldFromName(auraMonoObjectGetClass(obj), fieldName);
            if (field != IntPtr.Zero)
            {
                int v = value;
                auraMonoFieldSetValue(obj, field, (IntPtr)(&v));
            }
        }

        private unsafe void TrySetBuildingFloatField(IntPtr obj, string fieldName, float value)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldSetValue == null)
            {
                return;
            }
            IntPtr field = auraMonoClassGetFieldFromName(auraMonoObjectGetClass(obj), fieldName);
            if (field != IntPtr.Zero)
            {
                float v = value;
                auraMonoFieldSetValue(obj, field, (IntPtr)(&v));
            }
        }

        // Lightweight, non-logging resolve of the focused BuildComponent (element).
        private bool TryGetBuildingFocusedElementQuiet(out IntPtr elementObj)
        {
            elementObj = IntPtr.Zero;
            if (!this.TryGetPadBuildAuraModule(out IntPtr moduleObj))
            {
                return false;
            }
            if (!this.TryInvokeAuraMonoZeroArg(moduleObj, out IntPtr craftBoxObj, "GetCraftBox") || craftBoxObj == IntPtr.Zero)
            {
                return false;
            }
            IntPtr buildObj;
            if ((!this.TryInvokeAuraMonoZeroArg(craftBoxObj, out buildObj, "get_buildObject") || buildObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(craftBoxObj, "buildObject", out buildObj) || buildObj == IntPtr.Zero))
            {
                return false;
            }
            if ((!this.TryGetMonoObjectMember(buildObj, "element", out elementObj) || elementObj == IntPtr.Zero)
                && (!this.TryInvokeAuraMonoZeroArg(buildObj, out elementObj, "get_Element", "get_element") || elementObj == IntPtr.Zero))
            {
                elementObj = IntPtr.Zero;
                return false;
            }
            return true;
        }

        // Resolve CraftMath.PrecisionToCellSize(float) on the embedded Mono runtime and install a
        // MonoMod NativeDetour onto its JIT-compiled entry. Lazy; retried until AuraMono is ready and
        // the class/method resolve (transient misses do not burn buildingCellPatchTried).
        private void EnsureBuildingCellPatch()
        {
            if (this.buildingCellPatchTried)
            {
                return;
            }
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet (e.g. not in-world) — retry on a later frame.
                }

                // CraftMath is compiled into the XDTDataAndProtocol image (namespace ≠ assembly).
                IntPtr craftMath = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Core.Craft", "CraftMath",
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (craftMath == IntPtr.Zero)
                {
                    craftMath = this.FindAuraMonoClassInAllLoadedImages("CraftMath", "XDTLevelAndEntity.Core.Craft");
                }
                if (craftMath == IntPtr.Zero)
                {
                    return; // image may not be loaded yet — retry later.
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(craftMath, "PrecisionToCellSize", 1);
                if (method == IntPtr.Zero)
                {
                    this.buildingCellPatchTried = true; // class found but no such method — permanent.
                    this.BuildingLog("cell-patch: PrecisionToCellSize(1 arg) not found on CraftMath");
                    return;
                }

                // mono_compile_method → native code entry. Resolve our IntPtr-returning delegate once.
                if (buildingMonoCompileMethod == null)
                {
                    IntPtr monoModule = this.GetAuraMonoModuleHandle();
                    if (monoModule != IntPtr.Zero)
                    {
                        buildingMonoCompileMethod = this.GetAuraMonoExport<BuildingMonoCompileMethodDelegate>(monoModule, "mono_compile_method");
                    }
                }
                if (buildingMonoCompileMethod == null)
                {
                    this.buildingCellPatchTried = true;
                    this.BuildingLog("cell-patch: mono_compile_method export unavailable");
                    return;
                }

                IntPtr nativePtr = buildingMonoCompileMethod(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.buildingCellPatchTried = true;
                    this.BuildingLog("cell-patch: mono_compile_method returned null");
                    return;
                }

                buildingCellHookDelegate = BuildingPrecisionToCellSizeNative; // anti-GC: keep alive
                buildingCellDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, buildingCellHookDelegate);
                this.buildingCellPatchTried = true;
                this.BuildingLog("cell-patch: NativeDetour installed on CraftMath.PrecisionToCellSize @ 0x" + nativePtr.ToString("X") + " (sub-0.25 m grid enabled)");
            }
            catch (Exception ex)
            {
                this.buildingCellPatchTried = true; // don't loop on a hard failure (e.g. detour throw)
                this.BuildingLog("cell-patch failed: " + ex.Message);
            }
        }

        // Native detour body for Mono CraftMath.PrecisionToCellSize(float) -> Vector3.
        // Windows x64 sret ABI: retBuf is the hidden return-buffer pointer, precision is the arg.
        // When buildingFreeCellOverride > 0 we force (v,v,v); otherwise we reproduce the original
        // formula EXACTLY so the game behaves identically while the detour stays installed.
        // No Unity/Il2Cpp calls here — only the static field + System math (GC/thread-safe).
        private static unsafe IntPtr BuildingPrecisionToCellSizeNative(IntPtr retBuf, float precision)
        {
            try
            {
                if (retBuf == IntPtr.Zero)
                {
                    return retBuf;
                }
                float x, y, z;
                float v = buildingFreeCellOverride;
                if (v > 0f)
                {
                    x = y = z = v;
                }
                else if (precision > 100f)
                {
                    int num = (int)Math.Round((double)precision, MidpointRounding.ToEven);
                    x = BuildingToCellSize(num / 100);
                    y = BuildingToCellSize(num % 100 / 10);
                    z = BuildingToCellSize(num % 10);
                }
                else
                {
                    x = y = z = BuildingToCellSize(precision);
                }
                float* p = (float*)retBuf;
                p[0] = x;
                p[1] = y;
                p[2] = z;
            }
            catch
            {
                // Never let a native callback throw across the Mono boundary.
            }
            return retBuf;
        }

        // Mirror of CraftMath's local ToCellSize: (int)Clamp(value,1,8) * 0.25f. System math only.
        private static float BuildingToCellSize(float value)
        {
            float c = value < 1f ? 1f : (value > 8f ? 8f : value);
            return (int)c * 0.25f;
        }
    }
}
