using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    // ============================================================================================
    // Shared AuraMono `WebRequestUtility.SendCommand<T>` — the first generic one in this mod.
    //
    // Seven features each cached their own open-method pointer and re-implemented the same resolve →
    // inflate → allocate → set fields → unbox → invoke sequence, and five separately defined
    // `ChannelType.Reliable = 1`. This is that sequence written once.
    //
    // Migrated: CraftDirectSend (MakeItem + occupy release), QuestAssistant (EnterDialogNode +
    // PlayerEnterArea), CarpetStamp, DailyClaims (all eight claim commands), StealthBlock
    // (block + unblock). All five `ChannelType.Reliable = 1` constants collapsed onto
    // AuraChannelReliable below.
    //
    // NOT migrated, and not migratable as-is — each needs this helper extended first:
    //   * `cachedInstantCatchAuraSendCommandOpenMethod` (fishing buoy) and
    //     `vehicleBypassSendCommandMethod` set Vector3 fields and branch on SendCommand's return
    //     code. This maps fields by runtime type and discards the return value, so both would break
    //     silently — a Vector3 is refused outright, and a dropped send code turns a rejected send
    //     into a reported success.
    //   * `snowCachedSendCommandMethod` is a different shape entirely: managed
    //     MethodInfo.MakeGenericMethod, not AuraMono. Moving it is a rewrite, not a migration.
    //
    // ⚠️ IT SENDS REAL COMMANDS TO A LIVE SERVER. A wrong field value is not a wrong answer, it is a
    // changed account. Everything reachable through it is unsafe-tier by construction.
    //
    // The per-feature copies are being migrated onto this one at a time, each tested in-game before
    // the next is started — they touch server-authoritative paths, so a batch rewrite would be a
    // batch of untested sends. New work should start here rather than add an eighth copy.
    //
    // This file is deliberately NOT gated on FEATURE_MCP (it was, while the MCP bridge was its only
    // caller). Its command-type resolver moved out of the gate with it —
    // `FindAuraMonoClassAnySpelling`, HeartopiaComplete.AuraClassResolve.cs.
    //
    // ── THE FOUR THINGS THAT MAKE IT CRASH IF DONE WRONG ────────────────────────────────────────
    //  1. The command is a VALUE TYPE. SendCommand<T> takes T by value, so the boxed object must be
    //     unboxed before it becomes argument 0 — passing the box corrupts the call.
    //  2. The generic must be inflated per command type and the arity re-checked. A mismatched
    //     `method_inst` AVs the process on invoke rather than throwing (AGENTS.md §12).
    //  3. The box must be PINNED while fields are set: mono_string_new allocates, and an allocation
    //     between obtaining the pointer and writing through it can move the object.
    //  4. mono_field_set_value takes the ADDRESS of a value-type value but the POINTER ITSELF for a
    //     reference (string) field. Getting that backwards writes a pointer-to-pointer into the
    //     field, which corrupts silently rather than failing.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal const int AuraChannelReliable = 1;   // ChannelType.Reliable

        private IntPtr auraSendCommandWebRequestClass = IntPtr.Zero;
        private IntPtr auraSendCommandOpenMethod = IntPtr.Zero;
        private readonly Dictionary<string, IntPtr> auraSendCommandClassByName =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        private readonly Dictionary<IntPtr, IntPtr> auraSendCommandInflatedByClass =
            new Dictionary<IntPtr, IntPtr>();

        private bool EnsureAuraSendCommandOpenMethod()
        {
            if (this.auraSendCommandOpenMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.auraSendCommandWebRequestClass == IntPtr.Zero)
            {
                // Ends in an exhaustive image sweep — the hint-driven resolver alone has measured
                // blind spots, which is exactly why the per-feature callers all hand-roll an
                // across-assemblies fallback after it.
                this.auraSendCommandWebRequestClass =
                    this.FindAuraMonoClassAnySpelling("XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            }

            if (this.auraSendCommandWebRequestClass == IntPtr.Zero)
            {
                return false;
            }

            this.auraSendCommandOpenMethod =
                this.FindAuraMonoMethodOnHierarchy(this.auraSendCommandWebRequestClass, "SendCommand", 3);
            return this.auraSendCommandOpenMethod != IntPtr.Zero;
        }

        // `fields` values are mapped by runtime type: int / uint / float / bool / string. Anything
        // else is rejected by name rather than coerced, because a silently truncated or reinterpreted
        // field is the worst possible outcome on a server-authoritative path.
        // Everything except the invoke: resolve, inflate, arity-check, allocate, pin, set every
        // field, unbox. If this succeeds, the only thing left in a real send is handing the payload
        // to the server.
        //
        // It exists because the first question about a command is "did I get the type and field
        // names right", and on this path the usual way to find out — try it — costs a real mutation
        // of a live account. There is no undo, so there has to be a way to ask without sending.
        internal bool TryValidateAuraCommand(string commandFullName, IDictionary<string, object> fields,
                                             out string status)
        {
            return this.TryAuraSendCommandCore(commandFullName, fields, AuraChannelReliable, true,
                                               send: false, out status);
        }

        internal bool TryAuraSendCommand(string commandFullName, IDictionary<string, object> fields,
                                         int channelType, bool needAuthed, out string status)
        {
            return this.TryAuraSendCommandCore(commandFullName, fields, channelType, needAuthed,
                                               send: true, out status);
        }

        private unsafe bool TryAuraSendCommandCore(string commandFullName, IDictionary<string, object> fields,
                                                   int channelType, bool needAuthed, bool send,
                                                   out string status)
        {
            status = "not attempted";

            if (string.IsNullOrWhiteSpace(commandFullName))
            {
                status = "command type name is required";
                return false;
            }

            // Re-attached on every send, not just on the resolving call: the open method is cached
            // process-wide, so gating the attach on "first resolve" would leave any later caller on
            // an unattached thread. Both calls are idempotent and cached.
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || !this.EnsureAuraSendCommandOpenMethod()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectNew == null
                || auraMonoObjectUnbox == null
                || auraMonoFieldSetValue == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                status = "SendCommand unavailable (AuraMono not ready, or WebRequestUtility unresolved)";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                // Fail closed: without pinning, setting a string field can move the command out from
                // under the pointer being written through.
                status = "pinning unavailable — refusing to build a command that could be moved mid-write";
                return false;
            }

            if (!this.auraSendCommandClassByName.TryGetValue(commandFullName, out IntPtr commandClass)
                || commandClass == IntPtr.Zero)
            {
                commandClass = this.FindAuraMonoClassAnySpelling(commandFullName);
                if (commandClass == IntPtr.Zero)
                {
                    status = "command type not found: " + commandFullName;
                    return false;
                }

                this.auraSendCommandClassByName[commandFullName] = commandClass;
            }

            if (!this.auraSendCommandInflatedByClass.TryGetValue(commandClass, out IntPtr inflated)
                || inflated == IntPtr.Zero)
            {
                if (!this.TryInstantCatchInflateAuraSendCommand(this.auraSendCommandOpenMethod, commandClass,
                        out inflated) || inflated == IntPtr.Zero)
                {
                    status = "SendCommand could not be inflated for " + commandFullName;
                    return false;
                }

                // Re-checked even though the inflater already validated: a wrong method_inst faults
                // the process instead of throwing, so this is cheap insurance on a fatal mistake.
                if (!AuraMonoMethodParamCountIs(inflated, 3))
                {
                    status = "inflated SendCommand has the wrong arity for " + commandFullName;
                    return false;
                }

                this.auraSendCommandInflatedByClass[commandClass] = inflated;
            }

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, commandClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "could not allocate " + commandFullName;
                return false;
            }

            uint pin = AuraMonoPinNew(cmdObj);
            if (pin == 0U)
            {
                status = "could not pin " + commandFullName;
                return false;
            }

            try
            {
                if (fields != null)
                {
                    foreach (KeyValuePair<string, object> pair in fields)
                    {
                        if (!this.TrySetAuraCommandField(commandClass, cmdObj, pair.Key, pair.Value, out status))
                        {
                            return false;
                        }
                    }
                }

                // SendCommand<T> takes T by value — hand it the payload, not the box.
                IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
                if (cmdPtr == IntPtr.Zero)
                {
                    status = "could not unbox " + commandFullName;
                    return false;
                }

                if (!send)
                {
                    // Stop one step short, deliberately: everything that could be wrong about the
                    // command has already been exercised, and nothing has left the process.
                    status = "validated " + commandFullName + " ("
                        + (fields == null ? 0 : fields.Count) + " field(s) set, not sent)";
                    return true;
                }

                int authed = needAuthed ? 1 : 0;
                int channel = channelType;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = cmdPtr;
                args[1] = (IntPtr)(&authed);
                args[2] = (IntPtr)(&channel);

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(inflated, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    // Read the message here, before anything else allocates — same best-effort read,
                    // in the same position, as the per-feature copies. "It threw" on its own is not
                    // something a caller can act on when the send mutates a live account.
                    status = commandFullName + " threw on send: "
                        + (this.TryGetMonoStringMember(exc, "Message", out string excMessage)
                                && !string.IsNullOrWhiteSpace(excMessage)
                            ? excMessage
                            : "0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                status = "sent " + commandFullName;
                return true;
            }
            finally
            {
                FreeAuraMonoPins(new List<uint> { pin });
            }
        }

        private unsafe bool TrySetAuraCommandField(IntPtr commandClass, IntPtr cmdObj, string fieldName,
                                                   object value, out string status)
        {
            status = null;

            IntPtr field = this.FindAuraMonoFieldOnHierarchy(commandClass, fieldName);
            if (field == IntPtr.Zero)
            {
                status = "no such field on the command: " + fieldName;
                return false;
            }

            switch (value)
            {
                case int i:
                    auraMonoFieldSetValue(cmdObj, field, (IntPtr)(&i));
                    return true;

                case uint u:
                    auraMonoFieldSetValue(cmdObj, field, (IntPtr)(&u));
                    return true;

                case long l:
                    auraMonoFieldSetValue(cmdObj, field, (IntPtr)(&l));
                    return true;

                case float f:
                    auraMonoFieldSetValue(cmdObj, field, (IntPtr)(&f));
                    return true;

                case bool b:
                {
                    // A mono bool is one byte; writing an int would spill into the next field.
                    byte one = b ? (byte)1 : (byte)0;
                    auraMonoFieldSetValue(cmdObj, field, (IntPtr)(&one));
                    return true;
                }

                case string s:
                {
                    if (auraMonoStringNew == null)
                    {
                        status = "string fields unavailable (mono_string_new unbound)";
                        return false;
                    }

                    IntPtr strObj = auraMonoStringNew(this.auraMonoRootDomain, s);
                    if (strObj == IntPtr.Zero)
                    {
                        status = "could not create the string for " + fieldName;
                        return false;
                    }

                    // Reference field: mono_field_set_value wants the OBJECT POINTER, not its
                    // address. The value-type cases above are the opposite, and swapping them writes
                    // garbage that corrupts silently instead of failing.
                    auraMonoFieldSetValue(cmdObj, field, strObj);
                    return true;
                }

                case null:
                    status = "field " + fieldName + " was given null — omit it instead";
                    return false;

                default:
                    // Deliberately not coerced. On a path that mutates a live account, an
                    // unrecognised type must stop the send, not be guessed at.
                    status = "unsupported field type for " + fieldName + ": " + value.GetType().Name
                        + " (int, uint, long, float, bool, string)";
                    return false;
            }
        }
    }
}
