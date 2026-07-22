using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Postcard-endpoint translation bypass (experimental).
    //
    // The chat translator gates on langCode (sender's declared UI language) both client- and
    // server-side, so a foreign message typed on an English UI is refused (NoNeedToTranslate).
    // The POSTCARD translator (`TranslatePostCardCommand{MailId,Content}` →
    // `TranslatePostCardResultEvent{TranslatedContent}`) instead takes arbitrary Content and has
    // NO source-langCode gate (only the weekly-char quota) — postcard content is free text the
    // server must language-DETECT, so it should translate text the chat path refuses.
    //
    // Flow: route a blocked chat message's text through RequestTranslatePostCard(<any owned postcard
    // MailId>, <chat text>), serialized one at a time, and read the returned TranslatedContent.
    //
    // The result string can't be read from the deferred event snapshot (unpinned mono pointer), so
    // this installs its OWN NativeDetour on EventCenter.DispatchEvent<PostCardTranslateResultEvent>
    // and reads the string SYNCHRONOUSLY in the detour body — mono_string_to_utf8 allocates only a
    // native buffer (no managed GC), so it's safe on the boundary. Separate from the shared
    // event-hook engine (whose bodies never call Mono) so that invariant is untouched.
    public partial class HeartopiaComplete
    {
        private const string PostcardResultEventName = "XDTDataAndProtocol.Events.PostCardTranslateResultEvent";

        // Instance state (toggle + serialized send queue + mailId cache + install/backoff).
        private bool chatTranslatePostcardBypass;
        private readonly Queue<(ulong msgId, string text, string reason)> postcardTranslateQueue = new Queue<(ulong, string, string)>();
        private readonly byte[] postcardMailId = new byte[16];
        private bool postcardMailIdValid;
        private float postcardNextMailIdResolveAt = -999f;
        private bool postcardDetourInstallAttempted;
        private bool postcardDetourInstalled;
        private MonoMod.RuntimeDetour.NativeDetour postcardResultDetour;
        private Delegate postcardResultKeepAlive;
        private IntPtr postcardSendMethodPtr;
        private IntPtr postcardGetMailsMethodPtr;
        private IntPtr postcardGetIdMethodPtr;
        private IntPtr postcardClassPtr;
        private bool postcardUnavailableLogged;

        // Single outstanding request (serialized so the result — which carries only the shared
        // MailId, not a msgId — correlates unambiguously to what we sent).
        private bool postcardPendingActive;
        private ulong postcardPendingMsgId;
        private string postcardPendingText;
        private float postcardPendingSentAt;

        // Static state consumed by the native detour body (no closures/instance state on the boundary).
        private static DispatchEventHookDelegate postcardResultTrampoline;
        private static readonly byte[] postcardFilterMailId = new byte[16];
        private static bool postcardFilterValid;
        private static int postcardResultMailIdOffset = -1;
        private static int postcardResultContentOffset = -1;
        private static int postcardResultErrorOffset = -1;
        // Single capture slot (body writes, main-thread pump reads+clears; both on the Unity thread).
        private static bool postcardCaptureReady;
        private static int postcardCaptureErrorCode;
        private static string postcardCaptureText;

        internal void ResetChatTranslatePostcardWorldState()
        {
            this.postcardTranslateQueue.Clear();
            this.postcardMailIdValid = false;
            this.postcardNextMailIdResolveAt = -999f;
            this.postcardSendMethodPtr = IntPtr.Zero;
            this.postcardGetMailsMethodPtr = IntPtr.Zero;
            this.postcardGetIdMethodPtr = IntPtr.Zero;
            this.postcardClassPtr = IntPtr.Zero;
            this.postcardPendingActive = false;
            this.postcardPendingText = null;
            postcardFilterValid = false;
            postcardCaptureReady = false;
            postcardCaptureText = null;
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

        private void UpdateChatTranslatePostcardBypass(float now)
        {
            if (!this.chatTranslatePostcardBypass)
            {
                return;
            }

            this.EnsurePostcardResultDetour();

            // Drain a captured result and correlate it to the single outstanding request.
            if (postcardCaptureReady)
            {
                int err = postcardCaptureErrorCode;
                string translated = postcardCaptureText;
                postcardCaptureReady = false;
                postcardCaptureText = null;

                if (this.postcardPendingActive)
                {
                    if (err == 0 && !string.IsNullOrEmpty(translated))
                    {
                        this.chatForceTranslateSucceededCount++;
                        this.ChatTranslateLog("Postcard translated msg " + this.postcardPendingMsgId
                            + ": " + ChatTranslateTextForLog(this.postcardPendingText)
                            + " -> " + ChatTranslateTextForLog(translated));
                    }
                    else if (err != 0)
                    {
                        this.ChatTranslateLog("Postcard translate error " + err + " for msg " + this.postcardPendingMsgId
                            + " text=" + ChatTranslateTextForLog(this.postcardPendingText)
                            + (err == 4207 ? " (server rejected the injected content — TranslateContentInvalid)" : string.Empty));
                    }

                    this.postcardPendingActive = false;
                    this.postcardPendingText = null;
                }
            }

            // Timeout a stuck request so the queue keeps moving.
            if (this.postcardPendingActive && now - this.postcardPendingSentAt > 12f)
            {
                this.ChatTranslateLog("Postcard translate timed out for msg " + this.postcardPendingMsgId + ".");
                this.postcardPendingActive = false;
                this.postcardPendingText = null;
            }

            // Pump: one outstanding at a time.
            if (!this.postcardPendingActive && this.postcardTranslateQueue.Count > 0)
            {
                if (!this.postcardDetourInstalled)
                {
                    return; // wait for the result detour before sending, or results are lost
                }

                if (!this.EnsurePostcardMailId(now))
                {
                    return; // need a postcard MailId first
                }

                (ulong msgId, string text, string reason) item = this.postcardTranslateQueue.Dequeue();
                if (this.TrySendPostcardTranslate(item.text))
                {
                    this.postcardPendingActive = true;
                    this.postcardPendingMsgId = item.msgId;
                    this.postcardPendingText = item.text;
                    this.postcardPendingSentAt = now;
                    this.chatForceTranslateSentCount++;
                    this.ChatTranslateVerbose("postcard request SENT for msg " + item.msgId + " (" + item.reason + ", textChars=" + item.text.Length + ").");
                }
                else
                {
                    this.chatForceTranslateRequested.Remove(item.msgId);
                    this.ChatTranslateVerbose("postcard send FAILED for msg " + item.msgId + " (see log).");
                }
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
                        Buffer.BlockCopy(this.postcardMailId, 0, postcardFilterMailId, 0, 16);
                        postcardFilterValid = true;
                        this.postcardMailIdValid = true;
                        this.postcardUnavailableLogged = false;
                        this.ChatTranslateLog("Postcard MailId resolved (" + BytesToHex(this.postcardMailId) + ") — bypass armed.");
                        return true;
                    }

                    this.PostcardLogUnavailableOnce("No postcard found in mailbox — receive a postcard to arm the bypass.");
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

        private void EnsurePostcardResultDetour()
        {
            if (this.postcardDetourInstalled || this.postcardDetourInstallAttempted)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // retry next frame
                }

                IntPtr eventCenterClass = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
                IntPtr eventClass = this.FindAuraMonoClassByFullName(PostcardResultEventName);
                if (eventCenterClass == IntPtr.Zero || eventClass == IntPtr.Zero)
                {
                    return; // images not loaded yet
                }

                if (!this.ResolvePostcardResultOffsets(eventClass))
                {
                    return;
                }

                this.postcardDetourInstallAttempted = true;

                IntPtr openDispatch = this.FindAuraMonoMethodOnHierarchy(eventCenterClass, "DispatchEvent", 1);
                if (openDispatch == IntPtr.Zero || !this.TryInflateDispatchForEvent(openDispatch, eventClass, 1, out IntPtr inflated))
                {
                    this.PostcardLogUnavailableOnce("DispatchEvent<PostCardTranslateResultEvent> inflate failed.");
                    return;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                EventHookCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<EventHookCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                IntPtr nativePtr = compile != null ? compile(inflated) : IntPtr.Zero;
                if (nativePtr == IntPtr.Zero)
                {
                    this.PostcardLogUnavailableOnce("mono_compile_method null for postcard result dispatch.");
                    return;
                }

                DispatchEventHookDelegate body = PostcardResultBody;
                this.postcardResultKeepAlive = body;
                this.postcardResultDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, body);
                DispatchEventHookDelegate tramp = this.postcardResultDetour.GenerateTrampoline<DispatchEventHookDelegate>();
                if (tramp == null)
                {
                    try { this.postcardResultDetour.Undo(); } catch { }
                    this.postcardResultDetour = null;
                    this.postcardResultKeepAlive = null;
                    this.PostcardLogUnavailableOnce("postcard result trampoline unavailable; detour reverted.");
                    return;
                }

                postcardResultTrampoline = tramp;
                this.postcardDetourInstalled = true;
                this.ChatTranslateLog("Postcard result detour installed @0x" + nativePtr.ToInt64().ToString("X") + ".");
            }
            catch (Exception ex)
            {
                this.postcardDetourInstallAttempted = true;
                this.ChatTranslateLog("Postcard result detour install failed: " + ex.Message);
            }
        }

        private bool ResolvePostcardResultOffsets(IntPtr eventClass)
        {
            if (postcardResultMailIdOffset >= 0 && postcardResultContentOffset >= 0 && postcardResultErrorOffset >= 0)
            {
                return true;
            }

            if (auraMonoFieldGetOffset == null || eventClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr mailIdField = this.FindAuraMonoFieldOnHierarchy(eventClass, "mailId");
            IntPtr contentField = this.FindAuraMonoFieldOnHierarchy(eventClass, "translatedContent");
            IntPtr errorField = this.FindAuraMonoFieldOnHierarchy(eventClass, "errorCode");
            if (mailIdField == IntPtr.Zero || contentField == IntPtr.Zero || errorField == IntPtr.Zero)
            {
                return false;
            }

            int header = 2 * IntPtr.Size;
            postcardResultMailIdOffset = (int)auraMonoFieldGetOffset(mailIdField) - header;
            postcardResultContentOffset = (int)auraMonoFieldGetOffset(contentField) - header;
            postcardResultErrorOffset = (int)auraMonoFieldGetOffset(errorField) - header;
            return postcardResultMailIdOffset >= 0 && postcardResultContentOffset >= 0 && postcardResultErrorOffset >= 0;
        }

        // Native detour body: runs synchronously on the Unity thread during dispatch. Reads scalars +
        // (only if the mailId matches OUR request) the translated string synchronously, then forwards.
        // mono_string_to_utf8 allocates a native buffer only (no managed GC), so it is boundary-safe.
        private static void PostcardResultBody(IntPtr eventPtr)
        {
            try
            {
                CapturePostcardResult(eventPtr);
            }
            catch
            {
            }

            DispatchEventHookDelegate orig = postcardResultTrampoline;
            if (orig != null)
            {
                orig(eventPtr);
            }
        }

        private static void CapturePostcardResult(IntPtr eventPtr)
        {
            if (eventPtr == IntPtr.Zero
                || !postcardFilterValid
                || postcardResultMailIdOffset < 0
                || postcardResultContentOffset < 0
                || postcardResultErrorOffset < 0
                || postcardCaptureReady)
            {
                return; // not armed, or a prior capture not yet consumed
            }

            // Match the result's MailId to the postcard we requested against (ignore the user's own
            // real postcard translations).
            for (int i = 0; i < 16; i++)
            {
                if (Marshal.ReadByte(eventPtr, postcardResultMailIdOffset + i) != postcardFilterMailId[i])
                {
                    return;
                }
            }

            int err = Marshal.ReadInt32(eventPtr, postcardResultErrorOffset);
            string text = null;
            if (err == 0)
            {
                IntPtr strObj = Marshal.ReadIntPtr(eventPtr, postcardResultContentOffset);
                if (strObj != IntPtr.Zero && auraMonoStringToUtf8 != null)
                {
                    IntPtr utf8 = auraMonoStringToUtf8(strObj);
                    if (utf8 != IntPtr.Zero)
                    {
                        try
                        {
                            text = Marshal.PtrToStringUTF8(utf8);
                        }
                        finally
                        {
                            if (auraMonoFree != null)
                            {
                                auraMonoFree(utf8);
                            }
                        }
                    }
                }
            }

            postcardCaptureErrorCode = err;
            postcardCaptureText = text;
            postcardCaptureReady = true;
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
