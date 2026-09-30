using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // AVATAR STUDIO — pose, frame, face, zoom and rotation for the game's "Generate Avatar" panel.
    //
    // ── WHY THIS IS ALL CLIENT-SIDE ─────────────────────────────────────────────────────────────
    // PersonalInformationCreateHeadIconPanel renders a local GuiPlayer and, on Confirm, grabs a
    // SCREEN RECTANGLE of the rendered frame (CanvasUtility.ScreenShot -> ScreenCaptureUtil), uploads
    // it as a plain texture and sends only the photo id (EditAlternativeAvatarImages). The server
    // never learns which pose or face was on the model, so everything here only changes what the
    // model looks like at the moment of the capture. Nothing is sent.
    //
    // The panel itself offers 6 poses (TableSnapshotAction, driven by the animator int `Snapshot`;
    // 7+ have no animator state) and 8 faces. This adds, all verified live on 2026-09-30:
    //
    //   poses  GuiPlayer.PlayerAction(singleactionId) — the same cast the wardrobe and gacha
    //          previews use — plays any Singleaction emote on the model;
    //   frame  GuiPlayer.SetAnimationSpeed(0) freezes it, then AnimationComponent.PlayState(hash, 0,
    //          normalizedTime) + Evaluate(0) scrubs the CURRENT state to any frame;
    //   faces  the model's legacy `face` Animation carries 63 anim_facestation_* clips; Play(name)
    //          holds, because the panel parks the game's expression on the Manual layer and the
    //          game only calls PlayClip again when its own expression button is pressed;
    //   zoom   AvatarCamera.fieldOfView (the panel never writes it);
    //   yaw    the `avatar_model` pivot above the skeleton (the panel rotates the SKELETON through
    //          GuiPlayer.SetRotation, so the two compose instead of fighting);
    //   pan    AvatarCamera's localPosition along its own screen axes — the panel's drag clamps
    //          `camera@go` (the PARENT) to ±0.1, so an offset on the child adds to it. Sliders plus a
    //          right-button drag inside the capture circle; no detour of OnModelSwapHandler.
    //
    // ── THE CAPTURE ─────────────────────────────────────────────────────────────────────────────
    // A screen grab includes every overlay canvas, so this window would be baked into the avatar if
    // it overlapped the capture rectangle. The panel switches its `blackbg@go` off for exactly the
    // capture (ConfirmBtnHandler .. the ScreenShot callback) — the window hides while it is off.
    //
    // ── MONO OBJECT LIFETIME ────────────────────────────────────────────────────────────────────
    // No MonoObject* is kept across frames. Every model command re-resolves panel -> _characterModel
    // -> _guiPlayer -> animation inside one synchronous scope, pinning each hop (AGENTS.md §11).
    // Only Unity objects (camera, pivot, face Animation) are cached, and those are null-checked on
    // every use.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogAvatarStudio = false;

        private const string AvatarStudioTag = "AvatarStudio";

        // A PLAIN full name — TryCreateAuraMonoSystemTypeObject splits namespace and class, so an
        // assembly suffix would make the panel read as closed forever (FurnitureDyePanelFeature.cs).
        private const string AvatarStudioPanelTypeName = "XDTGame.UI.Panel.PersonalInformationCreateHeadIconPanel";
        private const string AvatarStudioPanelGoName = "PersonalInformationCreateHeadIconPanel(Clone)";
        private const string AvatarStudioCameraName = "AvatarCamera";
        private const string AvatarStudioPivotName = "avatar_model";
        private const string AvatarStudioCaptureMarkerName = "blackbg@go";
        private const string AvatarStudioCaptureRectName = "screenShot@t";
        private const string AvatarStudioSkeletonName = "p_player_ui_skeleton(Clone)";
        private const string AvatarStudioFaceName = "face";
        private const string AvatarStudioFacePrefix = "anim_facestation_";
        private const string AvatarStudioLoopSuffix = "_loop";

        // UIView.Open dispatches Open AFTER OnStart (the model exists by then); Close dispatches
        // Closing BEFORE OnStop destroys the model. Both carry only a System.Type the event engine
        // cannot read (docs/GAME_EVENTS.md), so they are used as "some panel changed" triggers and
        // the answer comes from one UIManager.GetView probe.
        private const string AvatarStudioOpenEventName = "XDTGame.Framework.UI.UIPanelOpenEvent";
        private const string AvatarStudioClosingEventName = "XDTGame.Framework.UI.UIPanelClosingEvent";
        private const string AvatarStudioCloseEventName = "XDTGame.Framework.UI.UIPanelCloseEvent";

        private const float AvatarStudioPollIntervalSec = 1f;        // only while the hooks are not live
        private const float AvatarStudioLiveSyncIntervalSec = 0.2f;  // frame readout while playing
        private const float AvatarStudioFrameRate = 30f;

        internal const float AvatarStudioZoomMin = 0.25f;
        internal const float AvatarStudioZoomMax = 3f;
        internal const float AvatarStudioYawMax = 180f;

        // World units either way. The panel's own drag stops at ±0.1; at 0.25x zoom the whole body
        // needs well over 1 to frame from head to feet.
        internal const float AvatarStudioPanMax = 1.5f;

        // XDAnimationStateInfo: auto-property backing fields in declaration order — fullPathHash,
        // shortNameHash, normalizedTime, length, speed, speedMultiplier, tagHash, loop. Offsets
        // verified live against a known state (length 17.1 s, normalizedTime read back after a seek).
        private const int AvatarStudioStateShortHashOffset = 4;
        private const int AvatarStudioStateTimeOffset = 8;
        private const int AvatarStudioStateLengthOffset = 12;
        private const int AvatarStudioStateLoopOffset = 28;

        internal enum AvatarStudioPoseKind
        {
            Snapshot,
            Emote
        }

        internal sealed class AvatarStudioPose
        {
            public AvatarStudioPoseKind Kind;
            public int Id;
            public string Label;
        }

        private readonly List<AvatarStudioPose> avatarStudioPoses = new List<AvatarStudioPose>();
        private readonly List<string> avatarStudioFaceClips = new List<string>();
        private readonly List<string> avatarStudioFaceLabels = new List<string>();

        private bool avatarStudioHooksRegistered;
        private bool avatarStudioProbePending = true;
        private float avatarStudioNextPollAt;
        private float avatarStudioNextLiveSyncAt;
        private bool avatarStudioPanelOpen;
        private bool avatarStudioDismissed;
        private AuraMonoObjectCache avatarStudioPanelType;
        private FeatureBreakerState avatarStudioBreaker;

        // Unity-side handles into the open panel (cleared on close).
        private GameObject avatarStudioPanelRoot;
        private Camera avatarStudioCamera;
        private Transform avatarStudioPivot;
        private GameObject avatarStudioCaptureMarker;
        private Animation avatarStudioFaceAnimation;
        private float avatarStudioBaseFov;
        private Quaternion avatarStudioBaseRotation;
        private bool avatarStudioViewCaptured;

        // Pan: an offset on AvatarCamera itself. The panel's drag writes the PARENT (camera@go), so
        // the two compose and the game's drag keeps working inside its own ±0.1.
        private RectTransform avatarStudioCaptureRect;
        private Vector3 avatarStudioBaseCameraLocalPos;
        private Vector3 avatarStudioPanRightLocal;
        private Vector3 avatarStudioPanUpLocal;
        private bool avatarStudioPanDragging;
        private Vector2 avatarStudioPanDragLast;

        // What the window shows.
        private float avatarStudioZoom = 1f;
        private float avatarStudioYaw;
        private Vector2 avatarStudioPan;
        private bool avatarStudioPaused;
        private int avatarStudioStateHash;
        private bool avatarStudioStateLoop;
        private int avatarStudioFrameCount = 1;
        private int avatarStudioFrame = 1;
        private string avatarStudioStatus = string.Empty;

        internal bool AvatarStudioPanelOpen
        {
            get { return this.avatarStudioPanelOpen; }
        }

        internal bool AvatarStudioWanted
        {
            get { return this.avatarStudioPanelOpen && !this.avatarStudioDismissed && !this.IsAvatarStudioCapturing(); }
        }

        // ----------------------------------------------------------------------------------------
        // Detection
        // ----------------------------------------------------------------------------------------

        // Pure bookkeeping: the event engine installs the detours on the world-ready gate itself.
        private void EnsureAvatarStudioHooks()
        {
            if (this.avatarStudioHooksRegistered)
            {
                return;
            }

            this.avatarStudioHooksRegistered = true;
            bool ok = this.RegisterGameEventHook(AvatarStudioOpenEventName, 0, this.OnAvatarStudioPanelEvent)
                    & this.RegisterGameEventHook(AvatarStudioClosingEventName, 0, this.OnAvatarStudioPanelEvent)
                    & this.RegisterGameEventHook(AvatarStudioCloseEventName, 0, this.OnAvatarStudioPanelEvent);
            if (!ok)
            {
                FeatureLog.Fail(AvatarStudioTag, "UI panel event hooks refused — falling back to a 1 s GetView poll.");
            }
        }

        private void OnAvatarStudioPanelEvent(GameEventSnapshot e)
        {
            this.avatarStudioProbePending = true;
        }

        private bool AvatarStudioHooksLive()
        {
            return this.IsGameEventHookInstalled(AvatarStudioOpenEventName)
                && this.IsGameEventHookInstalled(AvatarStudioClosingEventName);
        }

        // Called every frame from the window driver. Cheap in the steady state: a flag test and, while
        // the panel is up, one Unity activeInHierarchy read.
        private void TickAvatarStudioDetection()
        {
            this.EnsureAvatarStudioHooks();

            if (!this.IsWorldReady)
            {
                if (this.avatarStudioPanelOpen)
                {
                    this.OnAvatarStudioPanelClosed("world unloaded");
                }
                this.avatarStudioProbePending = true;
                return;
            }

            float now = Time.unscaledTime;
            if (this.avatarStudioPanelOpen && !this.IsAvatarStudioPanelGoAlive())
            {
                this.OnAvatarStudioPanelClosed("panel object gone");
            }

            if (!this.AvatarStudioHooksLive() && now >= this.avatarStudioNextPollAt)
            {
                this.avatarStudioNextPollAt = now + AvatarStudioPollIntervalSec;
                this.avatarStudioProbePending = true;
            }

            if (!this.avatarStudioProbePending || !this.avatarStudioBreaker.ShouldRun(now))
            {
                return;
            }
            this.avatarStudioProbePending = false;

            try
            {
                bool open = this.TryGetOpenAvatarStudioPanel(out IntPtr _);
                if (open && !this.avatarStudioPanelOpen)
                {
                    this.OnAvatarStudioPanelOpened();
                }
                else if (!open && this.avatarStudioPanelOpen)
                {
                    this.OnAvatarStudioPanelClosed("panel closed");
                }
                this.avatarStudioBreaker.Success();
            }
            catch (Exception ex)
            {
                this.avatarStudioBreaker.Failure(AvatarStudioTag, ex, now);
            }
        }

        // UIManager.GetView(Type) is non-null only for a panel that is open and not closing.
        private unsafe bool TryGetOpenAvatarStudioPanel(out IntPtr panel)
        {
            panel = IntPtr.Zero;
            if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj)
                || uiManagerObj == IntPtr.Zero || this.persistentHudGetViewMethod == IntPtr.Zero)
            {
                return false;
            }

            if (!this.avatarStudioPanelType.TryGet(out IntPtr typeObj))
            {
                if (!this.TryCreateAuraMonoSystemTypeObject(AvatarStudioPanelTypeName, out typeObj)
                    || typeObj == IntPtr.Zero)
                {
                    FeatureLog.Fail(AvatarStudioTag, "System.Type for " + AvatarStudioPanelTypeName
                        + " unresolved — the avatar panel can never be detected (game update?)");
                    return false;
                }
                this.avatarStudioPanelType.Set(typeObj);
                if (!this.avatarStudioPanelType.TryGet(out typeObj))
                {
                    return false; // never use an unpinned type object
                }
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            if (!TryAuraInvoke(this.persistentHudGetViewMethod, uiManagerObj, (IntPtr)args,
                               out IntPtr view, out string _) || view == IntPtr.Zero)
            {
                return false;
            }

            panel = view;
            return true;
        }

        private bool IsAvatarStudioPanelGoAlive()
        {
            try
            {
                return this.avatarStudioPanelRoot != null && this.avatarStudioPanelRoot.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private bool IsAvatarStudioCapturing()
        {
            try
            {
                return this.avatarStudioCaptureMarker != null && !this.avatarStudioCaptureMarker.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private void OnAvatarStudioPanelOpened()
        {
            GameObject root = GameObject.Find(AvatarStudioPanelGoName);
            if (root == null)
            {
                // GetView says open but the object is not up yet — ask again next second.
                this.avatarStudioNextPollAt = Time.unscaledTime + AvatarStudioPollIntervalSec;
                FeatureLog.Fail(AvatarStudioTag, AvatarStudioPanelGoName + " not found although the panel is open");
                return;
            }

            this.avatarStudioPanelRoot = root;
            this.avatarStudioCamera = null;
            this.avatarStudioPivot = null;
            this.avatarStudioCaptureMarker = null;
            this.avatarStudioFaceAnimation = null;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (this.avatarStudioCamera == null && n == AvatarStudioCameraName)
                {
                    this.avatarStudioCamera = t.GetComponent<Camera>();
                }
                else if (this.avatarStudioPivot == null && n == AvatarStudioPivotName)
                {
                    this.avatarStudioPivot = t;
                }
                else if (this.avatarStudioCaptureMarker == null && n == AvatarStudioCaptureMarkerName)
                {
                    this.avatarStudioCaptureMarker = t.gameObject;
                }
                else if (this.avatarStudioCaptureRect == null && n == AvatarStudioCaptureRectName)
                {
                    this.avatarStudioCaptureRect = t.TryCast<RectTransform>();
                }
            }

            this.avatarStudioViewCaptured = false;
            if (this.avatarStudioCamera != null && this.avatarStudioPivot != null)
            {
                this.avatarStudioBaseFov = this.avatarStudioCamera.fieldOfView;
                this.avatarStudioBaseRotation = this.avatarStudioPivot.localRotation;

                // The camera's own screen axes, expressed in its parent's space: panning along them
                // moves the picture left/right/up/down whatever the rig's orientation is.
                Transform camT = this.avatarStudioCamera.transform;
                Transform parent = camT.parent;
                this.avatarStudioBaseCameraLocalPos = camT.localPosition;
                this.avatarStudioPanRightLocal = (parent != null ? parent.InverseTransformDirection(camT.right) : camT.right).normalized;
                this.avatarStudioPanUpLocal = (parent != null ? parent.InverseTransformDirection(camT.up) : camT.up).normalized;
                this.avatarStudioViewCaptured = true;
            }
            this.avatarStudioPan = Vector2.zero;
            this.avatarStudioPanDragging = false;
            this.CaptureAvatarStudioPlanes(root);   // AvatarStudioBackgrounds.cs

            this.avatarStudioPanelOpen = true;
            this.avatarStudioDismissed = false;
            this.avatarStudioZoom = 1f;
            this.avatarStudioYaw = 0f;
            this.avatarStudioPaused = false;
            this.avatarStudioStateHash = 0;
            this.avatarStudioFrameCount = 1;
            this.avatarStudioFrame = 1;
            this.avatarStudioStatus = string.Empty;

            if (this.avatarStudioPoses.Count == 0)
            {
                this.LoadAvatarStudioCatalog();
            }

            FeatureLog.Life(AvatarStudioTag, "avatar panel opened — camera=" + (this.avatarStudioCamera != null)
                + " pivot=" + (this.avatarStudioPivot != null) + " captureMarker=" + (this.avatarStudioCaptureMarker != null)
                + ", " + this.avatarStudioPoses.Count + " poses");
            if (this.avatarStudioCaptureMarker == null)
            {
                FeatureLog.Fail(AvatarStudioTag, AvatarStudioCaptureMarkerName
                    + " not found — the window cannot hide itself for the capture; keep it off the avatar circle");
            }
        }

        private void OnAvatarStudioPanelClosed(string reason)
        {
            this.RestoreAvatarStudioView();
            this.ReleaseAvatarStudioBackgrounds();   // AvatarStudioBackgrounds.cs
            this.avatarStudioPanelOpen = false;
            this.avatarStudioPanelRoot = null;
            this.avatarStudioCamera = null;
            this.avatarStudioPivot = null;
            this.avatarStudioCaptureMarker = null;
            this.avatarStudioCaptureRect = null;
            this.avatarStudioFaceAnimation = null;
            this.avatarStudioViewCaptured = false;
            this.avatarStudioPanDragging = false;
            FeatureLog.Life(AvatarStudioTag, "avatar panel closed (" + reason + ")");
        }

        // The panel object can be pooled and reopened, so the camera and pivot go back to what the
        // game set before this window touched them.
        private void RestoreAvatarStudioView()
        {
            if (!this.avatarStudioViewCaptured)
            {
                return;
            }
            try
            {
                if (this.avatarStudioCamera != null)
                {
                    this.avatarStudioCamera.fieldOfView = this.avatarStudioBaseFov;
                    this.avatarStudioCamera.transform.localPosition = this.avatarStudioBaseCameraLocalPos;
                }
                if (this.avatarStudioPivot != null)
                {
                    this.avatarStudioPivot.localRotation = this.avatarStudioBaseRotation;
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "view restore threw: " + ex.Message);
            }
        }

        internal void DismissAvatarStudio()
        {
            this.avatarStudioDismissed = true;
            FeatureLog.Life(AvatarStudioTag, "window closed by the user (returns on the next panel open)");
        }

        // ----------------------------------------------------------------------------------------
        // Catalogue — TableData.TableSnapshotActions + TableData.TableSingleactions, read once
        // ----------------------------------------------------------------------------------------

        internal List<AvatarStudioPose> AvatarStudioPoses
        {
            get { return this.avatarStudioPoses; }
        }

        private void LoadAvatarStudioCatalog()
        {
            this.avatarStudioPoses.Clear();
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || !AuraMonoPinningAvailable)
                {
                    FeatureLog.Fail(AvatarStudioTag, "catalogue: AuraMono or pinning unavailable");
                    return;
                }

                // TableData lives in the GLOBAL namespace of the EcsClient image (see
                // FurnitureDyePanelFeature.TryGetFurnitureDyeCostTable for why this lookup).
                IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
                if (tableData == IntPtr.Zero)
                {
                    FeatureLog.Fail(AvatarStudioTag, "catalogue: TableData class unresolved");
                    return;
                }

                List<int> snapshots = new List<int>();
                this.VisitAvatarStudioTableRows(tableData, "TableSnapshotActions", row =>
                {
                    if (this.TryGetMonoInt32Member(row, "snapshotId", out int snapshotId) && snapshotId > 0
                        && !snapshots.Contains(snapshotId))
                    {
                        snapshots.Add(snapshotId);
                    }
                });
                snapshots.Sort();
                for (int i = 0; i < snapshots.Count; i++)
                {
                    this.avatarStudioPoses.Add(new AvatarStudioPose
                    {
                        Kind = AvatarStudioPoseKind.Snapshot,
                        Id = snapshots[i],
                        Label = this.L("Portrait pose") + " " + snapshots[i],
                    });
                }

                List<AvatarStudioPose> emotes = new List<AvatarStudioPose>();
                this.VisitAvatarStudioTableRows(tableData, "TableSingleactions", row =>
                {
                    if (!this.TryGetMonoInt32Member(row, "id", out int id) || id <= 0)
                    {
                        return;
                    }
                    // `name` is a property over TableData.Localize(_name): the game's own language.
                    this.TryGetMonoStringMember(row, "name", out string name);
                    this.TryGetMonoInt32Member(row, "isLoop", out int isLoop);
                    string label = string.IsNullOrWhiteSpace(name) ? this.L("Action") + " " + id : name.Trim();
                    if (isLoop == 2)
                    {
                        label += " " + this.L("(loop)");
                    }
                    else if (isLoop == 1)
                    {
                        label += " " + this.L("(posture)");
                    }
                    emotes.Add(new AvatarStudioPose { Kind = AvatarStudioPoseKind.Emote, Id = id, Label = label });
                });
                emotes.Sort((a, b) => a.Id.CompareTo(b.Id));

                // Several actions share a display name ("Thinking" x4); the id keeps them apart.
                Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < emotes.Count; i++)
                {
                    seen.TryGetValue(emotes[i].Label, out int n);
                    seen[emotes[i].Label] = n + 1;
                }
                for (int i = 0; i < emotes.Count; i++)
                {
                    if (seen[emotes[i].Label] > 1)
                    {
                        emotes[i].Label += " #" + emotes[i].Id;
                    }
                }
                this.avatarStudioPoses.AddRange(emotes);

                FeatureLog.Life(AvatarStudioTag, "catalogue: " + snapshots.Count + " portrait poses, "
                    + emotes.Count + " actions");
            }
            catch (Exception ex)
            {
                this.avatarStudioPoses.Clear();
                FeatureLog.Fail(AvatarStudioTag, "catalogue read threw: " + ex.Message);
            }
        }

        // Dictionary<int, TRow> static on TableData: walk its Values (enumerating the dictionary
        // itself yields KeyValuePair boxes). Every row is pinned for the whole visit.
        private void VisitAvatarStudioTableRows(IntPtr tableDataClass, string fieldName, Action<IntPtr> visit)
        {
            if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, fieldName, out IntPtr dict) || dict == IntPtr.Zero)
            {
                FeatureLog.Fail(AvatarStudioTag, "catalogue: TableData." + fieldName + " unavailable");
                return;
            }

            List<uint> pins = new List<uint>();
            pins.Add(AuraMonoPinNew(dict));
            try
            {
                if (!this.TryGetMonoObjectMember(dict, "Values", out IntPtr values) || values == IntPtr.Zero)
                {
                    FeatureLog.Fail(AvatarStudioTag, "catalogue: " + fieldName + ".Values unavailable");
                    return;
                }
                pins.Add(AuraMonoPinNew(values));

                List<IntPtr> rows = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(values, rows, pins))
                {
                    FeatureLog.Fail(AvatarStudioTag, "catalogue: " + fieldName + " enumeration failed");
                    return;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != IntPtr.Zero)
                    {
                        visit(rows[i]);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Face clips — the model's own legacy Animation (Unity side, no Mono)
        // ----------------------------------------------------------------------------------------

        internal List<string> AvatarStudioFaceLabels
        {
            get { return this.avatarStudioFaceLabels; }
        }

        private Animation ResolveAvatarStudioFace()
        {
            try
            {
                if (this.avatarStudioFaceAnimation != null)
                {
                    return this.avatarStudioFaceAnimation;
                }
                if (this.avatarStudioPanelRoot == null)
                {
                    return null;
                }

                // The skeleton is spawned by GuiPlayer after the panel opens and loads async, so this
                // is resolved lazily rather than in OnAvatarStudioPanelOpened.
                foreach (Animation a in this.avatarStudioPanelRoot.GetComponentsInChildren<Animation>(true))
                {
                    if (a.gameObject.name == AvatarStudioFaceName && a.transform.parent != null
                        && a.transform.parent.name == AvatarStudioSkeletonName)
                    {
                        this.avatarStudioFaceAnimation = a;
                        return a;
                    }
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "face lookup threw: " + ex.Message);
            }
            return null;
        }

        // Fills the face list once; returns true when there is a list to show.
        internal bool EnsureAvatarStudioFaceClips()
        {
            if (this.avatarStudioFaceClips.Count > 0)
            {
                return true;
            }

            Animation face = this.ResolveAvatarStudioFace();
            if (face == null)
            {
                return false;
            }

            try
            {
                List<string> loops = new List<string>();
                List<string> others = new List<string>();
                var e = face.GetEnumerator();
                while (e.MoveNext())
                {
                    AnimationState state = e.Current.TryCast<AnimationState>();
                    string n = state != null ? state.name : null;
                    if (string.IsNullOrEmpty(n) || !n.StartsWith(AvatarStudioFacePrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    (n.EndsWith(AvatarStudioLoopSuffix, StringComparison.Ordinal) ? loops : others).Add(n);
                }
                loops.Sort(StringComparer.OrdinalIgnoreCase);
                others.Sort(StringComparer.OrdinalIgnoreCase);

                HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
                foreach (string clip in loops)
                {
                    this.AddAvatarStudioFaceClip(clip, labels);
                }
                foreach (string clip in others)
                {
                    this.AddAvatarStudioFaceClip(clip, labels);
                }

                FeatureLog.Once(AvatarStudioTag, "faces", "face clips: " + this.avatarStudioFaceClips.Count
                    + " (" + loops.Count + " loops)");
            }
            catch (Exception ex)
            {
                this.avatarStudioFaceClips.Clear();
                this.avatarStudioFaceLabels.Clear();
                FeatureLog.Fail(AvatarStudioTag, "face clip list threw: " + ex.Message);
            }
            return this.avatarStudioFaceClips.Count > 0;
        }

        private void AddAvatarStudioFaceClip(string clip, HashSet<string> labels)
        {
            string label = clip.Substring(AvatarStudioFacePrefix.Length);
            if (label.EndsWith(AvatarStudioLoopSuffix, StringComparison.Ordinal))
            {
                label = label.Substring(0, label.Length - AvatarStudioLoopSuffix.Length);
            }
            if (!labels.Add(label))
            {
                label = clip; // a stripped name collided — show the clip as it is
            }
            this.avatarStudioFaceClips.Add(clip);
            this.avatarStudioFaceLabels.Add(label);
        }

        internal void ApplyAvatarStudioFace(int index)
        {
            if (index < 0 || index >= this.avatarStudioFaceClips.Count)
            {
                return;
            }

            string clip = this.avatarStudioFaceClips[index];
            try
            {
                Animation face = this.ResolveAvatarStudioFace();
                if (face == null)
                {
                    this.SetAvatarStudioStatus("Face: the model is still loading");
                    return;
                }
                bool ok = face.Play(clip);
                this.SetAvatarStudioStatus(ok ? "Face: " + this.avatarStudioFaceLabels[index] : "Face refused: " + clip);
                if (!ok)
                {
                    FeatureLog.Fail(AvatarStudioTag, "face Play refused " + clip);
                }
            }
            catch (Exception ex)
            {
                this.SetAvatarStudioStatus("Face failed");
                FeatureLog.Fail(AvatarStudioTag, "face Play threw for " + clip + ": " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Camera zoom and model yaw — Unity side
        // ----------------------------------------------------------------------------------------

        internal float AvatarStudioZoom
        {
            get { return this.avatarStudioZoom; }
        }

        internal float AvatarStudioYaw
        {
            get { return this.avatarStudioYaw; }
        }

        internal void SetAvatarStudioZoom(float zoom)
        {
            this.avatarStudioZoom = Mathf.Clamp(zoom, AvatarStudioZoomMin, AvatarStudioZoomMax);
            if (!this.avatarStudioViewCaptured || this.avatarStudioCamera == null)
            {
                return;
            }
            try
            {
                // Zoom is a lens change: higher zoom = narrower field of view.
                this.avatarStudioCamera.fieldOfView =
                    Mathf.Clamp(this.avatarStudioBaseFov / this.avatarStudioZoom, 1f, 120f);
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "zoom threw: " + ex.Message);
            }
        }

        internal void SetAvatarStudioYaw(float yaw)
        {
            this.avatarStudioYaw = Mathf.Clamp(yaw, -AvatarStudioYawMax, AvatarStudioYawMax);
            if (!this.avatarStudioViewCaptured || this.avatarStudioPivot == null)
            {
                return;
            }
            try
            {
                // Relative to the pivot's own rest rotation (it is not identity: ~(3, 180, 0)).
                this.avatarStudioPivot.localRotation =
                    this.avatarStudioBaseRotation * Quaternion.Euler(0f, this.avatarStudioYaw, 0f);
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "rotation threw: " + ex.Message);
            }
        }

        internal void ResetAvatarStudioView()
        {
            this.SetAvatarStudioZoom(1f);
            this.SetAvatarStudioYaw(0f);
            this.SetAvatarStudioPan(Vector2.zero);
        }

        internal Vector2 AvatarStudioPan
        {
            get { return this.avatarStudioPan; }
        }

        // Positive x moves the MODEL right on screen (the camera goes left), positive y moves it up.
        internal void SetAvatarStudioPan(Vector2 pan)
        {
            this.avatarStudioPan = new Vector2(
                Mathf.Clamp(pan.x, -AvatarStudioPanMax, AvatarStudioPanMax),
                Mathf.Clamp(pan.y, -AvatarStudioPanMax, AvatarStudioPanMax));
            if (!this.avatarStudioViewCaptured || this.avatarStudioCamera == null)
            {
                return;
            }
            try
            {
                this.avatarStudioCamera.transform.localPosition = this.avatarStudioBaseCameraLocalPos
                    - (this.avatarStudioPanRightLocal * this.avatarStudioPan.x)
                    - (this.avatarStudioPanUpLocal * this.avatarStudioPan.y);
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "pan threw: " + ex.Message);
            }
        }

        // Right-button drag inside the capture circle. Left button stays the panel's own (its ±0.1
        // drag), and a press on this window never starts a pan. Polled like the kit's window drag.
        internal void ProcessAvatarStudioPanDrag(bool pointerOverWindow)
        {
            try
            {
                if (!this.avatarStudioViewCaptured || this.avatarStudioCamera == null)
                {
                    this.avatarStudioPanDragging = false;
                    return;
                }

                Vector3 m3 = Input.mousePosition;
                Vector2 mouse = new Vector2(m3.x, m3.y);
                if (!this.avatarStudioPanDragging)
                {
                    if (Input.GetMouseButtonDown(1) && !pointerOverWindow && this.IsInAvatarStudioCaptureArea(mouse))
                    {
                        this.avatarStudioPanDragging = true;
                        this.avatarStudioPanDragLast = mouse;
                    }
                    return;
                }

                if (!Input.GetMouseButton(1))
                {
                    this.avatarStudioPanDragging = false;
                    return;
                }

                Vector2 delta = mouse - this.avatarStudioPanDragLast;
                this.avatarStudioPanDragLast = mouse;
                if (delta.x == 0f && delta.y == 0f)
                {
                    return;
                }

                // The model follows the cursor: one screen pixel is this many world units at the
                // model's depth, so the grab feels the same at every zoom.
                this.SetAvatarStudioPan(this.avatarStudioPan + (delta * this.AvatarStudioUnitsPerPixel()));
            }
            catch (Exception ex)
            {
                this.avatarStudioPanDragging = false;
                FeatureLog.Fail(AvatarStudioTag, "pan drag threw: " + ex.Message);
            }
        }

        private bool IsInAvatarStudioCaptureArea(Vector2 mouse)
        {
            if (this.avatarStudioCaptureRect != null)
            {
                // The panel's canvas is ScreenSpaceOverlay: no camera for the hit test.
                return RectTransformUtility.RectangleContainsScreenPoint(this.avatarStudioCaptureRect, mouse, null);
            }
            return mouse.x < Screen.width * 0.6f; // the capture circle lives on the left
        }

        private float AvatarStudioUnitsPerPixel()
        {
            Transform camT = this.avatarStudioCamera.transform;
            float depth = 1f;
            if (this.avatarStudioPivot != null)
            {
                depth = Vector3.Dot(this.avatarStudioPivot.position - camT.position, camT.forward);
                if (!(depth > 0.01f))
                {
                    depth = Mathf.Max(0.01f, Vector3.Distance(this.avatarStudioPivot.position, camT.position));
                }
            }
            float halfFov = this.avatarStudioCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            return (2f * depth * Mathf.Tan(halfFov)) / Mathf.Max(1f, Screen.height);
        }

        // ----------------------------------------------------------------------------------------
        // Model commands — GuiPlayer / AnimationComponent through AuraMono
        // ----------------------------------------------------------------------------------------

        internal bool AvatarStudioPaused
        {
            get { return this.avatarStudioPaused; }
        }

        internal int AvatarStudioFrame
        {
            get { return this.avatarStudioFrame; }
        }

        internal int AvatarStudioFrameCount
        {
            get { return this.avatarStudioFrameCount; }
        }

        internal string AvatarStudioStatus
        {
            get { return this.avatarStudioStatus; }
        }

        private void SetAvatarStudioStatus(string status)
        {
            this.avatarStudioStatus = status ?? string.Empty;
            FeatureLog.Detail(AvatarStudioTag, MasterLogAvatarStudio, this.avatarStudioStatus);
        }

        private delegate bool AvatarStudioModelAction(IntPtr guiPlayer, IntPtr animation, out string status);

        // Resolves the model chain afresh, pins every hop, runs `action`, frees the pins. Nothing it
        // touches outlives the call.
        private bool RunAvatarStudioModelAction(string what, bool needAnimation, AvatarStudioModelAction action)
        {
            string status;
            bool ok = false;
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoObjectGetClass == null || !AuraMonoPinningAvailable)
                {
                    status = "AuraMono or pinning unavailable";
                }
                else if (!this.TryGetOpenAvatarStudioPanel(out IntPtr panel))
                {
                    status = "the avatar panel is not open";
                }
                else
                {
                    pins.Add(AuraMonoPinNew(panel));
                    if (!this.TryGetMonoObjectMember(panel, "_characterModel", out IntPtr model) || model == IntPtr.Zero)
                    {
                        status = "_characterModel unavailable";
                    }
                    else
                    {
                        pins.Add(AuraMonoPinNew(model));
                        IntPtr animation = IntPtr.Zero;
                        if (this.TryGetMonoObjectMember(model, "_guiPlayer", out IntPtr component) && component != IntPtr.Zero)
                        {
                            pins.Add(AuraMonoPinNew(component));
                            if (this.TryGetMonoObjectMember(component, "animation", out animation) && animation != IntPtr.Zero)
                            {
                                pins.Add(AuraMonoPinNew(animation));
                            }
                        }

                        if (needAnimation && animation == IntPtr.Zero)
                        {
                            status = "the model is still loading";
                        }
                        else
                        {
                            ok = action(model, animation, out status);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            if (!ok)
            {
                FeatureLog.Fail(AvatarStudioTag, what + " failed: " + status);
                this.SetAvatarStudioStatus(what + " failed: " + status);
            }
            return ok;
        }

        // Resolves on the OBJECT's class (never a name-resolved one) — [[auramono-invoke-resolve-on-object-class]].
        private bool InvokeAvatarStudio(IntPtr obj, string method, int argc, IntPtr args, out IntPtr result, out string status)
        {
            result = IntPtr.Zero;
            IntPtr klass = obj == IntPtr.Zero ? IntPtr.Zero : auraMonoObjectGetClass(obj);
            IntPtr m = klass == IntPtr.Zero ? IntPtr.Zero : this.FindAuraMonoMethodOnHierarchy(klass, method, argc);
            if (m == IntPtr.Zero)
            {
                status = method + "(" + argc + ") unresolved";
                return false;
            }
            if (!TryAuraInvoke(m, obj, args, out result, out string error))
            {
                status = method + ": " + error;
                return false;
            }
            status = "ok";
            return true;
        }

        private unsafe bool SetAvatarStudioSpeed(IntPtr guiPlayer, float speed, out string status)
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&speed);
            return this.InvokeAvatarStudio(guiPlayer, "SetAnimationSpeed", 1, (IntPtr)args, out IntPtr _, out status);
        }

        // ── Why a frozen emote used to snap back after a while ──────────────────────────────────
        // Freezing the animator does not freeze the ACTION that plays the emote. GuiPlayer.
        // PlayerAction casts a GuiSocialAction (timeOut = float.MaxValue) whose SequenceTrack runs
        // the real clip — PlayerSocialAction, timeOut = 35 s — and every ActionClip counts
        // `_safe_time_check` down by the graph's deltaTime, in real time, whatever the animator
        // speed. At zero the clip ends, the controller leaves the emote and the model falls back to
        // the panel's snapshot pose (reported 2026-09-30: "it reset itself after a while").
        //
        // So while paused, the running clip's safety timer is parked at a huge value, and given
        // back its own full `duration` (= timeOut) on Play so the game's safety net works again.
        // Only a float is written — never a reference (no write barrier on this build).
        //
        // Chain, verified with mono.describe on the running build: GuiPlayerComponent._behaveGraph
        // -> ActorActionGraph.abilityCaster -> AbilityCaster._actionClip (GuiSocialAction)
        // -> _sequenceTrack -> _segment (ActionTrack.ClipWrapper) -> clip -> ActionClip._safe_time_check.
        private const float AvatarStudioHeldTimeout = 1000000f;

        private void HoldAvatarStudioActionTimeout(IntPtr guiPlayer, bool hold)
        {
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryGetMonoObjectMember(guiPlayer, "_guiPlayer", out IntPtr component) || component == IntPtr.Zero)
                {
                    return;
                }
                pins.Add(AuraMonoPinNew(component));
                if (!this.TryGetMonoObjectMember(component, "_behaveGraph", out IntPtr graph) || graph == IntPtr.Zero)
                {
                    return;
                }
                pins.Add(AuraMonoPinNew(graph));
                if (!this.TryGetMonoObjectMember(graph, "abilityCaster", out IntPtr caster) || caster == IntPtr.Zero)
                {
                    return;
                }
                pins.Add(AuraMonoPinNew(caster));
                if (!this.TryGetMonoObjectMember(caster, "_actionClip", out IntPtr outer) || outer == IntPtr.Zero)
                {
                    return; // nothing is being cast (a portrait pose) — nothing can time out
                }
                pins.Add(AuraMonoPinNew(outer));
                this.SetAvatarStudioClipTimeout(outer, hold);

                if (this.TryGetMonoObjectMember(outer, "_sequenceTrack", out IntPtr track) && track != IntPtr.Zero)
                {
                    pins.Add(AuraMonoPinNew(track));
                    if (this.TryGetMonoObjectMember(track, "_segment", out IntPtr segment) && segment != IntPtr.Zero)
                    {
                        pins.Add(AuraMonoPinNew(segment));
                        if (this.TryGetMonoObjectMember(segment, "clip", out IntPtr clip) && clip != IntPtr.Zero)
                        {
                            pins.Add(AuraMonoPinNew(clip));
                            this.SetAvatarStudioClipTimeout(clip, hold);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "action timeout " + (hold ? "hold" : "release") + " threw: " + ex.Message);
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe void SetAvatarStudioClipTimeout(IntPtr clip, bool hold)
        {
            IntPtr klass = auraMonoObjectGetClass(clip);
            IntPtr field = klass == IntPtr.Zero ? IntPtr.Zero : this.FindAuraMonoFieldOnHierarchy(klass, "_safe_time_check");
            if (field == IntPtr.Zero || auraMonoFieldSetValue == null)
            {
                return; // not an ActionClip (a track or a plain node) — it has no timer to park
            }

            float value = AvatarStudioHeldTimeout;
            if (!hold)
            {
                // ActionClip.duration is `sealed override => timeOut`: the clip's own full budget.
                if (!this.InvokeAvatarStudio(clip, "get_duration", 0, IntPtr.Zero, out IntPtr boxed, out string _)
                    || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    return;
                }
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return;
                }
                value = *(float*)raw;
                if (float.IsNaN(value) || value <= 0f)
                {
                    return;
                }
            }

            auraMonoFieldSetValue(clip, field, (IntPtr)(&value));
            FeatureLog.Detail(AvatarStudioTag, MasterLogAvatarStudio,
                "action timeout " + (hold ? "held" : "restored to " + value.ToString("0.#")) + " s");
        }

        internal void ApplyAvatarStudioPose(int index)
        {
            if (index < 0 || index >= this.avatarStudioPoses.Count)
            {
                return;
            }

            AvatarStudioPose pose = this.avatarStudioPoses[index];
            bool ok = this.RunAvatarStudioModelAction("Pose", false, (IntPtr guiPlayer, IntPtr animation, out string status) =>
            {
                // A frozen model cannot blend into anything, so every new pose starts playing.
                if (!this.SetAvatarStudioSpeed(guiPlayer, 1f, out status))
                {
                    return false;
                }
                // End a running action first: a cast while another holds the graph is refused as busy.
                if (!this.InvokeAvatarStudio(guiPlayer, "StopAction", 0, IntPtr.Zero, out IntPtr _, out status))
                {
                    return false;
                }

                unsafe
                {
                    int id = pose.Id;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&id);
                    string method = pose.Kind == AvatarStudioPoseKind.Snapshot ? "PlaySnapshot" : "PlayerAction";
                    return this.InvokeAvatarStudio(guiPlayer, method, 1, (IntPtr)args, out IntPtr _, out status);
                }
            });

            if (ok)
            {
                this.avatarStudioPaused = false;
                this.avatarStudioNextLiveSyncAt = 0f;
                this.SetAvatarStudioStatus("Pose: " + pose.Label);
                FeatureLog.Once(AvatarStudioTag, "first-pose", "first pose applied (" + pose.Kind + " " + pose.Id + ")");
            }
        }

        internal void ToggleAvatarStudioPause()
        {
            bool pause = !this.avatarStudioPaused;
            bool ok = this.RunAvatarStudioModelAction(pause ? "Pause" : "Play", pause,
                (IntPtr guiPlayer, IntPtr animation, out string status) =>
                {
                    if (!this.SetAvatarStudioSpeed(guiPlayer, pause ? 0f : 1f, out status))
                    {
                        return false;
                    }
                    this.HoldAvatarStudioActionTimeout(guiPlayer, pause);
                    if (pause)
                    {
                        this.ReadAvatarStudioState(animation, out status);
                    }
                    return true;
                });

            if (ok)
            {
                this.avatarStudioPaused = pause;
                this.avatarStudioNextLiveSyncAt = 0f;
                this.SetAvatarStudioStatus(pause ? "Paused" : "Playing");
            }
        }

        // Frame is 1-based. Pauses first if the model is still playing — a seek on a running
        // animator is overwritten on the next tick.
        internal void SeekAvatarStudioFrame(int frame)
        {
            bool wasPaused = this.avatarStudioPaused;
            bool ok = this.RunAvatarStudioModelAction("Frame", true, (IntPtr guiPlayer, IntPtr animation, out string status) =>
            {
                if (!wasPaused)
                {
                    if (!this.SetAvatarStudioSpeed(guiPlayer, 0f, out status))
                    {
                        return false;
                    }
                    this.HoldAvatarStudioActionTimeout(guiPlayer, true);
                    this.avatarStudioPaused = true;
                }
                if (this.avatarStudioStateHash == 0 && !this.ReadAvatarStudioState(animation, out status))
                {
                    return false;
                }

                int n = Math.Max(1, this.avatarStudioFrameCount);
                int f = Mathf.Clamp(frame, 1, n);
                // A loop's last frame IS its first, so it spans [0, 1); a one-shot ends on its end pose.
                float t = this.avatarStudioStateLoop || n == 1 ? (f - 1) / (float)n : (f - 1) / (float)(n - 1);

                unsafe
                {
                    int hash = this.avatarStudioStateHash;
                    int layer = 0;
                    float zero = 0f;
                    IntPtr* playArgs = stackalloc IntPtr[3];
                    playArgs[0] = (IntPtr)(&hash);
                    playArgs[1] = (IntPtr)(&layer);
                    playArgs[2] = (IntPtr)(&t);
                    if (!this.InvokeAvatarStudio(animation, "PlayState", 3, (IntPtr)playArgs, out IntPtr _, out status))
                    {
                        return false;
                    }
                    IntPtr* evalArgs = stackalloc IntPtr[1];
                    evalArgs[0] = (IntPtr)(&zero);
                    if (!this.InvokeAvatarStudio(animation, "Evaluate", 1, (IntPtr)evalArgs, out IntPtr _, out status))
                    {
                        return false;
                    }
                }
                this.avatarStudioFrame = f;
                return true;
            });

            if (ok)
            {
                this.SetAvatarStudioStatus("Frame " + this.avatarStudioFrame + " / " + this.avatarStudioFrameCount);
            }
        }

        // Live frame readout while the model plays; the window calls this on its own clock.
        internal void SyncAvatarStudioLiveFrame()
        {
            float now = Time.unscaledTime;
            if (this.avatarStudioPaused || now < this.avatarStudioNextLiveSyncAt || !this.avatarStudioBreaker.ShouldRun(now))
            {
                return;
            }
            this.avatarStudioNextLiveSyncAt = now + AvatarStudioLiveSyncIntervalSec;

            try
            {
                // Quiet on purpose: a model still loading is not a failure worth a log line.
                if (!this.EnsureAuraMonoApiReady() || !AuraMonoPinningAvailable
                    || !this.TryGetOpenAvatarStudioPanel(out IntPtr panel))
                {
                    return;
                }

                List<uint> pins = new List<uint>();
                try
                {
                    pins.Add(AuraMonoPinNew(panel));
                    if (this.TryGetMonoObjectMember(panel, "_characterModel", out IntPtr model) && model != IntPtr.Zero)
                    {
                        pins.Add(AuraMonoPinNew(model));
                        if (this.TryGetMonoObjectMember(model, "_guiPlayer", out IntPtr component) && component != IntPtr.Zero)
                        {
                            pins.Add(AuraMonoPinNew(component));
                            if (this.TryGetMonoObjectMember(component, "animation", out IntPtr animation) && animation != IntPtr.Zero)
                            {
                                pins.Add(AuraMonoPinNew(animation));
                                this.ReadAvatarStudioState(animation, out string _);
                            }
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }
                this.avatarStudioBreaker.Success();
            }
            catch (Exception ex)
            {
                this.avatarStudioBreaker.Failure(AvatarStudioTag, ex, now);
            }
        }

        // AnimationComponent.CurrentStateInfo is refreshed from the controller on every tick,
        // independent of the playback speed, so it is valid while frozen too.
        private unsafe bool ReadAvatarStudioState(IntPtr animation, out string status)
        {
            if (!this.InvokeAvatarStudio(animation, "get_CurrentStateInfo", 0, IntPtr.Zero, out IntPtr boxed, out status))
            {
                return false;
            }
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                status = "CurrentStateInfo returned nothing";
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                status = "CurrentStateInfo unbox failed";
                return false;
            }

            byte* p = (byte*)raw;
            int hash = *(int*)(p + AvatarStudioStateShortHashOffset);
            float time = *(float*)(p + AvatarStudioStateTimeOffset);
            float length = *(float*)(p + AvatarStudioStateLengthOffset);
            bool loop = p[AvatarStudioStateLoopOffset] != 0;
            if (hash == 0 || !(length > 0f) || float.IsNaN(time) || float.IsInfinity(length))
            {
                status = "no animation state yet";
                return false;
            }

            int n = Math.Max(1, Mathf.RoundToInt(length * AvatarStudioFrameRate));
            float phase = time - Mathf.Floor(time);
            int frame;
            if (!loop && time >= 1f)
            {
                frame = n;   // a finished one-shot rests on its last frame
            }
            else
            {
                frame = Mathf.Clamp(Mathf.FloorToInt(phase * n) + 1, 1, n);
            }

            this.avatarStudioStateHash = hash;
            this.avatarStudioStateLoop = loop;
            this.avatarStudioFrameCount = n;
            this.avatarStudioFrame = frame;
            status = "ok";
            return true;
        }
    }
}
