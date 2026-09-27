using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    // ============================================================================================
    // CONTROLLER SHORT NAME — resolved by NAME at runtime, never hard-coded.
    //
    // ── WHY THIS EXISTS ─────────────────────────────────────────────────────────────────────────
    // An animation controller is picked by a packed id, (charType << 24) | (poseType << 14) |
    // (override << 9) | shortName, and `shortName` is a ControllerShortName enum value. That enum
    // declares ONE explicit value and lets every other name take its position as its number — so
    // when an update inserts entries anywhere above a name, that name's number changes.
    //
    // It has moved twice already, and both times it broke the swings SILENTLY: the cast is still
    // accepted (ActionErrorCode 0) and the animator is simply asked for a family that no longer
    // means what it did, so nothing renders and nothing is logged.
    //
    //     lumbering / mining:  159/160  ->  163/164 (2026-08-20)  ->  200/201 (2026-09-24)
    //
    // By the second shift, 163 had become `switchpassengerloongboat` — the axe rows were asking the
    // animator to ride a dragon boat.
    //
    // ── THE FIX ─────────────────────────────────────────────────────────────────────────────────
    // The game ships its own name->enum converter and it is not generic at the call site:
    // `AnimEnumConvert.ToControllerShortName(string)` (and the pose/override siblings) wrap
    // Enum.TryParse and are plain static one-argument methods. Resolving "lumbering" through the
    // LOADED enum costs one invoke, is cached for the session, and cannot go stale — a name is
    // stable across updates in a way its ordinal is not.
    //
    // ── THE ONE TRAP ────────────────────────────────────────────────────────────────────────────
    // Each converter FALLS BACK rather than failing: an unknown short name returns `pose` (5), an
    // unknown override returns `empty` (5), an unknown pose returns `stand` (1). So a name the game
    // has actually dropped would come back as a plausible-looking number. Every lookup below is
    // therefore checked against that fallback and, when it matches a name that is not the fallback
    // itself, treated as a miss and logged — which is exactly the signal that went missing both
    // times the ordinals moved.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string AnimEnumConvertClassName = "XDTLevelAndEntity.ResHandle.AnimationRes.AnimEnumConvert";

        // ControllerShortName.pose — what ToControllerShortName returns for an unknown name.
        private const int ControllerShortNameFallback = 5;

        private readonly Dictionary<string, int> controllerShortNameCache = new Dictionary<string, int>(StringComparer.Ordinal);
        private IntPtr controllerShortNameMethod;
        private float controllerShortNameRetryAt;

        /// Ordinal of a ControllerShortName, resolved through the game's own converter and cached.
        /// Returns `fallback` when the lookup cannot be made — a stale number still renders the
        /// wrong clip, but it never renders less than the hard-coded build did.
        internal int ResolveControllerShortName(string name, int fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            if (this.controllerShortNameCache.TryGetValue(name, out int cached))
            {
                return cached;
            }

            if (!this.TryResolveControllerShortName(name, out int value))
            {
                return fallback;
            }

            // Cached only on success: a failure here is usually "not ready yet" (no world, AuraMono
            // still warming up), and caching it would make the miss permanent for the session.
            this.controllerShortNameCache[name] = value;
            if (value != fallback)
            {
                ModLogger.Msg("[Controller] " + name + " = " + value
                              + (fallback > 0 ? (" (built-in fallback was " + fallback + ")") : string.Empty));
            }

            return value;
        }

        private unsafe bool TryResolveControllerShortName(string name, out int value)
        {
            value = 0;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoStringNew == null)
            {
                return false;
            }

            if (this.controllerShortNameMethod == IntPtr.Zero)
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now < this.controllerShortNameRetryAt)
                {
                    return false;
                }

                this.controllerShortNameRetryAt = now + 2f;
                IntPtr klass = this.FindAuraMonoClassByFullName(AnimEnumConvertClassName);
                this.controllerShortNameMethod = klass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(klass, "ToControllerShortName", 1);
                if (this.controllerShortNameMethod == IntPtr.Zero)
                {
                    ModLogger.Msg("[Controller] AnimEnumConvert.ToControllerShortName unresolved — "
                                  + "falling back to built-in ordinals.");
                    return false;
                }
            }

            IntPtr monoString = auraMonoStringNew(this.auraMonoRootDomain, name);
            if (monoString == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinningAvailable ? AuraMonoPinNew(monoString) : 0u;
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = monoString;
                IntPtr boxed = auraMonoRuntimeInvoke(this.controllerShortNameMethod, IntPtr.Zero,
                                                     (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return false;
                }

                int resolved = this.ReadAuraMonoBoxedInt32(boxed);

                // The converter answers `pose` for anything it does not know, so a non-pose name
                // coming back as pose means the game dropped or renamed it. Say so — this is the
                // failure that was silent both times the ordinals moved.
                if (resolved == ControllerShortNameFallback
                    && !string.Equals(name, "pose", StringComparison.Ordinal))
                {
                    ModLogger.Msg("[Controller] the game does not know ControllerShortName '"
                                  + name + "' any more — the clip it names is gone or renamed.");
                    return false;
                }

                value = resolved;
                return true;
            }
            finally
            {
                if (pin != 0u)
                {
                    FreeAuraMonoPins(new List<uint> { pin });
                }
            }
        }
    }
}
