using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // ACTION PANEL — a floating grid of game actions, opened and closed by a hotkey.
    //
    // The catalogue is the surviving third of a full sweep of the engine's 306 [ActionConfiguration]
    // entries. Every row here was cast at a standing character and measured: accepted by the game,
    // left the character where it stood, and gave movement back the moment the cast returned. The
    // ones that are NOT here failed exactly one of those:
    //
    //   26 carry an unfilled Vector3 that reads as a real world position under the terrain, so the
    //      character drops 140-160 m (PlayerMatrixApply, the whole Canvass/Friend/Share branch);
    //   16 hold the body for their animation — a further ~12 s each (Gather, ThrowFlower);
    //    3 wedge locomotion until a relog and survive every recovery this mod has
    //      (FeedWildAnimalMotion, PlayerFeedPetReady, PlayerStartBookEdit);
    //   82 are the `locking` class, which takes locomotion away by construction;
    //   38 are ids no context type claims at all.
    //
    // ── WHAT MAKES ONE PLAY ─────────────────────────────────────────────────────────────────────
    // Casting a blank context is ACCEPTED (ActionErrorCode 0) and animates nothing: each action
    // reads a field or two before it will drive the animator. Three gates cover this catalogue —
    //
    //   maxComboTime        0 means "no swings"; the gather/axe combo count
    //   controllerFullName  the clip family, built from the tool CURRENTLY IN HAND
    //   socialType          which social clip
    //
    // — plus every Vector3 that names a PLACE, which is filled with the player's own position so a
    // position-carrying action snaps the character where it already stands. Direction-like vectors
    // are deliberately left at zero: they are vectors, not places.
    //
    // Nothing here reaches the server with a target. levelObjectNetId is left at 0, so the actions
    // that would chop, mine or collect find no target and their send never runs — the motion plays
    // and no resource is touched.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogActionPanel = false;

        // ControllerShortName ordinals — the clip family a tool swing asks for. Only the axe rows
        // need one; everything else resolves its clip without help.
        //
        // ⚠️ ORDINALS, and they MOVE. ControllerShortName is a plain enum with a single explicit
        // value, so every name's number is its position — the 2026-08-20 update inserted entries
        // ahead of these and shifted them by four (lumbering was 159, mining 160). The cast still
        // returned ActionErrorCode 0 and simply rendered nothing, because the action asks the
        // animator for a controller family that no longer means what it did.
        // Re-check against ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.ResHandle.AnimationRes
        // /ControllerShortName.cs after every game update.
        private const int ActionPanelShortLumbering = 163;
        private const int ActionPanelShortMining = 164;

        // The social clip the two social rows play. 1 is the plain wave.
        private const int ActionPanelSocialType = 1;

        // One row of the panel.
        //
        // `Label` is what the button says — one or two words, named for what a bystander sees rather
        // than what the engine calls it. `Name` is the engine's own name and exists so a line in the
        // log can be matched back to an ActionId.
        //
        // `Fields` is the context's field list as name:type pairs, in declaration order. The filler
        // below walks it by NAME, so the order does not matter to it — it is kept in the game's own
        // order to stay diffable against the decompiled contexts after an update.
        internal readonly struct ActionPanelRow
        {
            public ActionPanelRow(int id, string label, string name, string context, string fields,
                                  int controllerShortName = 0)
            {
                this.Id = id;
                this.Label = label;
                this.Name = name;
                this.Context = context;
                this.Fields = fields;
                this.ControllerShortName = controllerShortName;
            }

            public int Id { get; }

            public string Label { get; }

            public string Name { get; }

            public string Context { get; }

            public string Fields { get; }

            /// Non-zero only for the rows whose clip family has to be spelled out (the axe swings).
            public int ControllerShortName { get; }
        }

        internal static readonly ActionPanelRow[] ActionPanelRows = new ActionPanelRow[]
        {
            new ActionPanelRow(200, "Chop", "AxeAttackTree", "ScriptsRefactory.LevelAndEntity.Gameplay.Action.PlayerAxeAttackTree", "levelObjectNetId:ulong|handholdNetId:uint|faceDirection:Vector2|controllerFullName:controller|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", ActionPanelShortLumbering),
            new ActionPanelRow(203, "Gather", "GatherContinuous", "XDTLevelAndEntity.Gameplay.Action.PlayerGatherContinuous", "levelObjectNetId:ulong|ownerNetId:uint|maxComboTime:int|targetHeight:float|faceDir:Vector2|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(205, "Mine", "AxeAttackStone", "ScriptsRefactory.LevelAndEntity.Gameplay.Action.PlayerAxeAttackStone", "levelObjectNetId:ulong|handholdNetId:uint|faceDirection:Vector2|controllerFullName:controller|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", ActionPanelShortMining),
            new ActionPanelRow(216, "Salute", "Salute", "XDTLevelAndEntity.Gameplay.Action.PlayerSaluteParam", "staticId:int|actionType:int|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(226, "Rainbow", "ThrowRainbowBuff", "XDTLevelAndEntity.Gameplay.Action.PlayerThrowRainbowBuffParam", "staticId:int|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(230, "Socialise", "PlaySocial", "XDTLevelAndEntity.Gameplay.Action.PlayerSocialActionArg", "socialType:int|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(231, "Flower Wand", "FlowerWand", "XDTLevelAndEntity.Gameplay.Action.PlayerFlowerWandActionArg", "playerNetId:uint|wandNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(232, "Funny Bird", "FunnyBird", "XDTLevelAndEntity.Gameplay.Action.PlayerFunnyBirdArg", "perchNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(236, "Draw Card", "DrawCard", "XDTLevelAndEntity.Gameplay.Action.PlayerDrawCardArg", "isSit:bool|isFirstOrLast:bool|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(237, "Play Card", "PlayCard", "XDTLevelAndEntity.Gameplay.Action.PlayerPlayCardArg", "cards:IEnumerable<PokerCard>|isSit:bool|isFirstOrLast:bool|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(245, "Throw", "Throw", "XDTLevelAndEntity.Gameplay.Action.PlayerThrowArg", "staticId:int|target:Vector3|netId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(246, "Repair", "Repair", "XDTLevelAndEntity.Gameplay.Action.PlayerRepairContext", "netId:uint|direction:Vector2|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(250, "Weed", "CropWeedAction", "XDTLevelAndEntity.Gameplay.Action.PlayerParameterCropWeed", "targetNetId:uint|target:Vector2|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(251, "Tease Cat", "TeaseCatAction", "XDTLevelAndEntity.Gameplay.Action.PlayerTeaseCatParam", "catHandle:uint|teaseType:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(257, "Cheer Dog", "TeaseDogEncourageAction", "XDTLevelAndEntity.Gameplay.Action.PlayerTeaseDogEncourageParam", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(258, "Ignore Dog", "TeaseDogIgnoreAction", "XDTLevelAndEntity.Gameplay.Action.PlayerTeaseDogIgnoreParam", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(259, "Call Dog", "CallDogAction", "XDTLevelAndEntity.Gameplay.Action.PlayerCallDogParam", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(268, "Bait", "Bait", "XDTLevelAndEntity.Gameplay.Action.PlayerBaitParaBase", "baitNetId:uint|target:Vector3|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(269, "Pick Flower", "GatherFlower", "XDTLevelAndEntity.Gameplay.Action.PlayerParameterGarther", "targetNetId:uint|target:Vector2|gatherType:GatherType|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(272, "Spray", "FishingSpray", "XDTLevelAndEntity.Gameplay.Action.PlayerFishingSprayParaBase", "backPackNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(283, "Invite", "InitiatorPrepareSocial", "XDTLevelAndEntity.Gameplay.Action.SocialInitiatorPrepareArg", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(285, "Leave Tub", "PlayTubOff", "XDTLevelAndEntity.Gameplay.Action.PlayerTubOffArg", "targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(297, "Reel In", "FishThrowSuccess", "ScriptsRefactory.LevelAndEntity.Gameplay.Action.PlayerFishThrowSuccessParam", "floatTargetPos:Vector3|localRotation:Quaternion|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(318, "Open Door", "OpenRotateDoor", "XDTLevelAndEntity.Gameplay.Action.PlayerOpenRotateDoorOnArg", "interactId:int|position:Vector3|faceDir:Vector2|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(323, "Tank Dive", "FishTankSwimmingOn", "XDTLevelAndEntity.Gameplay.Action.PlayerSwimInFishTankOnArg", "position:Vector3|rotation:Quaternion|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(386, "Take Candy", "PlayerTakeCandyAction", "XDTLevelAndEntity.Gameplay.Action.TakeCandyArg", "targetPoint:Vector3|targetForward:Vector2|poseType:ControllerPoseType|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(442, "Fortify", "InsectFortifier", "XDTLevelAndEntity.Gameplay.Action.PlayerInsectFortifierParaBase", "backPackNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(445, "Wrap Gift", "PackGiftAction", "XDTLevelAndEntity.Gameplay.Action.PackGiftActionArg", "staticId:int|playerNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(446, "Open Gift", "OpenGiftAction", "XDTLevelAndEntity.Gameplay.Action.OpenGiftActionArg", "staticId:int|playerNetId:uint|giftNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(448, "Sparkler", "LightHandheldFireworks", "XDTLevelAndEntity.Gameplay.Action.PlayerHandheldFireworksActionPara", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(455, "Greet NPC", "PlayerCanvassNpcNewYearStart", "XDTLevelAndEntity.Gameplay.Action.PlayerCanvassNpcNewYearStartArg", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(456, "NPC Greets", "NpcCanvassNewYearStart", "XDTLevelAndEntity.Gameplay.Action.NpcCanvassNewYearStartArg", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(463, "Drop Prop", "EquipOffDoubleSocialItemEntityAction", "XDTLevelAndEntity.Gameplay.Action.EquipOffDoubleSocialItemEntityActionArg", "initiatorPlayer:BaseActorComponent|responsePlayer:BaseActorComponent|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(464, "Bird Whistle", "PlayerBirdWhistleAction", "XDTLevelAndEntity.Gameplay.Action.PlayerBirdWhistleArg", "actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
        };

        private bool actionPanelVisible;
        private string actionPanelStatus = "Idle.";

        public bool ActionPanelVisible
        {
            get { return this.actionPanelVisible; }
        }

        public string ActionPanelStatus
        {
            get { return this.actionPanelStatus; }
        }

        // Hotkey entry point. The window itself is built lazily on the first frame it should show.
        public void ToggleActionPanel()
        {
            this.actionPanelVisible = !this.actionPanelVisible;
            // TIER 1 — panel open/close. The per-row cast results below were already unconditional,
            // so the log showed casts arriving out of nowhere with no record of the panel they came
            // from.
            FeatureLog.Life("ActionPanel", this.actionPanelVisible ? "opened" : "closed");
        }

        // ── casting ─────────────────────────────────────────────────────────────────────────────

        // Build the action's context, fill the gate fields, and hand it to
        // LocalPlayerComponent.Cast. Returns the game's own ActionErrorCode through `status`.
        private unsafe bool TryCastActionPanelRow(ActionPanelRow row, out string status)
        {
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoObjectNew == null || auraMonoFieldSetValue == null
                || auraMonoClassGetFieldFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            // ⚠️ Fail closed without pinning: the context object is held across the field writes,
            // and the moving GC relocates it the moment anything allocates.
            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr castMethod = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "Cast", 1);
            if (castMethod == IntPtr.Zero)
            {
                status = "LocalPlayerComponent.Cast(1) unresolved";
                return false;
            }

            IntPtr argClass = this.FindAuraMonoClassByFullName(row.Context);
            if (argClass == IntPtr.Zero)
            {
                status = "context unresolved: " + row.Context;
                return false;
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(playerClass,
                        out System.Collections.Generic.List<IntPtr> players, pins)
                    || players == null || players.Count == 0)
                {
                    status = "no local player";
                    return false;
                }

                // The LOCAL player's own position, off its entity — never the name-resolved anchor,
                // which can land on a REMOTE player. Writing that into a position-carrying action
                // would fling the character across the map to somebody else.
                bool havePos = this.TryGetAuraMonoEntityPositionFromComponent(players[0], out Vector3 selfPos);

                IntPtr argObj = auraMonoObjectNew(this.auraMonoRootDomain, argClass);
                if (argObj == IntPtr.Zero)
                {
                    status = "context alloc failed";
                    return false;
                }

                uint argPin = AuraMonoPinNew(argObj);
                if (argPin != 0u)
                {
                    pins.Add(argPin);
                }

                int overrideType = this.ReadActionPanelControllerOverride(playerClass, players[0]);
                int controllerSent = 0;
                string[] parts = (row.Fields ?? string.Empty).Split('|');
                for (int i = 0; i < parts.Length; i++)
                {
                    int colon = parts[i].IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }

                    string name = parts[i].Substring(0, colon);
                    if (string.Equals(name, "maxComboTime", StringComparison.Ordinal))
                    {
                        this.SetActionPanelInt(argObj, argClass, name, 3);
                    }
                    else if (string.Equals(name, "socialType", StringComparison.Ordinal))
                    {
                        this.SetActionPanelInt(argObj, argClass, name, ActionPanelSocialType);
                    }
                    else if (string.Equals(name, "controllerFullName", StringComparison.Ordinal)
                             && row.ControllerShortName > 0)
                    {
                        controllerSent = (1 << 24) | (1 << 14) | (overrideType << 9) | row.ControllerShortName;
                        // (charType << 24) | (poseType << 14) | (override << 9) | shortName, with the
                        // override read live so whatever is actually in hand is honoured.
                        this.SetActionPanelInt(argObj, argClass, name,
                            (1 << 24) | (1 << 14) | (overrideType << 9) | row.ControllerShortName);
                    }
                    else if (havePos
                             && parts[i].EndsWith(":Vector3", StringComparison.Ordinal)
                             && IsActionPanelPositionField(name))
                    {
                        this.SetActionPanelVector3(argObj, argClass, name, selfPos);
                    }
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(castMethod, players[0], (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Cast threw 0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                int code = result == IntPtr.Zero ? 0 : this.ReadAuraMonoBoxedInt32(result);
                // The controller id goes in the line too: a swing that is ACCEPTED and renders
                // nothing is almost always this number pointing at the wrong clip family, and
                // without it that costs a live debugging session to find (it already did once).
                status = "ActionErrorCode " + code
                    + (controllerSent != 0 ? " controller=" + controllerSent + " (override " + overrideType
                        + ", shortName " + row.ControllerShortName + ")" : string.Empty);
                return code == 0;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // A Vector3 that names a PLACE, not a direction. Getting this wrong in either direction is
        // visible: fill a direction and the action aims at the world origin, leave a place empty and
        // the character is teleported under the map.
        private static bool IsActionPanelPositionField(string name)
        {
            return string.Equals(name, "position", StringComparison.Ordinal)
                || string.Equals(name, "target", StringComparison.Ordinal)
                || string.Equals(name, "targetPos", StringComparison.Ordinal)
                || string.Equals(name, "floatTargetPos", StringComparison.Ordinal)
                || string.Equals(name, "dstPosition", StringComparison.Ordinal)
                || string.Equals(name, "endPosition", StringComparison.Ordinal)
                || string.Equals(name, "playPosition", StringComparison.Ordinal);
        }

        // Called from the panel's buttons. Always logs — a toast is gone in three seconds and this is
        // the only record of what was cast and what the game answered.
        internal void CastActionPanelRow(ActionPanelRow row)
        {
            bool ok;
            string status;
            try
            {
                ok = this.TryCastActionPanelRow(row, out status);
            }
            catch (Exception ex)
            {
                ok = false;
                status = ex.GetType().Name + ": " + ex.Message;
            }

            this.actionPanelStatus = row.Label + " — " + status;
            ModLogger.Msg("[ActionPanel] " + row.Label + " (" + row.Id + " " + row.Name + ") -> " + status);
            if (!ok)
            {
                this.AddMenuNotification(row.Label + ": " + status, new Color(1f, 0.55f, 0.55f));
            }
        }

        private int ReadActionPanelControllerOverride(IntPtr playerClass, IntPtr player)
        {
            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(playerClass, "GetControllerOverrideType", 0);
            if (getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 5; // empty hands
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, player, IntPtr.Zero, ref exc);
            return (exc != IntPtr.Zero || boxed == IntPtr.Zero) ? 5 : this.ReadAuraMonoBoxedInt32(boxed);
        }

        private unsafe void SetActionPanelInt(IntPtr obj, IntPtr klass, string fieldName, int value)
        {
            IntPtr field = auraMonoClassGetFieldFromName(klass, fieldName);
            if (field != IntPtr.Zero)
            {
                auraMonoFieldSetValue(obj, field, (IntPtr)(&value));
            }
        }

        private unsafe void SetActionPanelVector3(IntPtr obj, IntPtr klass, string fieldName, Vector3 value)
        {
            IntPtr field = auraMonoClassGetFieldFromName(klass, fieldName);
            if (field != IntPtr.Zero)
            {
                auraMonoFieldSetValue(obj, field, (IntPtr)(&value));
            }
        }
    }
}
