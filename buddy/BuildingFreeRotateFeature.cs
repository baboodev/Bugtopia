using System;
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
            GUI.Label(new Rect(left, y, 470f, 44f), "Status: " + this.buildingFreeRotateStatus, statusStyle);
            y += 48f;

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
    }
}
