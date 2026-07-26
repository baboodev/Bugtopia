using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Postcard-endpoint translation bypass (experimental).
    //
    // The chat translator gates on langCode (sender's declared UI language) both client- and
    // server-side, so foreign text typed on an English UI is refused (NoNeedToTranslate). The
    // POSTCARD translator (`TranslatePostCardCommand{MailId,Content}` →
    // `TranslatePostCardResultEvent{TranslatedContent}`) takes arbitrary Content and has NO
    // source-langCode gate (only the weekly-char quota), so it may translate what chat refuses.
    //
    // Flow: route a blocked chat message's text through RequestTranslatePostCard(<an owned postcard
    // MailId>, <chat text>), serialized one at a time, and read the SERVER'S VERDICT back.
    //
    // ⚠️ HISTORY — do not "improve" this back: the first version installed its own NativeDetour on
    // DispatchEvent<PostCardTranslateResultEvent> and read the translated mono string SYNCHRONOUSLY
    // in the detour body (mono_string_to_utf8 + Marshal.PtrToStringUTF8), on the theory that it
    // "only allocates a native buffer". That FROZE THE GAME (2026-07-27): calling into Mono — and
    // allocating managed memory — from a reverse-pinvoke body while Mono holds its runtime locks
    // deadlocks the main thread (watchdog: stuck 16s at ou.end, RIP in an ntdll wait, coreclr →
    // mono-2.0-sgen on the frozen stack). Detour bodies must not call Mono or allocate — that is
    // exactly why the shared event-hook engine only memcpy's into a preallocated ring buffer.
    //
    // Current design: use the shared engine (RegisterGameEventHook) and read ONLY SCALARS from the
    // snapshot — the mailId bytes (to confirm the result is ours) and the ErrorCode (the server's
    // verdict, which is the actual experimental question: Success vs 4207/4209/4210). The
    // translatedContent pointer in the snapshot is an unpinned mono object and is NEVER
    // dereferenced.
    public partial class HeartopiaComplete
    {
        private const string PostcardResultEventName = "XDTDataAndProtocol.Events.PostCardTranslateResultEvent";
        private const int PostcardResultEventBytes = 40; // Guid(16) + string ptr(8) + ErrorCode(4) + padding

        // Instance state (toggle + serialized send queue + mailId cache + backoff).
        private bool chatTranslatePostcardBypass;
        private readonly Queue<(ulong msgId, string text, string reason)> postcardTranslateQueue = new Queue<(ulong, string, string)>();
        private readonly byte[] postcardMailId = new byte[16];
        private bool postcardMailIdValid;
        private float postcardNextMailIdResolveAt = -999f;
        private bool postcardHookRegistered;
        private float postcardNextHookAttemptAt = -999f;
        private IntPtr postcardSendMethodPtr;
        private IntPtr postcardGetMailsMethodPtr;
        private IntPtr postcardGetIdMethodPtr;
        private IntPtr postcardClassPtr;
        private IntPtr postcardRequestMailsMethodPtr;
        private float postcardNextMailRequestAt = -999f; // don't spam GetMailsCommand at the server
        private bool postcardUnavailableLogged;
        // Give-up latch: without a postcard the route can NEVER arm, so stop retrying (and stop
        // logging) instead of repeating the same blocker forever. Cleared by re-toggling or a
        // world change.
        private bool postcardRouteDisarmed;
        private int postcardEmptyMailboxStrikes;
        private string postcardLastPumpState = string.Empty; // heartbeat logs only on CHANGE

        // Result-event field offsets (Guid makes the managed layout Auto ⇒ resolve from metadata).
        private int postcardResultMailIdOffset = -1;
        private int postcardResultErrorOffset = -1;
        private bool postcardOffsetsLogged;

        // Single outstanding request (serialized so the result — which carries only the shared
        // MailId, not a msgId — correlates unambiguously to what we sent).
        private bool postcardPendingActive;
        private ulong postcardPendingMsgId;
        private string postcardPendingText;
        private float postcardPendingSentAt;

        internal void ResetChatTranslatePostcardWorldState()
        {
            this.postcardTranslateQueue.Clear();
            this.postcardMailIdValid = false;
            this.postcardNextMailIdResolveAt = -999f;
            this.postcardSendMethodPtr = IntPtr.Zero;
            this.postcardGetMailsMethodPtr = IntPtr.Zero;
            this.postcardGetIdMethodPtr = IntPtr.Zero;
            this.postcardClassPtr = IntPtr.Zero;
            this.postcardRequestMailsMethodPtr = IntPtr.Zero;
            this.postcardNextMailRequestAt = -999f;
            this.postcardRouteDisarmed = false;
            this.postcardEmptyMailboxStrikes = 0;
            this.postcardLastPumpState = string.Empty;
            this.postcardPendingActive = false;
            this.postcardPendingText = null;
        }

        // Called from OnChatTranslateReceiveChatMessage when the postcard route is selected.
        private void EnqueueChatTranslatePostcard(ulong msgId, string text, string reason)
        {
            if (this.postcardTranslateQueue.Count >= 32)
            {
                // Drop oldest to bound memory; chat is low-volume so this is just a safety cap.
                this.postcardTranslateQueue.Dequeue();
            }

            this.postcardTranslateQueue.Enqueue((msgId, text, reason));
            this.ChatTranslateVerbose("  -> queued for postcard endpoint (" + reason + ", textChars=" + text.Length + ").");
        }

        // One-shot give-up: state the reason once, drop the backlog, and go silent. Re-arm by
        // toggling the sub-option off/on (or on a world change).
        private void DisarmPostcardRoute(string reason)
        {
            if (this.postcardRouteDisarmed)
            {
                return;
            }

            this.postcardRouteDisarmed = true;
            this.postcardTranslateQueue.Clear();
            this.ChatTranslateLog("Postcard bypass DISABLED: " + reason
                + ". Re-enable it by toggling 'Chat Translate: Postcard Bypass' off and on again.");
        }

        private void UpdateChatTranslatePostcardBypass(float now)
        {
            if (!this.chatTranslatePostcardBypass || this.postcardRouteDisarmed)
            {
                return;
            }

            // Result hook: the shared engine (snapshot + main-thread drain). Never a bespoke detour
            // that touches Mono — see the header note.
            if (!this.postcardHookRegistered && now >= this.postcardNextHookAttemptAt)
            {
                this.postcardNextHookAttemptAt = now + 30f;
                this.postcardHookRegistered = this.RegisterGameEventHook(
                    PostcardResultEventName, PostcardResultEventBytes, this.OnPostcardTranslateResultEvent);
                if (this.postcardHookRegistered)
                {
                    this.ChatTranslateLog("Postcard result hook registered.");
                }
            }

            // Timeout a stuck request so the queue keeps moving.
            if (this.postcardPendingActive && now - this.postcardPendingSentAt > 15f)
            {
                this.ChatTranslateLog("Postcard translate timed out for msg " + this.postcardPendingMsgId
                    + " (no result event within 15s).");
                this.postcardPendingActive = false;
                this.postcardPendingText = null;
            }

            // Pump: one outstanding at a time.
            if (!this.postcardPendingActive && this.postcardTranslateQueue.Count > 0)
            {
                // Heartbeat only when the BLOCKING STATE CHANGES — repeating an unchanged state
                // every few seconds is pure log spam.
                string pumpState = "hookRegistered=" + this.postcardHookRegistered
                    + " mailIdArmed=" + this.postcardMailIdValid;
                if (!string.Equals(pumpState, this.postcardLastPumpState, StringComparison.Ordinal))
                {
                    this.postcardLastPumpState = pumpState;
                    this.ChatTranslateLog("Postcard pump: queued=" + this.postcardTranslateQueue.Count + " " + pumpState);
                }

                if (!this.postcardHookRegistered)
                {
                    return; // without the result hook we'd never learn the verdict
                }

                if (!this.EnsurePostcardMailId(now))
                {
                    return; // EnsurePostcardMailId logs its own reason
                }

                (ulong msgId, string text, string reason) item = this.postcardTranslateQueue.Dequeue();
                if (this.TrySendPostcardTranslate(item.text))
                {
                    this.postcardPendingActive = true;
                    this.postcardPendingMsgId = item.msgId;
                    this.postcardPendingText = item.text;
                    this.postcardPendingSentAt = now;
                    this.chatForceTranslateSentCount++;
                    this.ChatTranslateVerbose("postcard request SENT for msg " + item.msgId
                        + " (" + item.reason + ", textChars=" + item.text.Length + ").");
                }
                else
                {
                    this.chatForceTranslateRequested.Remove(item.msgId);
                    this.ChatTranslateVerbose("postcard send FAILED for msg " + item.msgId + " (see log).");
                }
            }
        }

        // Main-thread handler (engine drain). Reads ONLY scalars out of the snapshot: the mailId
        // bytes and the ErrorCode. The translatedContent pointer is an unpinned mono object — never
        // dereference it here.
        private void OnPostcardTranslateResultEvent(GameEventSnapshot snap)
        {
            try
            {
                if (!this.chatTranslatePostcardBypass || !this.postcardPendingActive)
                {
                    return;
                }

                if (!this.EnsurePostcardResultOffsets())
                {
                    return;
                }

                // Confirm this result is for the postcard we borrowed (the user's own postcard
                // translations, if any, must not be swallowed as ours).
                for (int i = 0; i < 16; i++)
                {
                    if (snap.ReadByte(this.postcardResultMailIdOffset + i) != this.postcardMailId[i])
                    {
                        return;
                    }
                }

                int errorCode = snap.ReadInt32(this.postcardResultErrorOffset);
                if (errorCode == 0)
                {
                    this.chatForceTranslateSucceededCount++;
                    this.ChatTranslateLog("★ Postcard endpoint ACCEPTED the injected text for msg "
                        + this.postcardPendingMsgId + " " + ChatTranslateTextForLog(this.postcardPendingText)
                        + " — server returned Success (translation delivered to the mail UI).");
                }
                else
                {
                    this.ChatTranslateLog("Postcard endpoint REJECTED msg " + this.postcardPendingMsgId
                        + " " + ChatTranslateTextForLog(this.postcardPendingText)
                        + " — errorCode=" + errorCode
                        + (errorCode == 4207 ? " (TranslateContentInvalid — server cross-checks the postcard's own content)" : string.Empty)
                        + (errorCode == 4210 ? " (TranslateLimit — weekly quota)" : string.Empty));
                }

                this.postcardPendingActive = false;
                this.postcardPendingText = null;
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("Postcard result handler exception: " + ex.Message);
            }
        }

        private bool EnsurePostcardResultOffsets()
        {
            if (this.postcardResultMailIdOffset >= 0 && this.postcardResultErrorOffset >= 0)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoFieldGetOffset == null)
                {
                    return false;
                }

                IntPtr eventClass = this.FindAuraMonoClassByFullName(PostcardResultEventName);
                if (eventClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr mailIdField = this.FindAuraMonoFieldOnHierarchy(eventClass, "mailId");
                IntPtr errorField = this.FindAuraMonoFieldOnHierarchy(eventClass, "errorCode");
                if (mailIdField == IntPtr.Zero || errorField == IntPtr.Zero)
                {
                    return false;
                }

                int header = 2 * IntPtr.Size;
                this.postcardResultMailIdOffset = (int)auraMonoFieldGetOffset(mailIdField) - header;
                this.postcardResultErrorOffset = (int)auraMonoFieldGetOffset(errorField) - header;
                if (this.postcardResultMailIdOffset < 0 || this.postcardResultErrorOffset < 0)
                {
                    this.postcardResultMailIdOffset = -1;
                    this.postcardResultErrorOffset = -1;
                    return false;
                }

                if (!this.postcardOffsetsLogged)
                {
                    this.postcardOffsetsLogged = true;
                    this.ChatTranslateVerbose("Postcard result offsets: mailId=" + this.postcardResultMailIdOffset
                        + " errorCode=" + this.postcardResultErrorOffset + ".");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Resolve ONE owned postcard's MailId (Guid, 16 raw bytes) from MailProtocolManager.GetMails().
        private unsafe bool EnsurePostcardMailId(float now)
        {
            if (this.postcardMailIdValid)
            {
                return true;
            }

            if (now < this.postcardNextMailIdResolveAt)
            {
                return false;
            }
            this.postcardNextMailIdResolveAt = now + 5f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
                {
                    return false;
                }

                if (this.postcardGetMailsMethodPtr == IntPtr.Zero)
                {
                    IntPtr mailMgrClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager");
                    if (mailMgrClass == IntPtr.Zero)
                    {
                        this.PostcardLogUnavailableOnce("MailProtocolManager class unavailable.");
                        return false;
                    }

                    this.postcardGetMailsMethodPtr = this.FindAuraMonoMethodOnHierarchy(mailMgrClass, "GetMails", 0);
                    if (this.postcardGetMailsMethodPtr == IntPtr.Zero)
                    {
                        this.PostcardLogUnavailableOnce("MailProtocolManager.GetMails() unavailable.");
                        return false;
                    }
                }

                if (this.postcardClassPtr == IntPtr.Zero)
                {
                    this.postcardClassPtr = this.FindAuraMonoClassByFullName("Sazabi.World.Shared.PostCard");
                    if (this.postcardClassPtr == IntPtr.Zero)
                    {
                        this.PostcardLogUnavailableOnce("PostCard class unavailable.");
                        return false;
                    }
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr listObj = auraMonoRuntimeInvoke(this.postcardGetMailsMethodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    this.ChatTranslateLog("Postcard bypass blocked: MailProtocolManager.GetMails() returned "
                        + (exc != IntPtr.Zero ? "an exception" : "null") + " — mail service not ready.");
                    return false;
                }

                // TryEnumerateAuraMonoCollectionItems returns false for an EMPTY collection too
                // (it only reports true once it collected something), so read Count first —
                // otherwise "mailbox has 0 mails" is indistinguishable from a real failure.
                int mailCount = this.GetPostcardMailListCount(listObj);
                if (mailCount == 0)
                {
                    this.postcardEmptyMailboxStrikes++;
                    if (this.postcardEmptyMailboxStrikes == 1)
                    {
                        this.ChatTranslateLog("Postcard bypass: mailbox list is EMPTY (0 mails loaded) — requesting it from the server…");
                        this.TryRequestPostcardMailList();
                    }
                    else if (this.postcardEmptyMailboxStrikes >= 4)
                    {
                        this.DisarmPostcardRoute("mailbox stayed empty (0 mails) — the postcard route needs a postcard in your mailbox");
                    }

                    return false;
                }

                this.postcardEmptyMailboxStrikes = 0;

                List<IntPtr> items = new List<IntPtr>();
                List<uint> pins = new List<uint>();
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                    {
                        this.ChatTranslateLog("Postcard bypass blocked: could not enumerate the mail list ("
                            + mailCount + " mails reported by Count).");
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr mail = items[i];
                        if (mail == IntPtr.Zero || auraMonoObjectGetClass(mail) != this.postcardClassPtr)
                        {
                            continue;
                        }

                        if (this.postcardGetIdMethodPtr == IntPtr.Zero)
                        {
                            this.postcardGetIdMethodPtr = this.FindAuraMonoMethodOnHierarchy(this.postcardClassPtr, "get_Id", 0);
                            if (this.postcardGetIdMethodPtr == IntPtr.Zero)
                            {
                                this.PostcardLogUnavailableOnce("PostCard.get_Id unavailable.");
                                return false;
                            }
                        }

                        IntPtr idExc = IntPtr.Zero;
                        IntPtr boxedGuid = auraMonoRuntimeInvoke(this.postcardGetIdMethodPtr, mail, IntPtr.Zero, ref idExc);
                        if (idExc != IntPtr.Zero || boxedGuid == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr raw = auraMonoObjectUnbox(boxedGuid);
                        if (raw == IntPtr.Zero)
                        {
                            continue;
                        }

                        Marshal.Copy(raw, this.postcardMailId, 0, 16);
                        this.postcardMailIdValid = true;
                        this.postcardUnavailableLogged = false;
                        this.ChatTranslateLog("Postcard MailId resolved (" + BytesToHex(this.postcardMailId) + ") — bypass armed.");
                        return true;
                    }

                    // Mails ARE loaded, there is just no postcard among them — a standing condition,
                    // not a transient one, so stop retrying instead of repeating this every scan.
                    this.DisarmPostcardRoute("no postcard among " + items.Count
                        + " mails — the route borrows a postcard's MailId, so you need one in your mailbox");
                    return false;
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("Postcard MailId resolve exception: " + ex.Message);
                return false;
            }
        }

        // List<MailBase>.Count without enumerating (the enumerate helper can't tell empty from
        // failed). Pins the list for the invoke — get_Count boxes its return, which can trigger a
        // moving-GC pass that would relocate an unpinned collection.
        private int GetPostcardMailListCount(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return -1;
            }

            uint pin = AuraMonoPinNew(listObj);
            try
            {
                IntPtr listClass = auraMonoObjectGetClass(listObj);
                if (listClass == IntPtr.Zero)
                {
                    return -1;
                }

                IntPtr getCount = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0);
                if (getCount == IntPtr.Zero)
                {
                    return -1;
                }

                return this.GetAuraMonoIntCount(listObj, getCount);
            }
            catch
            {
                return -1;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Ask the server to send the mail list (what the game does on level-ready / mail panel open).
        // Throttled hard — this is a real network command.
        private unsafe void TryRequestPostcardMailList()
        {
            float now = Time.unscaledTime;
            if (now < this.postcardNextMailRequestAt)
            {
                return;
            }
            this.postcardNextMailRequestAt = now + 30f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return;
                }

                if (this.postcardRequestMailsMethodPtr == IntPtr.Zero)
                {
                    IntPtr mailMgrClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager");
                    if (mailMgrClass == IntPtr.Zero)
                    {
                        return;
                    }

                    this.postcardRequestMailsMethodPtr = this.FindAuraMonoMethodOnHierarchy(mailMgrClass, "RequestMails", 2);
                    if (this.postcardRequestMailsMethodPtr == IntPtr.Zero)
                    {
                        return;
                    }
                }

                int offset = 0;
                int count = 0; // 0/0 = the game's own "give me everything" call
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&offset);
                args[1] = (IntPtr)(&count);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.postcardRequestMailsMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                this.ChatTranslateVerbose(exc == IntPtr.Zero
                    ? "Requested mail list from server (GetMailsCommand)."
                    : "RequestMails raised a mono exception.");
            }
            catch (Exception ex)
            {
                this.ChatTranslateVerbose("RequestMails exception: " + ex.Message);
            }
        }

        private unsafe bool TrySendPostcardTranslate(string content)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoStringNew == null
                    || this.auraMonoRootDomain == IntPtr.Zero || !this.postcardMailIdValid)
                {
                    return false;
                }

                if (this.postcardSendMethodPtr == IntPtr.Zero)
                {
                    IntPtr mailMgrClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Mail.MailProtocolManager");
                    if (mailMgrClass == IntPtr.Zero)
                    {
                        return false;
                    }

                    this.postcardSendMethodPtr = this.FindAuraMonoMethodOnHierarchy(mailMgrClass, "RequestTranslatePostCard", 2);
                    if (this.postcardSendMethodPtr == IntPtr.Zero)
                    {
                        this.PostcardLogUnavailableOnce("MailProtocolManager.RequestTranslatePostCard(2) unavailable.");
                        return false;
                    }
                }

                IntPtr contentObj = auraMonoStringNew(this.auraMonoRootDomain, content ?? string.Empty);
                fixed (byte* mailIdPtr = this.postcardMailId)
                {
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)mailIdPtr; // Guid by value -> pointer to the 16 raw bytes
                    args[1] = contentObj;
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(this.postcardSendMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        this.ChatTranslateLog("RequestTranslatePostCard raised a mono exception.");
                        return false;
                    }
                }

                this.postcardUnavailableLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                this.ChatTranslateLog("Postcard send exception: " + ex.Message);
                return false;
            }
        }

        private void PostcardLogUnavailableOnce(string reason)
        {
            if (this.postcardUnavailableLogged)
            {
                return;
            }

            this.postcardUnavailableLogged = true;
            this.ChatTranslateLog(reason);
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            char[] c = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                c[i * 2] = hex[bytes[i] >> 4];
                c[i * 2 + 1] = hex[bytes[i] & 0xF];
            }

            return new string(c);
        }
    }
}
