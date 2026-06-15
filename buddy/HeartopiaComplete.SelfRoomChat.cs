﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void StrangerChatLog(string message)
        {
            if (!MasterLogStrangerChat || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[StrangerChat] " + message);
        }

        private void EnsureStrangerChatBypassPatch()
        {
            if (!this.strangerChatBypassEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.nextStrangerChatBypassPatchAttemptAt)
            {
                return;
            }

            this.nextStrangerChatBypassPatchAttemptAt = now + (this.strangerChatBypassPatchApplied ? 3f : 5f);
            this.TryApplyAuraMonoStrangerChatBypass();
        }

        private void TryApplyAuraMonoStrangerChatBypass()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    this.StrangerChatLog("AuraMono runtime not ready yet.");
                    return;
                }

                bool hasState = this.TryGetAuraMonoSelfRoomChatVisibility(out bool inSelfRoom, out bool onlyFriendChat, out string stateStatus);
                if (hasState && inSelfRoom && !onlyFriendChat)
                {
                    if (!this.strangerChatBypassPatchApplied)
                    {
                        this.StrangerChatLog("Stranger chat already visible. " + stateStatus);
                    }
                    this.strangerChatBypassPatchApplied = true;
                    this.strangerChatBypassPatchUnavailableLogged = false;
                    return;
                }

                if (this.TryForceAuraMonoStrangerChatDisplayGate(out string gateStatus))
                {
                    bool wasApplied = this.strangerChatBypassPatchApplied;
                    this.strangerChatBypassPatchApplied = true;
                    this.strangerChatBypassPatchUnavailableLogged = false;
                    if (!wasApplied)
                    {
                        this.StrangerChatLog("Stranger Chat Bypass active via AuraMono local gate. " + gateStatus);
                    }
                    return;
                }

                if (inSelfRoom
                    && (this.TryInvokeAuraMonoSelfRoomUpdateChatVisibility(false, out string invokeStatus)
                        || this.TryInvokeAuraMonoSelfRoomProtocolChatVisibility(false, out invokeStatus)))
                {
                    this.strangerChatBypassPatchApplied = true;
                    this.strangerChatBypassPatchUnavailableLogged = false;
                    this.StrangerChatLog("Stranger Chat Bypass active via AuraMono self-room setting. " + invokeStatus);
                    return;
                }

                if (!this.strangerChatBypassPatchUnavailableLogged)
                {
                    this.StrangerChatLog("AuraMono chat display gate failed. state=" + stateStatus + " gate=" + gateStatus);
                    this.strangerChatBypassPatchUnavailableLogged = true;
                }
            }
            catch (Exception ex)
            {
                this.StrangerChatLog("Exception: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private unsafe bool TryForceAuraMonoStrangerChatDisplayGate(out string status)
        {
            status = "SelfRoomSystem display gate unavailable.";

            try
            {
                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoObjectGetClass == null
                    || auraMonoFieldSetValue == null)
                {
                    status = "AuraMono field API not ready.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.SelfRoom.SelfRoomSystem", out IntPtr selfRoomSystemObj) || selfRoomSystemObj == IntPtr.Zero)
                {
                    status = "SelfRoomSystem module unavailable.";
                    return false;
                }

                IntPtr selfRoomSystemClass = auraMonoObjectGetClass(selfRoomSystemObj);
                if (selfRoomSystemClass == IntPtr.Zero)
                {
                    status = "SelfRoomSystem class unavailable.";
                    return false;
                }

                if (!this.TryResolveAuraMonoStrangerChatInSelfRoomField(selfRoomSystemClass))
                {
                    status = "SelfRoomSystem IsInSelfRoom field unavailable.";
                    return false;
                }

                if (!this.strangerChatOriginalInSelfRoomValid
                    && this.TryGetAuraMonoSelfRoomChatVisibility(out bool originalInSelfRoom, out _, out _))
                {
                    this.strangerChatOriginalInSelfRoom = originalInSelfRoom;
                    this.strangerChatOriginalInSelfRoomValid = true;
                }

                if (!this.TrySetAuraMonoStrangerChatInSelfRoom(selfRoomSystemObj, selfRoomSystemClass, true, out status))
                {
                    return false;
                }

                if (this.TryGetAuraMonoSelfRoomChatVisibility(out bool verifyInSelfRoom, out bool verifyOnlyFriendChat, out string verifyStatus))
                {
                    status = "forced IsInSelfRoom=True; verify=" + verifyStatus;
                    if (verifyInSelfRoom && !verifyOnlyFriendChat)
                    {
                        return true;
                    }

                    if (verifyInSelfRoom && verifyOnlyFriendChat && this.TryInvokeAuraMonoSelfRoomUpdateChatVisibility(false, out string updateStatus))
                    {
                        if (this.TryGetAuraMonoSelfRoomChatVisibility(out verifyInSelfRoom, out verifyOnlyFriendChat, out verifyStatus))
                        {
                            status = "forced IsInSelfRoom=True; " + updateStatus + " verify=" + verifyStatus;
                            return verifyInSelfRoom && !verifyOnlyFriendChat;
                        }

                        status = "forced IsInSelfRoom=True; " + updateStatus + " verify unavailable.";
                        return true;
                    }

                    return false;
                }

                status = "forced IsInSelfRoom=True; verify unavailable.";
                return true;
            }
            catch (Exception ex)
            {
                status = "SelfRoomSystem display gate exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryRestoreAuraMonoStrangerChatDisplayGate(out string status)
        {
            status = "No stored SelfRoomSystem state to restore.";

            try
            {
                if (!this.strangerChatOriginalInSelfRoomValid)
                {
                    return false;
                }

                if (!this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoObjectGetClass == null
                    || auraMonoFieldSetValue == null)
                {
                    status = "AuraMono field API not ready.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.SelfRoom.SelfRoomSystem", out IntPtr selfRoomSystemObj) || selfRoomSystemObj == IntPtr.Zero)
                {
                    status = "SelfRoomSystem module unavailable.";
                    return false;
                }

                IntPtr selfRoomSystemClass = auraMonoObjectGetClass(selfRoomSystemObj);
                if (!this.TrySetAuraMonoStrangerChatInSelfRoom(selfRoomSystemObj, selfRoomSystemClass, this.strangerChatOriginalInSelfRoom, out status))
                {
                    return false;
                }

                bool restoredValue = this.strangerChatOriginalInSelfRoom;
                this.strangerChatOriginalInSelfRoom = false;
                this.strangerChatOriginalInSelfRoomValid = false;

                if (this.TryGetAuraMonoSelfRoomChatVisibility(out _, out _, out string verifyStatus))
                {
                    status = "restored IsInSelfRoom=" + restoredValue + "; verify=" + verifyStatus;
                }
                else
                {
                    status = "restored IsInSelfRoom=" + restoredValue + "; verify unavailable.";
                }

                return true;
            }
            catch (Exception ex)
            {
                status = "SelfRoomSystem restore exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TrySetAuraMonoStrangerChatInSelfRoom(IntPtr selfRoomSystemObj, IntPtr selfRoomSystemClass, bool value, out string status)
        {
            status = "SelfRoomSystem IsInSelfRoom field unavailable.";
            if (selfRoomSystemObj == IntPtr.Zero || selfRoomSystemClass == IntPtr.Zero || auraMonoFieldSetValue == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoStrangerChatInSelfRoomField(selfRoomSystemClass))
            {
                return false;
            }

            bool inRoomValue = value;
            auraMonoFieldSetValue(selfRoomSystemObj, this.cachedStrangerChatSelfRoomInRoomFieldPtr, (IntPtr)(&inRoomValue));
            status = "set IsInSelfRoom=" + value;
            return true;
        }

        private bool TryResolveAuraMonoStrangerChatInSelfRoomField(IntPtr selfRoomSystemClass)
        {
            if (selfRoomSystemClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.cachedStrangerChatSelfRoomInRoomFieldPtr == IntPtr.Zero)
            {
                this.cachedStrangerChatSelfRoomInRoomFieldPtr = this.FindAuraMonoFieldOnHierarchy(selfRoomSystemClass, "<IsInSelfRoom>k__BackingField");
                if (this.cachedStrangerChatSelfRoomInRoomFieldPtr == IntPtr.Zero)
                {
                    this.cachedStrangerChatSelfRoomInRoomFieldPtr = this.FindAuraMonoFieldOnHierarchy(selfRoomSystemClass, "IsInSelfRoom");
                }
                if (this.cachedStrangerChatSelfRoomInRoomFieldPtr == IntPtr.Zero)
                {
                    this.cachedStrangerChatSelfRoomInRoomFieldPtr = this.FindAuraMonoFieldOnHierarchy(selfRoomSystemClass, "isInSelfRoom");
                }
                if (this.cachedStrangerChatSelfRoomInRoomFieldPtr == IntPtr.Zero)
                {
                    this.cachedStrangerChatSelfRoomInRoomFieldPtr = this.FindAuraMonoFieldOnHierarchy(selfRoomSystemClass, "_isInSelfRoom");
                }
            }

            return this.cachedStrangerChatSelfRoomInRoomFieldPtr != IntPtr.Zero;
        }

        private bool TryGetAuraMonoSelfRoomChatVisibility(out bool inSelfRoom, out bool onlyFriendChat, out string status)
        {
            inSelfRoom = false;
            onlyFriendChat = true;
            status = "SelfRoomSystem unavailable.";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.SelfRoom.SelfRoomSystem", out IntPtr selfRoomSystemObj) || selfRoomSystemObj == IntPtr.Zero)
                {
                    status = "SelfRoomSystem module unavailable.";
                    return false;
                }

                IntPtr selfRoomSystemClass = auraMonoObjectGetClass(selfRoomSystemObj);
                if (selfRoomSystemClass == IntPtr.Zero)
                {
                    status = "SelfRoomSystem class unavailable.";
                    return false;
                }

                if (!this.TryInvokeAuraMonoBoolGetter(selfRoomSystemObj, selfRoomSystemClass, out inSelfRoom, "get_IsInSelfRoom", "CheckIfInSelfRoom"))
                {
                    status = "SelfRoomSystem IsInSelfRoom unavailable.";
                    return false;
                }

                if (!this.TryInvokeAuraMonoBoolGetter(selfRoomSystemObj, selfRoomSystemClass, out onlyFriendChat, "IsOnlyFriendChatVisibility"))
                {
                    status = "SelfRoomSystem IsOnlyFriendChatVisibility unavailable. inSelfRoom=" + inSelfRoom;
                    return false;
                }

                status = "SelfRoomSystem inSelfRoom=" + inSelfRoom + " onlyFriendChat=" + onlyFriendChat;
                return true;
            }
            catch (Exception ex)
            {
                status = "SelfRoomSystem visibility exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoSelfRoomUpdateChatVisibility(bool onlyFriend, out string status)
        {
            status = "SelfRoomSystem.UpdateChatVisibility unavailable.";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.SelfRoom.SelfRoomSystem", out IntPtr selfRoomSystemObj) || selfRoomSystemObj == IntPtr.Zero)
                {
                    status = "SelfRoomSystem module unavailable.";
                    return false;
                }

                IntPtr selfRoomSystemClass = auraMonoObjectGetClass(selfRoomSystemObj);
                if (selfRoomSystemClass == IntPtr.Zero)
                {
                    status = "SelfRoomSystem class unavailable.";
                    return false;
                }

                if (this.cachedStrangerChatSelfRoomUpdateMethodPtr == IntPtr.Zero)
                {
                    this.cachedStrangerChatSelfRoomUpdateMethodPtr = this.FindAuraMonoMethodOnHierarchy(selfRoomSystemClass, "UpdateChatVisibility", 1);
                }

                if (this.cachedStrangerChatSelfRoomUpdateMethodPtr == IntPtr.Zero)
                {
                    status = "SelfRoomSystem.UpdateChatVisibility method unavailable.";
                    return false;
                }

                bool value = onlyFriend;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&value);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.cachedStrangerChatSelfRoomUpdateMethodPtr, selfRoomSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "SelfRoomSystem.UpdateChatVisibility raised exception.";
                    return false;
                }

                status = "SelfRoomSystem.UpdateChatVisibility(" + onlyFriend + ") invoked.";
                return true;
            }
            catch (Exception ex)
            {
                status = "SelfRoomSystem.UpdateChatVisibility exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoSelfRoomProtocolChatVisibility(bool onlyFriend, out string status)
        {
            status = "SelfRoomProtocolManager.SelfRoomSetChatVisibility_InRoom unavailable.";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Login.SelfRoomProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    status = "SelfRoomProtocolManager class unavailable.";
                    return false;
                }

                if (this.cachedStrangerChatSelfRoomProtocolMethodPtr == IntPtr.Zero)
                {
                    this.cachedStrangerChatSelfRoomProtocolMethodPtr = this.FindAuraMonoMethodOnHierarchy(protocolClass, "SelfRoomSetChatVisibility_InRoom", 1);
                }

                if (this.cachedStrangerChatSelfRoomProtocolMethodPtr == IntPtr.Zero)
                {
                    status = "SelfRoomProtocolManager.SelfRoomSetChatVisibility_InRoom method unavailable.";
                    return false;
                }

                bool value = onlyFriend;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&value);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.cachedStrangerChatSelfRoomProtocolMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "SelfRoomProtocolManager.SelfRoomSetChatVisibility_InRoom raised exception.";
                    return false;
                }

                status = "SelfRoomProtocolManager.SelfRoomSetChatVisibility_InRoom(" + onlyFriend + ") invoked.";
                return true;
            }
            catch (Exception ex)
            {
                status = "SelfRoomProtocolManager.SelfRoomSetChatVisibility_InRoom exception: " + ex.Message;
                return false;
            }
        }

    }
}
