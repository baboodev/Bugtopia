using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private const int ChatTranslateStreamEventTypeDone = 4;       // TranslateStreamEventType.Done

        private bool chatForceTranslateEnabled;
        private bool chatTranslateVerboseLog;
        private bool chatTranslateForceAllLangs;
        private bool chatForceTranslateHooksRegistered;
        private float chatForceTranslateNextHookAttemptAt = -999f;
        private float chatForceTranslateNextResolveAt = -999f;
        private int chatForceTranslateWorldEpoch = -1;
        private int chatForceTranslateClientLangKey = -1;
        private IntPtr chatForceTranslateMethodPtr;
        private bool chatForceTranslateUnavailableLogged;
        private int chatForceTranslateSentCount;
        private int chatForceTranslateSucceededCount;
        private IntPtr chatTranslateGetCacheMethodPtr;
        private int chatForceTranslateLastErrorCode;
        private float chatForceTranslateNextErrorLogAt;
        private bool chatTranslateGameStateLogged;
        private bool chatTranslateGameToggleValue;
        private float chatTranslateGameToggleValidUntil = -999f;
        private IntPtr chatTranslateRecordMsgIdField;
        private readonly HashSet<ulong> chatForceTranslateRequested = new HashSet<ulong>();

        // Field offsets inside the snapshotted event payloads. ReceiveChatMessage contains a
        // DateTime field (LayoutKind.Auto), which makes the managed layout of the whole struct
        // unspecified — resolve the real offsets from mono metadata (mono_field_get_offset minus
        // the 2*IntPtr.Size boxed header) instead of trusting the C# declaration order. -1 =
        // unresolved; readers fall back to the sequential-layout guess until resolved.
        private int chatTranslateOffPlayerNetId = -1;
        private int chatTranslateOffShortId = -1;
        private int chatTranslateOffMsgId = -1;
        private int chatTranslateOffLangKey = -1;
        private int chatTranslateOffResultErrorCode = -1;
        private int chatTranslateOffResultMsgId = -1;
        private int chatTranslateOffResultEventType = -1;
        private bool chatTranslateOffsetsLogged;

        private void ChatTranslateLog(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ModLogger.Msg("[ChatTranslate] " + message);
            }
        }

        // Per-message decision trace for diagnosing "why doesn't player X translate". Enabled by
        // the "Translate Debug Log" sub-toggle; chat volume is low, so per-message lines are fine.
        private void ChatTranslateVerbose(string message)
        {
            if (this.chatTranslateVerboseLog)
            {
                this.ChatTranslateLog(message);
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
                this.chatTranslateRecordMsgIdField = IntPtr.Zero;
                this.chatTranslateGetCacheMethodPtr = IntPtr.Zero;
                this.chatForceTranslateNextResolveAt = -999f;
                this.chatTranslateGameStateLogged = false;
                this.chatTranslateGameToggleValidUntil = -999f;
                this.ResetChatTranslatePostcardWorldState();
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
                this.MaybeLogChatTranslateGameState();
            }

            // Postcard-endpoint bypass (opt-in): install the result detour, drain captured results,
            // and pump the serialized send queue.
            this.UpdateChatTranslatePostcardBypass(now);
        }

        // One-shot per world (verbose only): the two game-side gates that decide whether the
        // GAME could translate at all — overseas build flag and the in-panel Translate toggle
        // (whose getter also folds in the weekly-char / GM limits).
        private void MaybeLogChatTranslateGameState()
        {
            if (!this.chatTranslateVerboseLog || this.chatTranslateGameStateLogged)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
                {
                    return;
                }

                string overSeaText = "?";
                if (this.TryResolveAuraMonoModule("XDTGUI.Module.Login.LoginSystem", out IntPtr loginObj) && loginObj != IntPtr.Zero)
                {
                    IntPtr loginClass = auraMonoObjectGetClass(loginObj);
                    if (loginClass != IntPtr.Zero
                        && this.TryInvokeAuraMonoBoolGetter(loginObj, loginClass, out bool overSea, "get_IsOverSea", "IsOverSea"))
                    {
                        overSeaText = overSea.ToString();
                    }
                }

                string gameToggleText = this.TryGetChatTranslateGameToggle(out bool gameOn) ? gameOn.ToString() : "?";
                this.chatTranslateGameStateLogged = true;
                this.ChatTranslateLog("Game state: LoginSystem.IsOverSea=" + overSeaText
                    + " ChatSystem.TranslateOn(effective)=" + gameToggleText
                    + " clientLangKey=" + this.chatForceTranslateClientLangKey);
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("Game state probe exception: " + ex.Message);
            }
        }

        // Effective in-game Translate toggle (ChatSystem.get_TranslateOn: false when CN build,
        // toggle off, or weekly/GM limited). Cached for 2 s — read per incoming message otherwise.
        private bool TryGetChatTranslateGameToggle(out bool effectiveOn)
        {
            effectiveOn = this.chatTranslateGameToggleValue;
            float now = Time.unscaledTime;
            if (now < this.chatTranslateGameToggleValidUntil)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Chat.ChatSystem", out IntPtr chatSystemObj)
                    || chatSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr chatSystemClass = auraMonoObjectGetClass(chatSystemObj);
                if (chatSystemClass == IntPtr.Zero
                    || !this.TryInvokeAuraMonoBoolGetter(chatSystemObj, chatSystemClass, out bool on, "get_TranslateOn"))
                {
                    return false;
                }

                this.chatTranslateGameToggleValue = on;
                this.chatTranslateGameToggleValidUntil = now + 2f;
                effectiveOn = on;
                return true;
            }
            catch
            {
                return false;
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
                int offShortId = (this.chatTranslateOffShortId >= 0) ? this.chatTranslateOffShortId : 8;
                int offMsgId = (this.chatTranslateOffMsgId >= 0) ? this.chatTranslateOffMsgId : 16;
                int offLang = (this.chatTranslateOffLangKey >= 0) ? this.chatTranslateOffLangKey : 44;

                uint playerNetId = snap.ReadUInt32(offNetId);
                long shortId = unchecked((long)snap.ReadUInt64(offShortId));
                ulong msgId = snap.ReadUInt64(offMsgId);
                int langKey = snap.ReadInt32(offLang);
                if (msgId == 0UL)
                {
                    return;
                }

                bool haveSelf = this.TryResolveSelfPlayerNetId(out uint selfNetId) && selfNetId != 0U;
                bool haveClientLang = this.TryResolveChatTranslateClientLangKey(out int clientLang);
                string messageText = null;
                if (this.chatTranslateVerboseLog)
                {
                    this.TryGetChatTranslateMessageText(msgId, out messageText);
                }

                this.ChatTranslateVerbose("recv msgId=" + msgId
                    + " player netId=" + playerNetId + " shortId=" + shortId
                    + " langKey=" + langKey
                    + " clientLang=" + (haveClientLang ? clientLang.ToString() : "?")
                    + " self=" + (haveSelf ? selfNetId.ToString() : "?")
                    + " text=" + ChatTranslateTextForLog(messageText));

                // Never translate our own messages; fail closed while self is unresolved.
                if (!haveSelf)
                {
                    this.ChatTranslateVerbose("  -> skip: self netId unresolved yet.");
                    return;
                }

                if (playerNetId == selfNetId)
                {
                    // Server stamps our own messages with OUR langKey (from ConnectScene_CS) —
                    // a reflection-free fallback source when the LocalizationManager probes fail.
                    if (!haveClientLang && langKey >= 0)
                    {
                        this.chatForceTranslateClientLangKey = langKey;
                        this.chatForceTranslateUnavailableLogged = false;
                        this.ChatTranslateLog("Client language key = " + langKey + " (learned from own message).");
                    }

                    this.ChatTranslateVerbose("  -> skip: own message.");
                    return;
                }

                if (!haveClientLang)
                {
                    this.ChatTranslateVerbose("  -> skip: client langKey unresolved.");
                    return;
                }

                // Same-langKey messages are the blocked case the game never requests — always
                // ours. Foreign-langKey messages belong to the game's own translate pipeline;
                // with "Force ALL Languages" we take them ONLY when the game's effective
                // Translate toggle is off/unreadable — a double request would corrupt the game's
                // append-only translation cache with duplicate stream chunks.
                string reason;
                if (langKey == clientLang)
                {
                    reason = "same-langKey (game refuses to translate these)";
                }
                else if (!this.chatTranslateForceAllLangs)
                {
                    this.ChatTranslateVerbose("  -> skip: foreign langKey, left to the game's translate pipeline (Force ALL off).");
                    return;
                }
                else if (this.TryGetChatTranslateGameToggle(out bool gameOn) && gameOn)
                {
                    this.ChatTranslateVerbose("  -> skip: foreign langKey and in-game Translate toggle is ON (game auto-translates; no double request).");
                    return;
                }
                else
                {
                    reason = "force-all (in-game Translate toggle off/unreadable)";
                }

                if (this.chatForceTranslateRequested.Count >= 2048)
                {
                    this.chatForceTranslateRequested.Clear();
                }

                if (!this.chatForceTranslateRequested.Add(msgId))
                {
                    this.ChatTranslateVerbose("  -> skip: already requested.");
                    return;
                }

                // Both endpoints translate the supplied text, so resolve it from ChatSystem.record.
                if (messageText == null)
                {
                    this.TryGetChatTranslateMessageText(msgId, out messageText);
                }

                // Postcard-endpoint bypass: route through the postcard translator (no source-langKey
                // gate) instead of the chat endpoint. Needs the text (postcard content can't be empty).
                if (this.chatTranslatePostcardBypass)
                {
                    if (string.IsNullOrEmpty(messageText))
                    {
                        this.chatForceTranslateRequested.Remove(msgId);
                        this.ChatTranslateVerbose("  -> skip postcard route: message text unavailable in ChatSystem.record.");
                        return;
                    }

                    this.EnqueueChatTranslatePostcard(msgId, messageText, reason);
                    return;
                }

                // The stream endpoint validates OriginalMessage — an empty one comes back as
                // 4207 TranslateContentInvalid (confirmed live), so mirror the game and always
                // send the original text, resolved from ChatSystem.record.
                if (string.IsNullOrEmpty(messageText))
                {
                    this.ChatTranslateVerbose("  -> message text unavailable in ChatSystem.record; sending msgId-only request.");
                }

                if (this.TryChatTranslateSendRequest(msgId, messageText))
                {
                    this.ChatTranslateVerbose("  -> translate request SENT (" + reason + ", textChars="
                        + (messageText != null ? messageText.Length : 0) + ").");
                }
                else
                {
                    this.chatForceTranslateRequested.Remove(msgId);
                    this.ChatTranslateVerbose("  -> send FAILED (see log above).");
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
                int offType = (this.chatTranslateOffResultEventType >= 0) ? this.chatTranslateOffResultEventType : 16;
                int errorCode = snap.ReadInt32(offErr);
                ulong msgId = snap.ReadUInt64(offMsg);
                int eventType = snap.ReadInt32(offType);
                if (msgId == 0UL)
                {
                    return;
                }

                bool ours = this.chatForceTranslateRequested.Contains(msgId);

                // Verbose: trace EVERY stream result (game-initiated included) — this is the
                // server's actual verdict per message, the key signal when diagnosing why a
                // specific player never translates.
                this.ChatTranslateVerbose("stream result msgId=" + msgId
                    + " errorCode=" + errorCode
                    + " eventType=" + ChatTranslateStreamEventTypeName(eventType)
                    + (ours ? " (our request)" : " (game request)"));

                if (errorCode != 0 && ours)
                {
                    this.chatForceTranslateLastErrorCode = errorCode;
                    float nowErr = Time.unscaledTime;
                    if (nowErr >= this.chatForceTranslateNextErrorLogAt)
                    {
                        this.chatForceTranslateNextErrorLogAt = nowErr + 5f;
                        this.TryGetChatTranslateMessageText(msgId, out string messageText);
                        string suffix = " for msg " + msgId + " text=" + ChatTranslateTextForLog(messageText) + ".";
                        this.ChatTranslateLog(errorCode == ChatTranslateErrorNoNeedToTranslate
                            ? "Server answered NoNeedToTranslate" + suffix + " (it judged the text already in our language)"
                            : "Translate stream error " + errorCode + suffix);
                    }
                    return;
                }

                // Success: on the terminal Done event read the accumulated translation from the
                // game's own cache (ChatSystem.GetTranslatedCache — a rooted dictionary, so the
                // returned string is safe to read synchronously) and log the finished text. Logged
                // for our forced requests always; for game-initiated translations only in verbose.
                if (errorCode == 0
                    && eventType == ChatTranslateStreamEventTypeDone
                    && (ours || this.chatTranslateVerboseLog))
                {
                    if (this.TryResolveChatTranslateClientLangKey(out int clientLang)
                        && this.TryGetChatTranslatedText(msgId, clientLang, out string translated)
                        && !string.IsNullOrEmpty(translated))
                    {
                        this.chatForceTranslateSucceededCount++;
                        this.TryGetChatTranslateMessageText(msgId, out string original);
                        this.ChatTranslateLog("Chat translated msg " + msgId
                            + (ours ? " (forced)" : " (game)")
                            + ": " + ChatTranslateTextForLog(original)
                            + " -> " + ChatTranslateTextForLog(translated));
                    }
                }
            }
            catch
            {
            }
        }

        // Read a completed translation from ChatSystem's cache (GetTranslatedCache(msgId, langKey)).
        // Returns a mono string OBJECT from a rooted dictionary; read synchronously via the invoke
        // result — never a deferred raw pointer.
        private unsafe bool TryGetChatTranslatedText(ulong msgId, int langKey, out string text)
        {
            text = null;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Chat.ChatSystem", out IntPtr chatSystemObj)
                    || chatSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr chatSystemClass = auraMonoObjectGetClass(chatSystemObj);
                if (chatSystemClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.chatTranslateGetCacheMethodPtr == IntPtr.Zero)
                {
                    this.chatTranslateGetCacheMethodPtr = this.FindAuraMonoMethodOnHierarchy(chatSystemClass, "GetTranslatedCache", 2);
                    if (this.chatTranslateGetCacheMethodPtr == IntPtr.Zero)
                    {
                        return false;
                    }
                }

                ulong msgIdValue = msgId;
                int langValue = langKey;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&msgIdValue);
                args[1] = (IntPtr)(&langValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr resultStr = auraMonoRuntimeInvoke(this.chatTranslateGetCacheMethodPtr, chatSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || resultStr == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryReadMonoString(resultStr, out text) && !string.IsNullOrEmpty(text);
            }
            catch
            {
                return false;
            }
        }

        // Resolve the original message text by msgId from ChatSystem.record (AuraMono). Runs in
        // the OnUpdate drain, AFTER the game's own ReceiveChatMessage listeners populated the
        // record — the unpinned string pointer inside the event snapshot is never dereferenced.
        // GetChatRecordData() returns a fresh List<ChatRecordData> copy (<= 30 boxed structs);
        // items are pinned during the walk (SGen moving GC).
        private bool TryGetChatTranslateMessageText(ulong msgId, out string text)
        {
            text = null;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                    || auraMonoObjectUnbox == null || auraMonoFieldGetOffset == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Chat.ChatSystem", out IntPtr chatSystemObj)
                    || chatSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr chatSystemClass = auraMonoObjectGetClass(chatSystemObj);
                if (chatSystemClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getRecords = this.FindAuraMonoMethodOnHierarchy(chatSystemClass, "GetChatRecordData", 0);
                if (getRecords == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr listObj = auraMonoRuntimeInvoke(getRecords, chatSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                List<uint> pins = new List<uint>();
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                    {
                        return false;
                    }

                    // Newest records sit at the tail — walk backwards.
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        IntPtr boxed = items[i];
                        if (boxed == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (this.chatTranslateRecordMsgIdField == IntPtr.Zero)
                        {
                            IntPtr recordClass = auraMonoObjectGetClass(boxed);
                            if (recordClass == IntPtr.Zero)
                            {
                                continue;
                            }

                            this.chatTranslateRecordMsgIdField = this.FindAuraMonoFieldOnHierarchy(recordClass, "msgId");
                            if (this.chatTranslateRecordMsgIdField == IntPtr.Zero)
                            {
                                return false;
                            }
                        }

                        // msgId is a ulong — read the full 8 bytes from the unboxed struct (the
                        // generic member readers truncate boxed 64-bit values to 32 bits).
                        IntPtr data = auraMonoObjectUnbox(boxed);
                        if (data == IntPtr.Zero)
                        {
                            continue;
                        }

                        int msgIdOffset = (int)auraMonoFieldGetOffset(this.chatTranslateRecordMsgIdField) - 2 * IntPtr.Size;
                        if (msgIdOffset < 0)
                        {
                            return false;
                        }

                        ulong candidate = unchecked((ulong)Marshal.ReadInt64(data, msgIdOffset));
                        if (candidate != msgId)
                        {
                            continue;
                        }

                        return this.TryGetMonoStringMember(boxed, "content", out text) && !string.IsNullOrEmpty(text);
                    }

                    return false;
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("Message text lookup exception: " + ex.Message);
                return false;
            }
        }

        private static string ChatTranslateTextForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "<none>";
            }

            string flat = text.Replace("\r", " ").Replace("\n", " ");
            return "\"" + (flat.Length > 120 ? flat.Substring(0, 120) + "…" : flat) + "\"";
        }

        private static string ChatTranslateStreamEventTypeName(int eventType)
        {
            switch (eventType)
            {
                case 0: return "None";
                case 1: return "Chunk";
                case 2: return "Retract";
                case 3: return "Error";
                case 4: return "Done";
                default: return eventType.ToString();
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
                        IntPtr shortIdField = this.FindAuraMonoFieldOnHierarchy(klass, "shortId");
                        IntPtr msgIdField = this.FindAuraMonoFieldOnHierarchy(klass, "msgId");
                        IntPtr langField = this.FindAuraMonoFieldOnHierarchy(klass, "langKey");
                        if (netIdField != IntPtr.Zero && msgIdField != IntPtr.Zero && langField != IntPtr.Zero)
                        {
                            this.chatTranslateOffPlayerNetId = (int)auraMonoFieldGetOffset(netIdField) - header;
                            this.chatTranslateOffMsgId = (int)auraMonoFieldGetOffset(msgIdField) - header;
                            this.chatTranslateOffLangKey = (int)auraMonoFieldGetOffset(langField) - header;
                            if (shortIdField != IntPtr.Zero)
                            {
                                this.chatTranslateOffShortId = (int)auraMonoFieldGetOffset(shortIdField) - header;
                            }
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
                        IntPtr typeField = this.FindAuraMonoFieldOnHierarchy(klass, "eventType");
                        if (errField != IntPtr.Zero && msgIdField != IntPtr.Zero)
                        {
                            this.chatTranslateOffResultErrorCode = (int)auraMonoFieldGetOffset(errField) - header;
                            this.chatTranslateOffResultMsgId = (int)auraMonoFieldGetOffset(msgIdField) - header;
                            if (typeField != IntPtr.Zero)
                            {
                                this.chatTranslateOffResultEventType = (int)auraMonoFieldGetOffset(typeField) - header;
                            }
                        }
                    }
                }

                if (!this.chatTranslateOffsetsLogged && this.chatTranslateOffMsgId >= 0 && this.chatTranslateOffResultMsgId >= 0)
                {
                    this.chatTranslateOffsetsLogged = true;
                    this.ChatTranslateLog("Event field offsets: playerNetId=" + this.chatTranslateOffPlayerNetId
                        + " shortId=" + this.chatTranslateOffShortId
                        + " msgId=" + this.chatTranslateOffMsgId
                        + " langKey=" + this.chatTranslateOffLangKey
                        + " resultErrorCode=" + this.chatTranslateOffResultErrorCode
                        + " resultMsgId=" + this.chatTranslateOffResultMsgId
                        + " resultEventType=" + this.chatTranslateOffResultEventType);
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

            if (this.TryResolveChatTranslateClientLangKeyManaged(out langKey))
            {
                this.chatForceTranslateClientLangKey = langKey;
                this.chatForceTranslateUnavailableLogged = false;
                this.ChatTranslateLog("Client language key = " + langKey + " (managed interop).");
                return true;
            }

            return this.TryResolveChatTranslateClientLangKeyAura(out langKey);
        }

        // Managed interop path: LocalizationManager is an engine-side IL2CPP class, so the
        // interop stub is callable via plain reflection (the AuraMono copy lives in the
        // EngineWrapper mono image and is only the fallback).
        private bool TryResolveChatTranslateClientLangKeyManaged(out int langKey)
        {
            langKey = -1;
            try
            {
                Type type = this.FindLoadedType(
                    "XDFramework.Expansion.LocalizationManager",
                    "Il2CppXDFramework.Expansion.LocalizationManager",
                    "LocalizationManager");
                if (type == null)
                {
                    return false;
                }

                System.Reflection.PropertyInfo instanceProp = type.GetProperty("Instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                object instance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (instance == null)
                {
                    return false;
                }

                System.Reflection.MethodInfo method = type.GetMethod("GetLanguageKey",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (method == null)
                {
                    return false;
                }

                object result = method.Invoke(instance, null);
                if (result == null)
                {
                    return false;
                }

                int value = Convert.ToInt32(result);
                if (value < 0)
                {
                    return false;
                }

                langKey = value;
                return true;
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("Managed language key probe failed: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveChatTranslateClientLangKeyAura(out int langKey)
        {
            langKey = -1;

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
                this.ChatTranslateLog("Client language key = " + value + " (AuraMono).");
                langKey = value;
                return true;
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Language key resolve exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryChatTranslateSendRequest(ulong msgId, string originalText)
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
                // The stream endpoint rejects an empty OriginalMessage with 4207
                // TranslateContentInvalid, so pass the original text (the game does the same).
                ulong msgIdValue = msgId;
                IntPtr textObj = auraMonoStringNew(this.auraMonoRootDomain, originalText ?? string.Empty);
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&msgIdValue);
                args[1] = textObj;
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
