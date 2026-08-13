using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AI;

namespace HeartopiaMod
{
    // NavMesh access layer for the Aura Farm "walk to the node" mode.
    //
    // WHY THIS EXISTS AT ALL: the Mono dump hides the pathfinding. ilspy-dumps/EngineWrapper's
    // NavMeshClient.AI.XDNavigationMgr only exposes LoadNavMeshDataSync/Unload, so from the Mono
    // side it looks like the game rolled its own navigation and there is nothing to call. The
    // IL2CPP dump contradicts that: gameassembly-dumps/XDNavigation/NavMeshClient.AI/
    // XDNavigationMgr.cs holds `UnityEngine.AI.NavMeshData navMeshData` +
    // `NavMeshDataInstance navMeshDataInstance` — the baked mesh is REGISTERED WITH THE STOCK
    // UnityEngine.AI SYSTEM, which is what the game's own "Track" feature (the star-effect line to
    // a map marker) draws its route along. So NavMesh.CalculatePath should see the same corridor.
    //
    // "SHOULD" IS NOT "DOES". Unity Physics raycasts are known-dead from this mod — the game runs
    // its own physics and UnityEngine.Physics answers nothing (docs: unity-physics-raycast-from-mod
    // in project memory). "The Unity API exists therefore it works" already burned this project
    // once, so nothing may be built on NavMesh until the probe below has PASSED IN-GAME. Read the
    // log lines, not the API surface.
    //
    // The probe rides the Aura Farm's own hop chokepoint (FarmTeleportTo), so every sample is a
    // real player -> real node pair on the real route the farm takes. It is capped per run and
    // costs nothing once the cap is reached.
    //
    // Everything here is main-thread Unity statics plus the existing AuraMono self-player read.
    // No mono object IntPtr is held across a frame, no coroutine, no detour.
    public partial class HeartopiaComplete
    {
        // NavMeshClient.AI.NavMeshAreaMask, for decoding NavMeshHit.mask in the probe log.
        // All = -1, Walkable = 1, NotWalkable = 2, Jump = 4, shop = 8, plot = 0x10,
        // inspiration = 0x20, Water = 0x40.
        private const int NavMeshAreaMaskWalkable = 1;
        private const int NavMeshAreaMaskNotWalkable = 2;
        private const int NavMeshAreaMaskJump = 4;
        private const int NavMeshAreaMaskShop = 8;
        private const int NavMeshAreaMaskPlot = 0x10;
        private const int NavMeshAreaMaskInspiration = 0x20;
        private const int NavMeshAreaMaskWater = 0x40;

        // How far off the mesh a position may sit and still be snapped onto it. 5 m for the probe
        // (deliberately generous — a miss at 5 m means the mesh really is not there); the walker
        // will use a tighter ~3 m.
        private const float NavMeshProbeSampleRadius = 5f;

        // Samples per farm run. Enough to cover several hops of different lengths without turning
        // the log into a wall; reset on every Start Foraging.
        private const int NavMeshProbeMaxSamples = 8;

        private int navMeshProbeSamplesLogged;

        // 0 = untried, 1 = the UnityEngine.AI interop answered, -1 = it threw / is absent (the
        // module is not loaded in this build). Latches so a dead module costs one log line, not one
        // per hop.
        private int navMeshApiAvailable;

        // Has the IL2CPP-side existence check below run yet? Separate from the tri-state above
        // because "the class exists" is not yet "a call succeeded".
        private bool navMeshClassProbed;

        // AuraMono EntityUtil.GetSelfPlayer fallback for the self position (class/method IntPtrs
        // stay raw — image lifetime, per AGENTS.md §9).
        private IntPtr navMeshEntityUtilClass;
        private IntPtr navMeshGetSelfPlayerMethod;

        // Called from ToggleAutoFarm when a run starts, so each run re-probes.
        private void ResetNavMeshProbeState()
        {
            this.navMeshProbeSamplesLogged = 0;
        }

        // Self player world position, from the MONO PLAYER ENTITY — never GetPlayer().
        // GameObject.Find("p_player_skeleton(Clone)") returns the first active match and remote
        // players share that object name, so it can silently be somebody else standing next to you
        // (project memory: player-resolve-and-input-block). A walker steered off a stranger's
        // position would wander toward them forever.
        //
        // Primary: the InteractSystem player the Aura Farm already uses for its own distance math,
        // so the walker and the gather aura agree on where "here" is.
        // Fallback: EntityUtil.GetSelfPlayer() -> position / entity.position (same shape as
        // SnowSculptureFeature's reader).
        private bool TryGetNavMeshSelfPosition(out Vector3 position, out string source)
        {
            position = Vector3.zero;
            source = "none";

            Vector3 auraPos = this.GetAuraFarmPlayerPosition();
            if (auraPos != Vector3.zero)
            {
                position = auraPos;
                source = "InteractSystem.player";
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.navMeshEntityUtilClass == IntPtr.Zero)
            {
                this.navMeshEntityUtilClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
            }

            if (this.navMeshEntityUtilClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.navMeshGetSelfPlayerMethod == IntPtr.Zero)
            {
                this.navMeshGetSelfPlayerMethod = this.FindAuraMonoMethodOnHierarchy(this.navMeshEntityUtilClass, "GetSelfPlayer", 0);
            }

            if (this.navMeshGetSelfPlayerMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(this.navMeshGetSelfPlayerMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetAuraMonoObjectPosition(playerObj, out position) && position != Vector3.zero)
            {
                source = "EntityUtil.GetSelfPlayer";
                return true;
            }

            if (this.TryGetMonoObjectMember(playerObj, "entity", out IntPtr entityObj) && entityObj != IntPtr.Zero
                && this.TryGetAuraMonoObjectPosition(entityObj, out position) && position != Vector3.zero)
            {
                source = "EntityUtil.GetSelfPlayer.entity";
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private static string FormatNavMeshVector(Vector3 v)
            => "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";

        // Decode a NavMeshHit.mask into the game's own area names so the log says "Water" instead
        // of "64" — the sea-floor question (is the underwater ground baked as Water?) is answered
        // by exactly this field.
        private static string FormatNavMeshAreaMask(int mask)
        {
            if (mask == 0)
            {
                return "none(0)";
            }

            string names = string.Empty;
            if ((mask & NavMeshAreaMaskWalkable) != 0) names += "Walkable|";
            if ((mask & NavMeshAreaMaskNotWalkable) != 0) names += "NotWalkable|";
            if ((mask & NavMeshAreaMaskJump) != 0) names += "Jump|";
            if ((mask & NavMeshAreaMaskShop) != 0) names += "shop|";
            if ((mask & NavMeshAreaMaskPlot) != 0) names += "plot|";
            if ((mask & NavMeshAreaMaskInspiration) != 0) names += "inspiration|";
            if ((mask & NavMeshAreaMaskWater) != 0) names += "Water|";

            if (names.Length == 0)
            {
                return "other(" + mask + ")";
            }

            return names.TrimEnd('|') + "(" + mask + ")";
        }

        // Pre-flight: does UnityEngine.AI actually exist in the IL2CPP domain?
        //
        // This must run BEFORE any managed `NavMesh.` / `new NavMeshPath()` expression. Touching an
        // interop type runs its static constructor, which resolves ~50 method tokens off
        // Il2CppClassPointerStore<NavMesh>.NativeClassPtr — and if the module were absent that
        // pointer is IntPtr.Zero, so those resolves dereference null NATIVELY. That is an
        // uncatchable AV, not an exception the try/catch below would see. il2cpp_class_from_name
        // asks the same question without constructing anything.
        private bool EnsureNavMeshApiAvailable()
        {
            if (this.navMeshApiAvailable < 0)
            {
                return false;
            }

            if (this.navMeshClassProbed)
            {
                return true;
            }

            this.navMeshClassProbed = true;

            IntPtr navMeshClass = IntPtr.Zero;
            IntPtr navMeshPathClass = IntPtr.Zero;
            try
            {
                // IL2CPP namespaces are the real ones — the Il2Cpp* prefix is a managed-interop
                // artifact and never appears here.
                navMeshClass = this.TryFindIl2CppClass("NavMesh", "UnityEngine.AI", string.Empty);
                navMeshPathClass = this.TryFindIl2CppClass("NavMeshPath", "UnityEngine.AI", string.Empty);
            }
            catch (Exception ex)
            {
                this.navMeshApiAvailable = -1;
                ModLogger.Msg("[NavMeshProbe] IL2CPP class lookup for UnityEngine.AI threw: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }

            if (navMeshClass == IntPtr.Zero || navMeshPathClass == IntPtr.Zero)
            {
                this.navMeshApiAvailable = -1;
                ModLogger.Msg("[NavMeshProbe] UnityEngine.AI is NOT in the IL2CPP domain (NavMesh="
                    + (navMeshClass != IntPtr.Zero) + ", NavMeshPath=" + (navMeshPathClass != IntPtr.Zero)
                    + ") — the AI module is not loaded on this build, so walking cannot use the navmesh.");
                return false;
            }

            return true;
        }

        // Snap a world position onto the mesh. Returns false when there is no mesh within
        // maxDistance — which for the walker means "do not try to walk there".
        private bool TryNavMeshSample(Vector3 position, float maxDistance, out NavMeshHit hit)
        {
            hit = default;
            if (!this.EnsureNavMeshApiAvailable())
            {
                return false;
            }

            try
            {
                bool ok = NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas);
                this.navMeshApiAvailable = 1;
                return ok;
            }
            catch (Exception ex)
            {
                this.navMeshApiAvailable = -1;
                ModLogger.Msg("[NavMeshProbe] UnityEngine.AI is not answering (SamplePosition threw) — "
                    + "walking cannot use the navmesh on this build: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // ── THE PROBE ──────────────────────────────────────────────────────
        // Fired from FarmTeleportTo for every node hop until the per-run cap. Logs, in one line per
        // stage, exactly what STEP 0 asks for: ok, status, corners.Length, corners[0], corners[last]
        // — plus the SamplePosition results, because a raw node position is usually a metre or two
        // off the mesh and CalculatePath on an off-mesh endpoint returns Invalid even where the
        // corridor exists. The snapped re-path distinguishes "no navmesh" from "endpoints off mesh".
        //
        // PASS looks like: sample hits at both ends, snapped status=PathComplete, corners.Length >= 2,
        // pathLen/straightLen a sane ratio (~1.0-1.6).
        // FAIL looks like: sample misses, or status=PathInvalid with 0 corners everywhere.
        private void ProbeNavMeshRoute(Vector3 target, string kind)
        {
            if (this.navMeshProbeSamplesLogged >= NavMeshProbeMaxSamples || !this.EnsureNavMeshApiAvailable())
            {
                return;
            }

            try
            {
                if (!this.TryGetNavMeshSelfPosition(out Vector3 playerPos, out string playerSource))
                {
                    // Not a navmesh failure — say so plainly so a self-position problem is never
                    // read as "the mesh is missing".
                    this.navMeshProbeSamplesLogged++;
                    ModLogger.Msg("[NavMeshProbe] " + kind + " skipped: self player position unresolved (Mono entity read failed).");
                    return;
                }

                this.navMeshProbeSamplesLogged++;
                int sampleIndex = this.navMeshProbeSamplesLogged;
                float straightLen = Vector3.Distance(playerPos, target);

                ModLogger.Msg("[NavMeshProbe] #" + sampleIndex + "/" + NavMeshProbeMaxSamples + " " + kind
                    + " player=" + FormatNavMeshVector(playerPos) + " (" + playerSource + ")"
                    + " target=" + FormatNavMeshVector(target)
                    + " straight=" + straightLen.ToString("F1") + "m");

                bool playerOnMesh = this.TryNavMeshSample(playerPos, NavMeshProbeSampleRadius, out NavMeshHit playerHit);
                if (this.navMeshApiAvailable < 0)
                {
                    return; // SamplePosition threw; the latch already logged the reason.
                }

                bool targetOnMesh = this.TryNavMeshSample(target, NavMeshProbeSampleRadius, out NavMeshHit targetHit);

                ModLogger.Msg("[NavMeshProbe] #" + sampleIndex + " sample player: hit=" + playerOnMesh
                    + (playerOnMesh
                        ? " pos=" + FormatNavMeshVector(playerHit.position)
                          + " dist=" + playerHit.distance.ToString("F2") + "m"
                          + " area=" + FormatNavMeshAreaMask(playerHit.mask)
                        : " (no mesh within " + NavMeshProbeSampleRadius.ToString("F0") + "m)")
                    + " | sample target: hit=" + targetOnMesh
                    + (targetOnMesh
                        ? " pos=" + FormatNavMeshVector(targetHit.position)
                          + " dist=" + targetHit.distance.ToString("F2") + "m"
                          + " area=" + FormatNavMeshAreaMask(targetHit.mask)
                        : " (no mesh within " + NavMeshProbeSampleRadius.ToString("F0") + "m)"));

                // Raw endpoints first — this is the naive call the architecture sketch starts from,
                // and its failure vs the snapped call's success is the whole reason to snap.
                this.LogNavMeshPathAttempt(sampleIndex, "raw", playerPos, target, straightLen);

                if (playerOnMesh && targetOnMesh)
                {
                    Vector3 snappedFrom = playerHit.position;
                    Vector3 snappedTo = targetHit.position;
                    this.LogNavMeshPathAttempt(sampleIndex, "snapped", snappedFrom, snappedTo,
                        Vector3.Distance(snappedFrom, snappedTo));
                }
                else
                {
                    ModLogger.Msg("[NavMeshProbe] #" + sampleIndex + " snapped: skipped (an endpoint is off the mesh).");
                }
            }
            catch (Exception ex)
            {
                this.navMeshApiAvailable = -1;
                ModLogger.Msg("[NavMeshProbe] aborted — UnityEngine.AI threw: " + ex);
            }
        }

        // One CalculatePath attempt, fully reported. Never throws to the caller's caller.
        private void LogNavMeshPathAttempt(int sampleIndex, string label, Vector3 from, Vector3 to, float straightLen)
        {
            if (!this.EnsureNavMeshApiAvailable())
            {
                return;
            }

            try
            {
                NavMeshPath path = new NavMeshPath();
                bool ok = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
                this.navMeshApiAvailable = 1;

                NavMeshPathStatus status = path.status;
                Il2CppStructArray<Vector3> corners = path.corners;
                int cornerCount = corners == null ? 0 : corners.Length;

                string cornerText;
                float pathLen = 0f;
                if (cornerCount > 0)
                {
                    for (int i = 1; i < cornerCount; i++)
                    {
                        pathLen += Vector3.Distance(corners[i - 1], corners[i]);
                    }

                    cornerText = " first=" + FormatNavMeshVector(corners[0])
                        + " last=" + FormatNavMeshVector(corners[cornerCount - 1])
                        + " pathLen=" + pathLen.ToString("F1") + "m"
                        + " ratio=" + (straightLen > 0.01f ? (pathLen / straightLen).ToString("F2") : "n/a")
                        + " endGap=" + Vector3.Distance(corners[cornerCount - 1], to).ToString("F2") + "m";
                }
                else
                {
                    cornerText = " (no corners)";
                }

                ModLogger.Msg("[NavMeshProbe] #" + sampleIndex + " " + label + ": ok=" + ok
                    + " status=" + status
                    + " corners=" + cornerCount
                    + cornerText);
            }
            catch (Exception ex)
            {
                this.navMeshApiAvailable = -1;
                ModLogger.Msg("[NavMeshProbe] #" + sampleIndex + " " + label + ": CalculatePath threw — "
                    + "the navmesh route is NOT available from the mod: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
