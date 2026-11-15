using System;
using System.Globalization;
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

        // ControllerShortName is resolved BY NAME at cast time (ControllerNameResolver.cs) — the
        // enum numbers its entries by position, so they move on every big update and a stale one
        // renders nothing while still returning ActionErrorCode 0. These are only the last-known
        // values, used if the lookup itself cannot run.
        private const string ActionPanelShortLumbering = "lumbering";
        private const string ActionPanelShortMining = "mining";
        private const int ActionPanelShortLumberingFallback = 200;
        private const int ActionPanelShortMiningFallback = 201;

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
                                  string controllerShortName = null, int controllerShortNameFallback = 0,
                                  string constants = null)
            {
                this.Id = id;
                this.Label = label;
                this.Name = name;
                this.Context = context;
                this.Fields = fields;
                this.ControllerShortName = controllerShortName;
                this.ControllerShortNameFallback = controllerShortNameFallback;
                this.Constants = constants;
            }

            public int Id { get; }

            public string Label { get; }

            public string Name { get; }

            public string Context { get; }

            public string Fields { get; }

            /// Set only for the rows whose clip family has to be spelled out (the axe swings).
            /// A ControllerShortName NAME, resolved against the loaded enum at cast time.
            public string ControllerShortName { get; }

            /// Last-known ordinal for that name, used only if the runtime lookup cannot run.
            public int ControllerShortNameFallback { get; }

            /// Fixed int field values this row needs, as "name=value|name=value".
            ///
            /// The filler below can work out the generic gates (combo count, social type, the
            /// player's own position), but not a value that IDENTIFIES one particular thing —
            /// an interaction id, for instance, which is what picks the clip for the whole
            /// OnceInteract family. Those are spelled out per row.
            public string Constants { get; }
        }

        internal static readonly ActionPanelRow[] ActionPanelRows = new ActionPanelRow[]
        {
            new ActionPanelRow(200, "Chop", "AxeAttackTree", "ScriptsRefactory.LevelAndEntity.Gameplay.Action.PlayerAxeAttackTree", "levelObjectNetId:ulong|handholdNetId:uint|faceDirection:Vector2|controllerFullName:controller|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", ActionPanelShortLumbering, ActionPanelShortLumberingFallback),
            new ActionPanelRow(203, "Gather", "GatherContinuous", "XDTLevelAndEntity.Gameplay.Action.PlayerGatherContinuous", "levelObjectNetId:ulong|ownerNetId:uint|maxComboTime:int|targetHeight:float|faceDir:Vector2|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool"),
            new ActionPanelRow(205, "Mine", "AxeAttackStone", "ScriptsRefactory.LevelAndEntity.Gameplay.Action.PlayerAxeAttackStone", "levelObjectNetId:ulong|handholdNetId:uint|faceDirection:Vector2|controllerFullName:controller|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", ActionPanelShortMining, ActionPanelShortMiningFallback),
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
            // ── interaction clips (ActionId 313, OnceInteractConfig) ────────────────────────
            //
            // These nine share one ActionId and differ ONLY by interactId, because
            // PlayerActionOnceInteract does not name its own clip: it asks
            // InteractionPiplineSystem for the config behind the id, so the id IS the animation
            // selector. Picked out of the 90 interactions of this shape by casting every one and
            // reading the animator state back (AnimHashConvert.ToStateName); the state each plays
            // is noted so a game update that moves them is diffable.
            //
            // The labels are what the animation LOOKS like — these clips belong to the
            // interactions themselves, not to the Singleaction catalogue, so they have no game
            // name of their own.
            //
            // Cast with no target: occupiedLevelObject stays 0, so OnFinishAction finds no
            // furniture and returns. The clip is all that happens.
            new ActionPanelRow(313, "Paper Punch", "OnceInteract 707045", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707045"),   // Linkage06
            new ActionPanelRow(313, "Shower", "OnceInteract 707086", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707086"),        // Petty253
            new ActionPanelRow(313, "Spoon", "OnceInteract 707087", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707087"),         // Petty254
            new ActionPanelRow(313, "Sauce", "OnceInteract 707088", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707088"),         // Petty220
            new ActionPanelRow(313, "Paint", "OnceInteract 707111", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707111"),         // Petty322
            new ActionPanelRow(313, "Reading", "OnceInteract 707130", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707130"),       // Petty332
            new ActionPanelRow(313, "Put Lock", "OnceInteract 707193", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707193"),      // Petty355
            new ActionPanelRow(313, "Air Dance", "OnceInteract 707222", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707222"),     // Petty385
            new ActionPanelRow(313, "Air Fly", "OnceInteract 707225", "XDTLevelAndEntity.Gameplay.Action.PlayerActionOnceInteractArg", "interactId:int|faceDir:Vector2|destination:Vector3|targetDirection:TargetDirection|target:ulong|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "interactId=707225"),       // Petty375
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
            // Puzzle poses (no board). Cast starts ac_player_stand_empty_jigsaw; lean= forces the
            // visible clip because without a board OnMotionTick never sets Upward/Forward/Downward.
            // 0 = DownwardJigsaw (floor / seated lean), 1 = ForwardJigsaw (standing lean).
            new ActionPanelRow(483, "Puzzle Stand", "JigsawPuzzleMotion", "XDTLevelAndEntity.Gameplay.Locomotion.PlayerJigsawPuzzleMotionArg", "jigsawPuzzleNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "lean=1"),
            new ActionPanelRow(483, "Puzzle Sit", "JigsawPuzzleMotion", "XDTLevelAndEntity.Gameplay.Locomotion.PlayerJigsawPuzzleMotionArg", "jigsawPuzzleNetId:uint|actor:ActorActionGraph|playPosition:LowAccuracyVec3|quickCast:bool", null, 0, "lean=0"),
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
                    else if (string.Equals(name, "jigsawPuzzleNetId", StringComparison.Ordinal))
                    {
                        // Filled below once lean= is known — remotes derive Forward/Downward from
                        // this entity's height in PlayerJigsawPuzzleMotion.OnMotionTick.
                    }
                    else if (string.Equals(name, "controllerFullName", StringComparison.Ordinal)
                             && !string.IsNullOrEmpty(row.ControllerShortName))
                    {
                        int shortName = this.ResolveControllerShortName(row.ControllerShortName,
                                                                        row.ControllerShortNameFallback);
                        controllerSent = (1 << 24) | (1 << 14) | (overrideType << 9) | shortName;
                        // (charType << 24) | (poseType << 14) | (override << 9) | shortName, with the
                        // override read live so whatever is actually in hand is honoured.
                        this.SetActionPanelInt(argObj, argClass, name, controllerSent);
                    }
                    else if (havePos
                             && parts[i].EndsWith(":Vector3", StringComparison.Ordinal)
                             && IsActionPanelPositionField(name))
                    {
                        this.SetActionPanelVector3(argObj, argClass, name, selfPos);
                    }
                }

                // Row-specific fixed values, applied last so they always win over the generic
                // filler above. lean= is not an ActionContext field — handled after Cast.
                int puzzleLean = -1;
                string[] constants = (row.Constants ?? string.Empty).Split('|');
                for (int i = 0; i < constants.Length; i++)
                {
                    int eq = constants[i].IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string cName = constants[i].Substring(0, eq);
                    if (!int.TryParse(constants[i].Substring(eq + 1), NumberStyles.Integer,
                                      CultureInfo.InvariantCulture, out int constantValue))
                    {
                        continue;
                    }

                    if (string.Equals(cName, "lean", StringComparison.Ordinal))
                    {
                        puzzleLean = constantValue;
                        continue;
                    }

                    this.SetActionPanelInt(argObj, argClass, cName, constantValue);
                }

                // Puzzle motion is an ActorMotionClip: AbilityCaster.Cast does NOT drive MotionGraph
                // and remotes only learn about it via SetMotion → FsmStatus.MotionData.
                // WriteBytes carries only jigsawPuzzleNetId — remotes set Forward/Downward/Upward
                // from (player.y - entity.y). netId=0 → hide handhold only, no lean clip (what
                // watchers were seeing). Pick an anchor whose height yields the requested lean.
                if (puzzleLean >= 0)
                {
                    uint leanAnchor = 0U;
                    string anchorKind = "none";
                    if (this.TryResolveActionPanelPuzzleLeanAnchor(players[0], puzzleLean, selfPos,
                            havePos, out leanAnchor, out anchorKind)
                        && leanAnchor != 0U)
                    {
                        this.SetActionPanelUInt(argObj, argClass, "jigsawPuzzleNetId", leanAnchor);
                    }

                    if (havePos)
                    {
                        this.SetActionPanelPlayPosition(argObj, argClass, selfPos);
                    }

                    if (!this.TryActionPanelSetMotion(players[0], argObj, out string motionStatus))
                    {
                        status = motionStatus;
                        return false;
                    }

                    // Local: ForceLean wins immediately (OnMotionTick only sets bools once when the
                    // anchor entity resolves). Remotes rely on the anchor height in MotionData.
                    this.ForceActionPanelPuzzleLean(players[0], puzzleLean);
                    status = "SetMotion ok lean=" + puzzleLean
                        + " anchor=" + leanAnchor + " (" + anchorKind + ")";
                    return true;
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
        // Ends whatever the body is playing — the lever behind the panel's Stop button.
        //
        // The interaction clips (ActionId 313) do not release on their own: one held the character
        // for minutes in testing, and while it runs every other cast comes back ActionErrorCode 1
        // (busy). ActorActionGraph.EndCasting is the same lever the craft/dye skip uses.
        internal void EndActionPanelCasting()
        {
            string status = "unavailable";
            bool ok = false;
            try
            {
                ok = this.TryEndActionPanelCasting(out status);
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
            }

            this.actionPanelStatus = ok ? "Stopped the current action." : ("Stop failed: " + status);
            FeatureLog.Life("ActionPanel", "stop -> " + (ok ? "ok" : ("FAILED (" + status + ")")));
        }

        private bool TryEndActionPanelCasting(out string status)
        {
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr graphClass = this.FindAuraMonoClassInAllLoadedImages(
                "ActorActionGraph", "XDTLevelAndEntity.Gameplay.Playable");
            IntPtr getGraph = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "get_actionGraph", 0);
            IntPtr endCasting = graphClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(graphClass, "EndCasting", 0);
            if (getGraph == IntPtr.Zero || endCasting == IntPtr.Zero)
            {
                status = "get_actionGraph=" + (getGraph != IntPtr.Zero)
                         + " EndCasting=" + (endCasting != IntPtr.Zero);
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

                IntPtr exc = IntPtr.Zero;
                IntPtr graph = auraMonoRuntimeInvoke(getGraph, players[0], IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || graph == IntPtr.Zero)
                {
                    status = "no action graph";
                    return false;
                }

                uint graphPin = AuraMonoPinNew(graph);
                if (graphPin != 0u)
                {
                    pins.Add(graphPin);
                }

                auraMonoRuntimeInvoke(endCasting, graph, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "EndCasting threw 0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                // Puzzle (and any other ActorMotionClip) lives on MotionPart, not AbilityCaster —
                // EndCasting does not touch it. SetMotion(Free) ends the clip, writes Free into
                // FsmStatus.MotionData (FreeMotionClip is an ActorMotionClip), and remotes drop
                // the jigsaw pose. Local lean bools / controller are cleaned after.
                if (!this.TryActionPanelRestoreFreeMotion(players[0], pins, out string freeStatus))
                {
                    status = "EndCasting ok; Free motion failed: " + freeStatus;
                    this.ClearActionPanelPuzzlePose(players[0]);
                    return false;
                }

                this.ClearActionPanelPuzzlePose(players[0]);

                status = "ok";
                return true;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // CharacterPart.Body = 0. SetMotion → MotionPart.SetContext → OnActionStart →
        // PlayerSyncStatus.SendAction → FsmStatus.MotionData for ActorMotionClip args.
        private const int ActionPanelCharacterPartBody = 0;

        private unsafe bool TryActionPanelSetMotion(IntPtr player, IntPtr argObj, out string status)
        {
            status = "unavailable";
            if (player == IntPtr.Zero || argObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr setMotion = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "SetMotion", 2);
            if (setMotion == IntPtr.Zero)
            {
                status = "SetMotion(2) unresolved";
                return false;
            }

            int part = ActionPanelCharacterPartBody;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&part);
            args[1] = argObj;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(setMotion, player, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "SetMotion threw 0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "ok";
            return true;
        }

        // LowAccuracyVec3 is three packed millimetre ints, not a Vector3 — writing floats corrupts
        // the sync payload and remotes reconstruct the pose at the wrong place.
        private unsafe void SetActionPanelPlayPosition(IntPtr argObj, IntPtr argClass, Vector3 pos)
        {
            if (argObj == IntPtr.Zero || argClass == IntPtr.Zero || auraMonoClassGetFieldFromName == null
                || auraMonoFieldSetValue == null)
            {
                return;
            }

            IntPtr field = auraMonoClassGetFieldFromName(argClass, "playPosition");
            if (field == IntPtr.Zero)
            {
                // playPosition is declared on ActionContext; walk one parent if the concrete
                // arg class does not redeclare it.
                IntPtr parent = auraMonoClassGetParent != null
                    ? auraMonoClassGetParent(argClass)
                    : IntPtr.Zero;
                if (parent != IntPtr.Zero)
                {
                    field = auraMonoClassGetFieldFromName(parent, "playPosition");
                }
            }

            if (field == IntPtr.Zero)
            {
                return;
            }

            int* lav = stackalloc int[3];
            lav[0] = (int)(pos.x * 1000f);
            lav[1] = (int)(pos.y * 1000f);
            lav[2] = (int)(pos.z * 1000f);
            auraMonoFieldSetValue(argObj, field, (IntPtr)lav);
        }

        private unsafe bool TryActionPanelRestoreFreeMotion(IntPtr player,
            System.Collections.Generic.List<uint> pins, out string status)
        {
            status = "unavailable";
            if (auraMonoObjectNew == null)
            {
                return false;
            }

            IntPtr freeClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.Gameplay.Component.Player.PlayerFreeMotionArg");
            if (freeClass == IntPtr.Zero)
            {
                freeClass = this.FindAuraMonoClassInAllLoadedImages(
                    "PlayerFreeMotionArg", "XDTLevelAndEntity.Gameplay.Component.Player");
            }

            if (freeClass == IntPtr.Zero)
            {
                status = "PlayerFreeMotionArg unresolved";
                return false;
            }

            IntPtr freeObj = auraMonoObjectNew(this.auraMonoRootDomain, freeClass);
            if (freeObj == IntPtr.Zero)
            {
                status = "PlayerFreeMotionArg alloc failed";
                return false;
            }

            uint pin = AuraMonoPinNew(freeObj);
            if (pin != 0u)
            {
                pins.Add(pin);
            }

            if (this.TryGetAuraMonoEntityPositionFromComponent(player, out Vector3 selfPos))
            {
                this.SetActionPanelPlayPosition(freeObj, freeClass, selfPos);
            }

            return this.TryActionPanelSetMotion(player, freeObj, out status);
        }

        private static bool IsActionPanelPositionField(string name)
        {
            return string.Equals(name, "position", StringComparison.Ordinal)
                || string.Equals(name, "target", StringComparison.Ordinal)
                || string.Equals(name, "targetPos", StringComparison.Ordinal)
                || string.Equals(name, "floatTargetPos", StringComparison.Ordinal)
                || string.Equals(name, "dstPosition", StringComparison.Ordinal)
                || string.Equals(name, "endPosition", StringComparison.Ordinal)
                // IMoveStrideWhenStart reads this one and STEPS THERE on start, so an unfilled
                // destination walks the character to the world origin.
                || string.Equals(name, "destination", StringComparison.Ordinal)
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

        private unsafe void SetActionPanelUInt(IntPtr obj, IntPtr klass, string fieldName, uint value)
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

        // After Cast(JigsawPuzzleMotion) with netId=0 the controller loads but lean bools stay false
        // (OnMotionTick only sets them when a board entity exists). Force Forward (1) or Downward (0).
        private const uint ActionPanelJigsawController = 33592442u; // ac_player_stand_empty_jigsaw

        private bool TryGetActionPanelAnimationComponent(IntPtr player, out IntPtr anim)
        {
            anim = IntPtr.Zero;
            if (player == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(player, "<animationComponent>k__BackingField", out anim)
                && anim != IntPtr.Zero)
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(player, "animationComponent", out anim) && anim != IntPtr.Zero)
            {
                return true;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr getAnim = playerClass == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(playerClass, "get_animationComponent", 0);
            if (getAnim == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            anim = auraMonoRuntimeInvoke(getAnim, player, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && anim != IntPtr.Zero;
        }

        // Undo ForceActionPanelPuzzleLean: clear lean bools and switch back to stand+pose with the
        // player's current handhold override (same shape StandLocomotion uses).
        private unsafe void ClearActionPanelPuzzlePose(IntPtr player)
        {
            if (!this.TryGetActionPanelAnimationComponent(player, out IntPtr anim) || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            IntPtr animClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.EntityView.AnimationComponent");
            IntPtr paramClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.AnimParamHash");
            IntPtr charClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.ControllerCharType");
            IntPtr poseClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.ControllerPoseType");
            IntPtr shortClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.ControllerShortName");
            if (animClass == IntPtr.Zero || paramClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr setBool = this.FindAuraMonoMethodOnHierarchy(animClass, "SetBool", 2);
            if (setBool != IntPtr.Zero
                && this.TryReadAuraMonoStaticIntField(paramClass, new[] { "DownwardJigsaw" }, out int hashDown)
                && this.TryReadAuraMonoStaticIntField(paramClass, new[] { "ForwardJigsaw" }, out int hashForward)
                && this.TryReadAuraMonoStaticIntField(paramClass, new[] { "UpwardJigsaw" }, out int hashUp))
            {
                void ClearBool(int hash)
                {
                    int h = hash;
                    bool v = false;
                    IntPtr* bArgs = stackalloc IntPtr[2];
                    bArgs[0] = (IntPtr)(&h);
                    bArgs[1] = (IntPtr)(&v);
                    IntPtr excB = IntPtr.Zero;
                    auraMonoRuntimeInvoke(setBool, anim, (IntPtr)bArgs, ref excB);
                }

                ClearBool(hashDown);
                ClearBool(hashForward);
                ClearBool(hashUp);
            }

            // Only slam stand+pose when the jigsaw controller is still active — other Stop targets
            // (OnceInteract etc.) already restore via EndCasting and must not be overridden.
            IntPtr getCtrl = this.FindAuraMonoMethodOnHierarchy(animClass, "get_controllerFullName", 0);
            if (getCtrl != IntPtr.Zero && auraMonoObjectUnbox != null)
            {
                IntPtr excC = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(getCtrl, anim, IntPtr.Zero, ref excC);
                if (excC == IntPtr.Zero && boxed != IntPtr.Zero)
                {
                    IntPtr raw = auraMonoObjectUnbox(boxed);
                    uint current = raw == IntPtr.Zero ? 0u : unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(raw));
                    if (current != ActionPanelJigsawController)
                    {
                        return;
                    }
                }
            }

            IntPtr play5 = this.FindAuraMonoMethodOnHierarchy(animClass, "PlayController", 5);
            if (play5 == IntPtr.Zero || charClass == IntPtr.Zero || poseClass == IntPtr.Zero || shortClass == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryReadAuraMonoStaticIntField(charClass, new[] { "player" }, out int charType)
                || !this.TryReadAuraMonoStaticIntField(poseClass, new[] { "stand" }, out int poseType)
                || !this.TryReadAuraMonoStaticIntField(shortClass, new[] { "pose" }, out int shortName))
            {
                return;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            int overrideType = this.ReadActionPanelControllerOverride(playerClass, player);

            int* id = stackalloc int[4];
            id[0] = charType;
            id[1] = poseType;
            id[2] = overrideType;
            id[3] = shortName;
            bool fallback = true;
            float blend = 0.25f;
            int dst = 0;
            bool force = true;
            IntPtr* playArgs = stackalloc IntPtr[5];
            playArgs[0] = (IntPtr)id;
            playArgs[1] = (IntPtr)(&fallback);
            playArgs[2] = (IntPtr)(&blend);
            playArgs[3] = (IntPtr)(&dst);
            playArgs[4] = (IntPtr)(&force);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(play5, anim, (IntPtr)playArgs, ref exc);
        }

        private unsafe void ForceActionPanelPuzzleLean(IntPtr player, int lean)
        {
            if (player == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            if (!this.TryGetActionPanelAnimationComponent(player, out IntPtr anim))
            {
                return;
            }

            IntPtr animClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.EntityView.AnimationComponent");
            IntPtr paramClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.AnimParamHash");
            IntPtr stateClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.ResHandle.AnimationRes.AnimStateHash");
            if (animClass == IntPtr.Zero || paramClass == IntPtr.Zero || stateClass == IntPtr.Zero)
            {
                return;
            }

            int hashDown, hashForward, hashUp, hashStart, hashLoop;
            if (!this.TryReadAuraMonoStaticIntField(paramClass, new[] { "DownwardJigsaw" }, out hashDown)
                || !this.TryReadAuraMonoStaticIntField(paramClass, new[] { "ForwardJigsaw" }, out hashForward)
                || !this.TryReadAuraMonoStaticIntField(paramClass, new[] { "UpwardJigsaw" }, out hashUp))
            {
                return;
            }

            string startName = lean == 0 ? "DownwardJigsawStart" : "ForwardJigsawStart";
            string loopName = lean == 0 ? "DownwardJigsaw" : "ForwardJigsaw";
            if (!this.TryReadAuraMonoStaticIntField(stateClass, new[] { startName }, out hashStart)
                || !this.TryReadAuraMonoStaticIntField(stateClass, new[] { loopName }, out hashLoop))
            {
                return;
            }

            IntPtr play5 = this.FindAuraMonoMethodOnHierarchy(animClass, "PlayController", 5);
            IntPtr setBool = this.FindAuraMonoMethodOnHierarchy(animClass, "SetBool", 2);
            IntPtr playState = this.FindAuraMonoMethodOnHierarchy(animClass, "PlayState", 3);
            if (play5 == IntPtr.Zero || setBool == IntPtr.Zero)
            {
                return;
            }

            // ControllerFullNameId = 4 ints unpacked from the enum uint.
            int* id = stackalloc int[4];
            uint c = ActionPanelJigsawController;
            id[0] = (int)((c >> 25) & 0x7Fu);
            id[1] = (int)((c >> 15) & 0x3FFu);
            id[2] = (int)((c >> 10) & 0x1Fu);
            id[3] = (int)(c & 0x3FFu);
            bool fallback = true;
            float blend = 0.05f;
            int dst = hashStart;
            bool force = true;
            IntPtr* playArgs = stackalloc IntPtr[5];
            playArgs[0] = (IntPtr)id;
            playArgs[1] = (IntPtr)(&fallback);
            playArgs[2] = (IntPtr)(&blend);
            playArgs[3] = (IntPtr)(&dst);
            playArgs[4] = (IntPtr)(&force);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(play5, anim, (IntPtr)playArgs, ref exc);

            void SetLeanBool(int hash, bool on)
            {
                int h = hash;
                bool v = on;
                IntPtr* bArgs = stackalloc IntPtr[2];
                bArgs[0] = (IntPtr)(&h);
                bArgs[1] = (IntPtr)(&v);
                IntPtr excB = IntPtr.Zero;
                auraMonoRuntimeInvoke(setBool, anim, (IntPtr)bArgs, ref excB);
            }

            SetLeanBool(hashDown, lean == 0);
            SetLeanBool(hashForward, lean == 1);
            SetLeanBool(hashUp, false);

            if (playState != IntPtr.Zero)
            {
                int layer = 0;
                float t = 0f;
                int start = hashStart;
                int loop = hashLoop;
                IntPtr* sArgs = stackalloc IntPtr[3];
                sArgs[0] = (IntPtr)(&start);
                sArgs[1] = (IntPtr)(&layer);
                sArgs[2] = (IntPtr)(&t);
                IntPtr excS = IntPtr.Zero;
                auraMonoRuntimeInvoke(playState, anim, (IntPtr)sArgs, ref excS);
                sArgs[0] = (IntPtr)(&loop);
                auraMonoRuntimeInvoke(playState, anim, (IntPtr)sArgs, ref excS);
            }
        }

        // PlayerJigsawPuzzleMotion.OnMotionTick lean from (player.y - entity.y):
        //   > 0 or |d|<=0.1 → Downward (sit) | |d|<=0.5 → Forward (stand) | else Upward
        // FaceDirection aims at the same entity — pick one already in the look cone so the
        // turn is a no-op. Sit keeps self (dy=0 → Downward, zero aim).
        //
        // BuildComponent netIds often resolve locally (FaceDirection turns us) but remotes'
        // EntityUtil.GetEntity misses them → hide-handhold only. Prefer puzzle boards and
        // other players (always networked).
        private const float ActionPanelPuzzleAnchorScanMeters = 30f;
        private const float ActionPanelPuzzleLeanDownMaxAbs = 0.1f;
        private const float ActionPanelPuzzleLeanForwardMaxAbs = 0.5f;
        // ~25° half-angle — wider cones made FaceDirection spins obvious.
        private const float ActionPanelPuzzleLookDotMin = 0.9f;

        private static readonly string[] ActionPanelPuzzleBoardAnchorTypeNames = new string[]
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland.JigsawPuzzleComponent",
            "XDTLevelAndEntity.GamePlay.Component.Homeland.JigsawPuzzleComponent",
        };

        private static readonly string[] ActionPanelPuzzlePlayerAnchorTypeNames = new string[]
        {
            "XDTLevelAndEntity.Gameplay.Component.Player.RemotePlayerComponent",
            "XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
        };

        private static int ClassifyActionPanelPuzzleLean(float playerY, float entityY)
        {
            float num = playerY - entityY;
            if (num > 0f)
            {
                return 0;
            }

            float abs = num < 0f ? -num : num;
            if (abs <= ActionPanelPuzzleLeanDownMaxAbs)
            {
                return 0;
            }

            if (abs <= ActionPanelPuzzleLeanForwardMaxAbs)
            {
                return 1;
            }

            return 2;
        }

        private bool TryResolveActionPanelPuzzleLeanAnchor(IntPtr player, int desiredLean,
            Vector3 playerPos, bool havePos, out uint anchorNetId, out string kind)
        {
            anchorNetId = 0U;
            kind = "none";

            if (desiredLean == 0)
            {
                if (this.TryGetAuraMonoEntityNetIdFromComponent(player, out uint selfId) && selfId != 0U)
                {
                    anchorNetId = selfId;
                    kind = "self";
                    return true;
                }

                kind = "self-miss";
                return false;
            }

            if (!havePos)
            {
                kind = "no-pos";
                return false;
            }

            if (!this.TryGetActionPanelLookForward(out Vector3 forward))
            {
                kind = "no-forward";
                return false;
            }

            if (this.TryFindLookDirEntityForActionPanelLean(playerPos, forward, desiredLean,
                    out uint lookId, out string lookKind)
                && lookId != 0U)
            {
                anchorNetId = lookId;
                kind = lookKind;
                return true;
            }

            // Tight cone missed — allow any board in the front hemisphere with Forward height so
            // remotes still get lean (FaceDirection may turn a bit).
            if (this.TryFindFrontBoardForActionPanelLean(playerPos, forward, desiredLean,
                    out uint frontId, out string frontKind)
                && frontId != 0U)
            {
                anchorNetId = frontId;
                kind = frontKind;
                return true;
            }

            kind = "no-look-anchor";
            return false;
        }

        private bool TryGetActionPanelLookForward(out Vector3 forward)
        {
            forward = Vector3.forward;
            if (this.TryGetNoclipSelfAnchorPose(out _, out Quaternion selfRot, out bool hasRot, out _)
                && hasRot)
            {
                forward = selfRot * Vector3.forward;
            }
            else
            {
                try
                {
                    GameObject p = GetLocalPlayer();
                    if (p != null)
                    {
                        forward = p.transform.forward;
                    }
                }
                catch
                {
                    return false;
                }
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            forward.Normalize();
            return true;
        }

        private bool TryGetAuraMonoEntityNetIdFromComponent(IntPtr component, out uint netId)
        {
            netId = 0U;
            if (component == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(component, "entity", out IntPtr entity) || entity == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetAuraMonoEntityNetId(entity, out netId) && netId != 0U;
        }

        private bool TryFindLookDirEntityForActionPanelLean(Vector3 playerPos, Vector3 forward,
            int desiredLean, out uint boardNetId, out string kind)
        {
            boardNetId = 0U;
            kind = "none";
            if (!AuraMonoPinningAvailable || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                return false;
            }

            uint selfNetId = 0U;
            this.TryGetLocalPlayerNetId(out selfNetId);

            // Boards first (vanilla join path), then other players (always in EntityUtil on remotes).
            if (this.TryScoreLookDirAnchors(playerPos, forward, desiredLean, selfNetId,
                    ActionPanelPuzzleLookDotMin, ActionPanelPuzzleBoardAnchorTypeNames, "board",
                    out boardNetId, out kind))
            {
                return true;
            }

            return this.TryScoreLookDirAnchors(playerPos, forward, desiredLean, selfNetId,
                ActionPanelPuzzleLookDotMin, ActionPanelPuzzlePlayerAnchorTypeNames, "player",
                out boardNetId, out kind);
        }

        private bool TryFindFrontBoardForActionPanelLean(Vector3 playerPos, Vector3 forward,
            int desiredLean, out uint boardNetId, out string kind)
        {
            boardNetId = 0U;
            kind = "none";
            if (!AuraMonoPinningAvailable || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                return false;
            }

            uint selfNetId = 0U;
            this.TryGetLocalPlayerNetId(out selfNetId);
            return this.TryScoreLookDirAnchors(playerPos, forward, desiredLean, selfNetId,
                0f, ActionPanelPuzzleBoardAnchorTypeNames, "board-front",
                out boardNetId, out kind);
        }

        private bool TryGetLocalPlayerNetId(out uint netId)
        {
            netId = 0U;
            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages(
                "LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            if (playerClass == IntPtr.Zero || !AuraMonoPinningAvailable)
            {
                return false;
            }

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(playerClass,
                        out System.Collections.Generic.List<IntPtr> players, pins)
                    || players == null || players.Count == 0)
                {
                    return false;
                }

                return this.TryGetAuraMonoEntityNetIdFromComponent(players[0], out netId) && netId != 0U;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool TryScoreLookDirAnchors(Vector3 playerPos, Vector3 forward, int desiredLean,
            uint selfNetId, float minLookDot, string[] typeNames, string typeKind,
            out uint bestId, out string kind)
        {
            bestId = 0U;
            kind = "none";
            float bestScore = float.MaxValue;

            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>();
            try
            {
                for (int t = 0; t < typeNames.Length; t++)
                {
                    IntPtr klass = this.FindAuraMonoClassByFullName(typeNames[t]);
                    if (klass == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryAuraMonoGetComponentObjects(klass, out System.Collections.Generic.List<IntPtr> comps, pins)
                        || comps == null || comps.Count == 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < comps.Count; i++)
                    {
                        IntPtr comp = comps[i];
                        if (comp == IntPtr.Zero
                            || !this.TryGetMonoObjectMember(comp, "entity", out IntPtr entity) || entity == IntPtr.Zero
                            || !this.TryGetAuraMonoEntityNetId(entity, out uint netId) || netId == 0U
                            || (selfNetId != 0U && netId == selfNetId)
                            || !this.TryGetAuraMonoEntityPosition(entity, out Vector3 pos))
                        {
                            continue;
                        }

                        Vector3 delta = pos - playerPos;
                        float distSq = delta.sqrMagnitude;
                        if (distSq < 0.25f
                            || distSq > ActionPanelPuzzleAnchorScanMeters * ActionPanelPuzzleAnchorScanMeters)
                        {
                            continue;
                        }

                        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
                        float flatSq = flat.sqrMagnitude;
                        if (flatSq < 0.25f)
                        {
                            continue;
                        }

                        float lookDot = Vector3.Dot(flat / Mathf.Sqrt(flatSq), forward);
                        if (lookDot < minLookDot)
                        {
                            continue;
                        }

                        if (ClassifyActionPanelPuzzleLean(playerPos.y, pos.y) != desiredLean)
                        {
                            continue;
                        }

                        float dist = Mathf.Sqrt(distSq);
                        float score = dist / (0.05f + lookDot * lookDot);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestId = netId;
                            kind = typeKind + "-look";
                        }
                    }
                }

                return bestId != 0U;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }
    }
}
