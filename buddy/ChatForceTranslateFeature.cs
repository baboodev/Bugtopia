using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Chat Translate Unlock: request server translation for chat messages the game refuses to
    // translate because the sender's UI language matches ours.
    //
    // ChatMessageComponent.LangCode is the SENDER'S UI LANGUAGE (reported once via
    // ConnectScene_CS at scene connect), not the language of the typed text. Every client-side
    // translate gate (ChatSystem.RequestTranslate, the per-message context menu,
    // ChatModule auto-translate) only compares data.langKey == LocalizationManager.GetLanguageKey(),
    // so a player typing Russian on an English UI produces messages an English-UI receiver can
    // never translate — no button, no auto-translate, the request never reaches the server.
    //
    // Bypass: hook the ReceiveChatMessage EventCenter event (scalar fields only — the message
    // string pointer in the snapshot is unpinned and must never be dereferenced) and, for
    // non-self messages whose langKey EQUALS ours (the blocked case; foreign langKeys remain the
    // game's own job via its in-game Translate toggle), invoke the Mono static
    // ChatProtocolManager.RequestTranslateStream(msgId, "") directly. The server still holds the
    // message text (it just synced ChatMessageComponent) and streams the translation back; the
    // game's own ChatSystem stream handler accumulates the chunks, caches them and updates
    // bubbles/HUD/history — no UI work needed here. If the server decides the text is already in
    // our language it answers NoNeedToTranslate (4209) and nothing visible changes.
    public partial class HeartopiaComplete
    {
        private const string ChatTranslateReceiveEventName = "XDTDataAndProtocol.Events.ReceiveChatMessage";
        private const int ChatTranslateReceiveEventBytes = 48;
        private const string ChatTranslateStreamResultEventName = "XDTDataAndProtocol.Events.RequestTranslateStreamResultEvent";
        private const int ChatTranslateStreamResultEventBytes = 20;
        private const int ChatTranslateErrorNoNeedToTranslate = 4209; // ErrorCode.NoNeedToTranslate

        private bool chatForceTranslateEnabled;
        private bool chatForceTranslateHooksRegistered;
        private float chatForceTranslateNextHookAttemptAt = -999f;
        private float chatForceTranslateNextResolveAt = -999f;
        private int chatForceTranslateWorldEpoch = -1;
        private int chatForceTranslateClientLangKey = -1;
        private IntPtr chatForceTranslateMethodPtr;
        private bool chatForceTranslateUnavailableLogged;
        private int chatForceTranslateSentCount;
        private int chatForceTranslateLastErrorCode;
        private float chatForceTranslateNextErrorLogAt;
        private readonly HashSet<ulong> chatForceTranslateRequested = new HashSet<ulong>();

        // Field offsets inside the snapshotted event payloads. ReceiveChatMessage contains a
        // DateTime field (LayoutKind.Auto), which makes the managed layout of the whole struct
        // unspecified — resolve the real offsets from mono metadata (mono_field_get_offset minus
        // the 2*IntPtr.Size boxed header) instead of trusting the C# declaration order. -1 =
        // unresolved; readers fall back to the sequential-layout guess until resolved.
        private int chatTranslateOffPlayerNetId = -1;
        private int chatTranslateOffMsgId = -1;
        private int chatTranslateOffLangKey = -1;
        private int chatTranslateOffResultErrorCode = -1;
        private int chatTranslateOffResultMsgId = -1;
        private bool chatTranslateOffsetsLogged;

        private void ChatTranslateLog(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ModLogger.Msg("[ChatTranslate] " + message);
            }
        }

        private void ChatTranslateLogUnavailableOnce(string reason)
        {
            if (this.chatForceTranslateUnavailableLogged)
            {
                return;
            }

            this.chatForceTranslateUnavailableLogged = true;
            this.ChatTranslateLog(reason);
        }

        internal void EnsureChatForceTranslateFeature()
        {
            if (!this.chatForceTranslateEnabled)
            {
                return;
            }

            // World change: forget per-world state. msgIds are per-session, and the client
            // language key can change via the reconnect a language switch forces.
            int epoch = AuraMonoWorldEpoch;
            if (epoch != this.chatForceTranslateWorldEpoch)
            {
                this.chatForceTranslateWorldEpoch = epoch;
                this.chatForceTranslateRequested.Clear();
                this.chatForceTranslateClientLangKey = -1;
                this.chatForceTranslateMethodPtr = IntPtr.Zero;
                this.chatForceTranslateNextResolveAt = -999f;
            }

            float now = Time.unscaledTime;
            if (!this.chatForceTranslateHooksRegistered && now >= this.chatForceTranslateNextHookAttemptAt)
            {
                this.chatForceTranslateNextHookAttemptAt = now + 30f;
                bool receive = this.RegisterGameEventHook(ChatTranslateReceiveEventName, ChatTranslateReceiveEventBytes, this.OnChatTranslateReceiveChatMessage);
                bool result = this.RegisterGameEventHook(ChatTranslateStreamResultEventName, ChatTranslateStreamResultEventBytes, this.OnChatTranslateStreamResult);
                this.chatForceTranslateHooksRegistered = receive; // result hook is diagnostics-only
                if (receive)
                {
                    this.ChatTranslateLog("Event hooks registered (receive=" + receive + ", result=" + result + ").");
                }
            }

            // Warm the offset/langKey/method caches off the hot path while enabled.
            if (now >= this.chatForceTranslateNextResolveAt)
            {
                this.chatForceTranslateNextResolveAt = now + 5f;
                this.TryResolveChatTranslateEventOffsets();
                this.TryResolveChatTranslateClientLangKey(out _);
            }
        }

        private void OnChatTranslateReceiveChatMessage(GameEventSnapshot snap)
        {
            try
            {
                if (!this.chatForceTranslateEnabled)
                {
                    return;
                }

                int offNetId = (this.chatTranslateOffPlayerNetId >= 0) ? this.chatTranslateOffPlayerNetId : 0;
                int offMsgId = (this.chatTranslateOffMsgId >= 0) ? this.chatTranslateOffMsgId : 16;
                int offLang = (this.chatTranslateOffLangKey >= 0) ? this.chatTranslateOffLangKey : 44;

                uint playerNetId = snap.ReadUInt32(offNetId);
                ulong msgId = snap.ReadUInt64(offMsgId);
                int langKey = snap.ReadInt32(offLang);
                if (msgId == 0UL)
                {
                    return;
                }

                // Never translate our own messages; fail closed while self is unresolved.
                if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U || playerNetId == selfNetId)
                {
                    return;
                }

                // Only the blocked case: sender's langKey matches ours. Foreign-langKey messages
                // stay with the game's own translate pipeline (its Translate toggle), so we never
                // double-request a message — duplicate stream chunks would corrupt the game's
                // append-only translation cache.
                if (!this.TryResolveChatTranslateClientLangKey(out int clientLang) || langKey != clientLang)
                {
                    return;
                }

                if (this.chatForceTranslateRequested.Count >= 2048)
                {
                    this.chatForceTranslateRequested.Clear();
                }

                if (!this.chatForceTranslateRequested.Add(msgId))
                {
                    return;
                }

                if (!this.TryChatTranslateSendRequest(msgId))
                {
                    this.chatForceTranslateRequested.Remove(msgId);
                }
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Receive handler exception: " + ex.Message);
            }
        }

        private void OnChatTranslateStreamResult(GameEventSnapshot snap)
        {
            try
            {
                if (!this.chatForceTranslateEnabled)
                {
                    return;
                }

                int offErr = (this.chatTranslateOffResultErrorCode >= 0) ? this.chatTranslateOffResultErrorCode : 0;
                int offMsg = (this.chatTranslateOffResultMsgId >= 0) ? this.chatTranslateOffResultMsgId : 8;
                int errorCode = snap.ReadInt32(offErr);
                ulong msgId = snap.ReadUInt64(offMsg);
                if (msgId == 0UL || errorCode == 0 || !this.chatForceTranslateRequested.Contains(msgId))
                {
                    return;
                }

                this.chatForceTranslateLastErrorCode = errorCode;
                float now = Time.unscaledTime;
                if (now >= this.chatForceTranslateNextErrorLogAt)
                {
                    this.chatForceTranslateNextErrorLogAt = now + 5f;
                    this.ChatTranslateLog(errorCode == ChatTranslateErrorNoNeedToTranslate
                        ? "Server answered NoNeedToTranslate for msg " + msgId + " (text already in our language)."
                        : "Translate stream error " + errorCode + " for msg " + msgId + ".");
                }
            }
            catch
            {
            }
        }

        private void TryResolveChatTranslateEventOffsets()
        {
            if (this.chatTranslateOffMsgId >= 0 && this.chatTranslateOffResultMsgId >= 0)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoFieldGetOffset == null)
                {
                    return;
                }

                int header = 2 * IntPtr.Size;
                if (this.chatTranslateOffMsgId < 0)
                {
                    IntPtr klass = this.FindAuraMonoClassByFullName(ChatTranslateReceiveEventName);
                    if (klass != IntPtr.Zero)
                    {
                        IntPtr netIdField = this.FindAuraMonoFieldOnHierarchy(klass, "playerNetId");
                        IntPtr msgIdField = this.FindAuraMonoFieldOnHierarchy(klass, "msgId");
                        IntPtr langField = this.FindAuraMonoFieldOnHierarchy(klass, "langKey");
                        if (netIdField != IntPtr.Zero && msgIdField != IntPtr.Zero && langField != IntPtr.Zero)
                        {
                            this.chatTranslateOffPlayerNetId = (int)auraMonoFieldGetOffset(netIdField) - header;
                            this.chatTranslateOffMsgId = (int)auraMonoFieldGetOffset(msgIdField) - header;
                            this.chatTranslateOffLangKey = (int)auraMonoFieldGetOffset(langField) - header;
                        }
                    }
                }

                if (this.chatTranslateOffResultMsgId < 0)
                {
                    IntPtr klass = this.FindAuraMonoClassByFullName(ChatTranslateStreamResultEventName);
                    if (klass != IntPtr.Zero)
                    {
                        IntPtr errField = this.FindAuraMonoFieldOnHierarchy(klass, "errorCode");
                        IntPtr msgIdField = this.FindAuraMonoFieldOnHierarchy(klass, "msgId");
                        if (errField != IntPtr.Zero && msgIdField != IntPtr.Zero)
                        {
                            this.chatTranslateOffResultErrorCode = (int)auraMonoFieldGetOffset(errField) - header;
                            this.chatTranslateOffResultMsgId = (int)auraMonoFieldGetOffset(msgIdField) - header;
                        }
                    }
                }

                if (!this.chatTranslateOffsetsLogged && this.chatTranslateOffMsgId >= 0 && this.chatTranslateOffResultMsgId >= 0)
                {
                    this.chatTranslateOffsetsLogged = true;
                    this.ChatTranslateLog("Event field offsets: playerNetId=" + this.chatTranslateOffPlayerNetId
                        + " msgId=" + this.chatTranslateOffMsgId
                        + " langKey=" + this.chatTranslateOffLangKey
                        + " resultErrorCode=" + this.chatTranslateOffResultErrorCode
                        + " resultMsgId=" + this.chatTranslateOffResultMsgId);
                }
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Offset resolve exception: " + ex.Message);
            }
        }

        private bool TryResolveChatTranslateClientLangKey(out int langKey)
        {
            langKey = this.chatForceTranslateClientLangKey;
            if (langKey >= 0)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr klass = this.FindAuraMonoClassByFullName("XDFramework.Expansion.LocalizationManager");
                if (klass == IntPtr.Zero)
                {
                    this.ChatTranslateLogUnavailableOnce("LocalizationManager class unavailable.");
                    return false;
                }

                IntPtr getInstance = this.FindAuraMonoMethodOnHierarchy(klass, "get_Instance", 0);
                IntPtr getLanguageKey = this.FindAuraMonoMethodOnHierarchy(klass, "GetLanguageKey", 0);
                if (getInstance == IntPtr.Zero || getLanguageKey == IntPtr.Zero)
                {
                    this.ChatTranslateLogUnavailableOnce("LocalizationManager.Instance/GetLanguageKey unavailable.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr instance = auraMonoRuntimeInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || instance == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(getLanguageKey, instance, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoInt32(boxed, out int value) || value < 0)
                {
                    return false;
                }

                this.chatForceTranslateClientLangKey = value;
                this.chatForceTranslateUnavailableLogged = false;
                this.ChatTranslateLog("Client language key = " + value + ".");
                langKey = value;
                return true;
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Language key resolve exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryChatTranslateSendRequest(ulong msgId)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoStringNew == null || this.auraMonoRootDomain == IntPtr.Zero)
                {
                    this.ChatTranslateLogUnavailableOnce("AuraMono runtime not ready for translate request.");
                    return false;
                }

                if (this.chatForceTranslateMethodPtr == IntPtr.Zero)
                {
                    IntPtr klass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Social.ChatProtocolManager");
                    if (klass == IntPtr.Zero)
                    {
                        this.ChatTranslateLogUnavailableOnce("ChatProtocolManager class unavailable.");
                        return false;
                    }

                    this.chatForceTranslateMethodPtr = this.FindAuraMonoMethodOnHierarchy(klass, "RequestTranslateStream", 2);
                    if (this.chatForceTranslateMethodPtr == IntPtr.Zero)
                    {
                        this.ChatTranslateLogUnavailableOnce("ChatProtocolManager.RequestTranslateStream(2) unavailable.");
                        return false;
                    }
                }

                // Static invoke: RequestTranslateStream(ulong msgId, string originalMessage).
                // Empty originalMessage — the server just synced the message entity and resolves
                // the text by msgId (the game's own non-stream path relies on the same behavior).
                ulong msgIdValue = msgId;
                IntPtr emptyText = auraMonoStringNew(this.auraMonoRootDomain, string.Empty);
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&msgIdValue);
                args[1] = emptyText;
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.chatForceTranslateMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.ChatTranslateLog("RequestTranslateStream raised a mono exception (msg " + msgId + ").");
                    return false;
                }

                this.chatForceTranslateSentCount++;
                this.chatForceTranslateUnavailableLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Send exception: " + ex.Message);
                return false;
            }
        }
    }
}
