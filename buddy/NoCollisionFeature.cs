using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Passing through invisible barriers, by flipping the engine's LAYER COLLISION MATRIX.
    //
    // WHAT THESE BARRIERS ARE. The world contains barrier volumes the player never sees: thin tall
    // slabs and similar geometry sitting on layer Passable(10). The layer's name means the OPPOSITE
    // of what it says — measured with paired probes, 12 of 12 samples solid. These are what produce
    // "I cannot walk here and there is nothing to see".
    //
    // WHAT REMOVES THEM. One call:
    //
    //     XDT.Physics.PhysicsManager.IgnoreLayerCollision(our layer, 10, true)
    //
    // That flips ONE CELL of the collision matrix. The world itself is untouched: no collider is
    // destroyed, disabled, turned into a trigger or moved. The barriers stay exactly where they are —
    // they simply stop applying to us.
    //
    // ⭐ This is the sanctioned route, not a hole someone found: the game uses the same lever itself.
    // VehicleManager takes out the Water<->Vehicle pair by layer, and VehicleResHandle takes out
    // controller<->car-body per pair so a driver does not collide with the car they are sitting in.
    //
    // ⭐ ON FOOT AND IN A VEHICLE ARE DIFFERENT PAIRS, and that is measured rather than reasoned:
    // driving through a barrier succeeded while the PLAYER pair was still ENABLED, so while riding it
    // is the car body (layer 16) that meets the wall and the character controller takes no part. One
    // pair cannot cover both.
    //
    // ⭐ IT SURVIVES A WORLD CHANGE. Diving underwater and surfacing again raised the world epoch
    // 3 -> 5 (two scene swaps, colliders fully rebuilt) and the pass-through kept working with no
    // re-application. This is a global engine setting rather than a property of the scene, which is
    // why there is no world gate here — a rare exception to how the rest of this mod works.
    //
    // ⚠️ THERE IS NO GETTER. The engine cannot be asked "is this pair off right now" — the wrappers
    // expose setters only. So the fields below are the ONLY record of what we switched, and they have
    // to be exact: lie once and the collision can never be given back. Two consequences:
    //   * apply strictly on the DIFFERENCE between wanted and applied, never "just in case" per frame;
    //   * restore with the SAME layer number we disabled with (see noCollisionPlayerLayerApplied).
    //
    // ⚠️ THE ORACLES CANNOT SEE THE MATRIX. All of LevelLayerManager is built on casts, and a cast
    // filters by layerMask alone. Once a pair is off the player walks through while
    // CanPlayerMoveUseSphere still answers BLOCKED. For the walker that means this switch does NOT
    // open new routes to it — its leg audit keeps rejecting legs that became walkable. The benefit is
    // a different one and it is real: when the walker does step onto a leg whose barrier the audit
    // missed — and it does miss them, which is why the escape ladder exists — it now pushes through
    // instead of wedging and burning the whole leg budget.
    public partial class HeartopiaComplete
    {
        // The barrier layer. See the docs: "Passable" blocks, "Wall" does not.
        private const int NoCollisionBarrierLayer = 10;

        // Fallback layer for the player: this is what the live controller reported. It is still read
        // live every time — the constant only covers a controller that will not resolve.
        private const int NoCollisionPlayerLayerFallback = 8;

        // The car body's layer. There is nothing to read it from live — we hold no reference to the
        // collider of the vehicle we are sitting in — so the number is the measured one: the census
        // found 32 colliders on layer 16, and driving through a barrier confirmed that layer.
        private const int NoCollisionVehicleLayer = 16;

        // User toggles (persisted).
        private bool noCollisionPlayerEnabled;
        private bool noCollisionVehicleEnabled;

        // The walker's hold: while walk-to-nodes is steering the character, collision is off whatever
        // the toggles say. Not persisted — this is run state, not a setting.
        private bool noCollisionWalkerHold;

        private IntPtr noCollisionMethod;
        private bool noCollisionResolveTried;
        private bool noCollisionResolveFailedLogged;

        // What is ACTUALLY switched off in the engine right now. The only source of truth — see the
        // header above.
        private bool noCollisionPlayerApplied;
        private bool noCollisionVehicleApplied;

        // The layer we disabled with. Restoring must use the same one: if the controller answers a
        // different number at restore time (or does not answer at all), the pair stays off forever.
        private int noCollisionPlayerLayerApplied = -1;

        internal bool NoCollisionActive => this.noCollisionPlayerApplied || this.noCollisionVehicleApplied;

        private bool EnsureNoCollisionResolved()
        {
            if (this.noCollisionMethod != IntPtr.Zero)
            {
                return true;
            }

            if (this.noCollisionResolveTried)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;   // AuraMono is not up yet — NOT a reason to burn the attempt.
                }

                // Two wrappers over the SAME native icalls (verified by comparing the icall ids in
                // the decompilation), so whichever resolves first will do.
                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDT.Physics", "PhysicsManager",
                    new[] { "EngineWrapper", "EngineWrapper.dll" });
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInImages(
                        "MonoGame.ScriptFramework", "PhysicsExtension",
                        new[] { "EngineWrapper", "EngineWrapper.dll" });
                }

                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInAllLoadedImages("PhysicsManager", "XDT.Physics");
                }

                if (cls == IntPtr.Zero)
                {
                    return false;   // the image may not be loaded yet — try again later.
                }

                this.noCollisionMethod = this.FindAuraMonoMethodOnHierarchy(cls, "IgnoreLayerCollision", 3);
                if (this.noCollisionMethod == IntPtr.Zero)
                {
                    // Class present, method absent — that is permanent, nothing to retry.
                    this.noCollisionResolveTried = true;
                    ModLogger.Msg("[NoCollision] IgnoreLayerCollision(int,int,bool) not found — "
                        + "passing through barriers is unavailable this session.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.noCollisionResolveTried = true;
                ModLogger.Msg("[NoCollision] resolve threw: " + ex.Message);
                return false;
            }
        }

        // All three arguments are value types, so every slot is a pointer to OUR OWN bytes on the
        // stack. In-params are the safe direction through mono_runtime_invoke; the lethal shape (an
        // out slot holding a struct) does not occur here at all.
        private unsafe bool TryNoCollisionIgnoreLayer(int layerA, int layerB, bool ignore)
        {
            if (this.noCollisionMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int a = layerA;
                int b = layerB;
                byte flag = (byte)(ignore ? 1 : 0);
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&a);
                args[1] = (IntPtr)(&b);
                args[2] = (IntPtr)(&flag);

                // ⚠️ The method returns void, so success CANNOT be judged from the result here — it
                // is always zero. An empty exception is the only signal there is.
                auraMonoRuntimeInvoke(this.noCollisionMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    ModLogger.Msg("[NoCollision] IgnoreLayerCollision(" + layerA + ", " + layerB
                        + ", " + ignore + ") threw.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[NoCollision] invoke threw: " + ex.Message);
                return false;
            }
        }

        // The character controller's live layer. Not a constant: layers are handed out by
        // LayerMask.NameToLayer, and a hardcoded number would survive exactly until they are renumbered.
        private int ResolveNoCollisionPlayerLayer()
        {
            try
            {
                if (this.TryGetFarmWalkSweepController(out IntPtr ctrl) && ctrl != IntPtr.Zero
                    && this.TryInvokeAuraMonoZeroArgInt(ctrl, out int layer, "get_layer")
                    && layer >= 0 && layer <= 31)
                {
                    return layer;
                }
            }
            catch
            {
            }

            return NoCollisionPlayerLayerFallback;
        }

        private void ProcessNoCollisionOnUpdate()
        {
            // The walker is steering — drop collision for the length of the run, toggles aside.
            bool hold = this.FarmWalkRunActive;
            if (hold != this.noCollisionWalkerHold)
            {
                this.noCollisionWalkerHold = hold;
                ModLogger.Msg(hold
                    ? "[NoCollision] walk to nodes started — dropping collision with the barrier "
                        + "layer for the run."
                    : "[NoCollision] walk to nodes finished — collision returns to whatever the "
                        + "toggles ask for.");
            }

            this.ApplyNoCollisionState();
        }

        // Apply ONLY on the difference between wanted and applied. Poking the matrix every frame
        // would be both wasted work and the loss of the one record that we changed anything at all.
        private void ApplyNoCollisionState()
        {
            bool wantPlayer = this.noCollisionPlayerEnabled || this.noCollisionWalkerHold;
            bool wantVehicle = this.noCollisionVehicleEnabled || this.noCollisionWalkerHold;

            if (wantPlayer == this.noCollisionPlayerApplied && wantVehicle == this.noCollisionVehicleApplied)
            {
                return;
            }

            if (!this.EnsureNoCollisionResolved())
            {
                // Staying silent is not an option: from outside this looks like "the box is ticked
                // and nothing happens".
                if ((wantPlayer || wantVehicle) && !this.noCollisionResolveFailedLogged)
                {
                    this.noCollisionResolveFailedLogged = true;
                    ModLogger.Msg("[NoCollision] nothing to switch with — IgnoreLayerCollision has "
                        + "not resolved yet (AuraMono is not up, or the image is not loaded).");
                }

                return;
            }

            this.noCollisionResolveFailedLogged = false;

            if (wantPlayer != this.noCollisionPlayerApplied)
            {
                // Disable with the LIVE layer, restore with the REMEMBERED one.
                int layer = wantPlayer ? this.ResolveNoCollisionPlayerLayer() : this.noCollisionPlayerLayerApplied;
                if (layer >= 0 && this.TryNoCollisionIgnoreLayer(layer, NoCollisionBarrierLayer, wantPlayer))
                {
                    this.noCollisionPlayerApplied = wantPlayer;
                    this.noCollisionPlayerLayerApplied = wantPlayer ? layer : -1;
                    ModLogger.Msg("[NoCollision] player (layer " + layer + ") "
                        + (wantPlayer ? "now IGNORES" : "collides again with") + " barrier layer "
                        + NoCollisionBarrierLayer + ".");
                }
            }

            if (wantVehicle != this.noCollisionVehicleApplied)
            {
                if (this.TryNoCollisionIgnoreLayer(NoCollisionVehicleLayer, NoCollisionBarrierLayer, wantVehicle))
                {
                    this.noCollisionVehicleApplied = wantVehicle;
                    ModLogger.Msg("[NoCollision] vehicle (layer " + NoCollisionVehicleLayer + ") "
                        + (wantVehicle ? "now IGNORES" : "collides again with") + " barrier layer "
                        + NoCollisionBarrierLayer + ".");
                }
            }
        }

        // Give back everything we switched off. Called on mod teardown: the matrix SURVIVES a world
        // change, so it would survive an unload too — left off, it would stay off for the rest of the
        // game session with nobody left to restore it.
        internal void ReleaseNoCollision()
        {
            if (!this.noCollisionPlayerApplied && !this.noCollisionVehicleApplied)
            {
                return;
            }

            this.noCollisionPlayerEnabled = false;
            this.noCollisionVehicleEnabled = false;
            this.noCollisionWalkerHold = false;
            this.ApplyNoCollisionState();
        }
    }
}
