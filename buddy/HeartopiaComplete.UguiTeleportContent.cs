using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, round 3 (migration plan: cosmic-waddling-rainbow.md):
    // the Teleport tab — all nine sub-tabs (Home / Animal Care / NPCs / Locations / Events /
    // House / Custom / XYZ / Spawn Vehicle). First real consumers of the two new kit primitives
    // built this round in HeartopiaComplete.UguiKit.cs: the legacy InputField factory
    // (CreateUguiInputField — TMP_InputField is stripped; RVA evidence in its comment) and the
    // flexible list-row builder (CreateUguiListRow — the four IMGUI row shapes, not a generic
    // CRUD abstraction).
    //
    // Ground rules (same as rounds 1/2):
    //  - The IMGUI drawers (DrawTeleportTab, DrawSpawnVehicleTab) stay fully functional and
    //    untouched — this file only READS the same data fields and CALLS the same action methods
    //    (TeleportToLocation / TeleportToHome / RefreshAutoHomePosition / SaveCustomTeleports /
    //    GetTeleportNpcEntries / TryLoadSpawnVehicleList / TryScanLiveVehicles / RecallVehicleById
    //    / SpawnVehicleById / TryGetOnLiveVehicle). Two independent rendering paths, one backend.
    //  - Wiring is by STATIC display-position index (UguiShellTeleportTabIndex = 5 + the nine
    //    UguiShellTeleport*SubIndex constants, declared next to their round-1/2 siblings in
    //    UguiShellTabIndices.cs) — the sub display indices match teleportSubTab's own 0-8 exactly.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs these builders).
    //  - Strings stay UNlocalized for IMGUI parity (DrawTeleportTab/DrawSpawnVehicleTab use raw
    //    strings) EXCEPT the two Spawn Vehicle toggles, whose IMGUI twin (DrawSwitchToggle)
    //    localizes its label internally.
    //
    // Reactivity per sub-tab (report contract):
    //  - Home: per-frame while active — RefreshAutoHomePosition() every frame (IMGUI runs it
    //    every repaint; cheap/idempotent), live status/position SetTexts every frame, layout
    //    repositioned only when the conditional-visibility signature changes.
    //  - Animal Care / Locations / Events / House: static data — built once, no processor.
    //  - NPCs: rows rebuild on Refresh click and on every search-text change (live per-keystroke
    //    via InputField.onValueChanged, plus a per-gated-frame .text-vs-last-applied poll compare
    //    — the uguiPocDropdownPollFallback idiom — as wiring insurance AND as the external-change
    //    detector for IMGUI-side edits of the shared npcTeleportSearchText). No per-frame data
    //    poll beyond that: the entries cache only changes on a refresh.
    //  - Custom: name InputField is click-time-read (onValueChanged null); rows rebuild after
    //    any Save/Del (the UGUI analog of IMGUI's break-after-remove) and when the shared list's
    //    count changes (cross-surface add/delete), checked with one int compare per gated frame.
    //  - XYZ: all three InputFields click-time-read; a 0.5s tick pushes EXTERNAL backing-field
    //    changes (Home's "Copy Position", IMGUI-side edits) into the fields via
    //    SetTextWithoutNotify, diffed against the last SEEN field value so it can never clobber
    //    in-progress typing (typing does not touch the backing fields until the button fires).
    //  - Spawn Vehicle: both sections refresh on a ~1s throttle (Research-tab idiom) — status
    //    labels re-SetText when their strings change (server results arrive async after a click)
    //    and both row lists rebuild when a cheap signature over the row data changes; clicks
    //    also rebuild immediately. Toggle state re-syncs from the shared fields every gated
    //    frame (SetIsOnWithoutNotify, round-2 idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Shared gate + small helpers
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellTeleportSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellTeleportTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellTeleportTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellTeleportTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // Transparent kit scroll view (LIVE rail / Logging idiom): alpha-0 images still raycast,
        // so wheel/drag scrolling keeps working over the flat ContentBg look.
        private GameObject CreateUguiTeleportScroll(Transform parent, string name,
            float x, float y, float w, float h, float contentHeight, out Transform contentOut)
        {
            GameObject scroll = this.CreateUguiScrollView(parent, name, contentHeight, out contentOut);
            PlaceUguiTopLeft(scroll, x, y, w, h);
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (contentOut != null && contentOut.parent != null)
                {
                    Image viewportBg = contentOut.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch { }
            return scroll;
        }

        // Kit buttons keep their label in a child named "Label" (CreateUguiButtonCore) — used for
        // the Load/Refresh + Scan/Rescan caption swaps (TrySetUguiButtonLabelSize precedent).
        private void SetUguiButtonLabel(GameObject buttonGo, string text)
        {
            try
            {
                Transform t = (buttonGo != null) ? buttonGo.transform.Find("Label") : null;
                if (t != null)
                {
                    this.SetUguiLabelText(t.gameObject, text);
                }
            }
            catch { }
        }

        // One-way external re-sync for a click-time-read InputField: pushes the shared backing
        // field's value into the UI ONLY when the field itself moved since we last saw it (the
        // IMGUI twin edited it, or Home's Copy Position wrote it). User typing changes .text but
        // never the backing field, so typing can never be clobbered by this.
        private static void SyncUguiInputFieldFromBackingField(InputField field, ref string lastSeen, string liveValue)
        {
            string live = liveValue ?? string.Empty;
            if (field == null || string.Equals(live, lastSeen, StringComparison.Ordinal))
            {
                return;
            }
            lastSeen = live;
            try
            {
                field.SetTextWithoutNotify(live);
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Dispatch (called by BuildUguiShell per sub display index) + per-frame driver
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellTeleportSubContent(int subIndex, Transform parent,
            float x, float y, float w, float h)
        {
            switch (subIndex)
            {
                case UguiShellTeleportHomeSubIndex:
                    return this.BuildUguiShellTeleportHomeContent(parent, x, y, w, h);

                case UguiShellTeleportAnimalCareSubIndex:
                {
                    // DrawTeleportTab sub 1: names fall back to "Animal Care #N" when empty.
                    List<KeyValuePair<string, Vector3>> entries = new List<KeyValuePair<string, Vector3>>();
                    for (int i = 0; i < this.animalCareLocations.Length; i++)
                    {
                        string label = (i < this.animalCareLocationNames.Length
                                && !string.IsNullOrWhiteSpace(this.animalCareLocationNames[i]))
                            ? this.animalCareLocationNames[i]
                            : string.Format("Animal Care #{0}", i + 1);
                        entries.Add(new KeyValuePair<string, Vector3>(label, this.animalCareLocations[i]));
                    }
                    return this.BuildUguiShellTeleportStaticListContent(parent, "TeleportAnimalCareContent", x, y, w, h, entries);
                }

                case UguiShellTeleportNpcsSubIndex:
                    return this.BuildUguiShellTeleportNpcsContent(parent, x, y, w, h);

                case UguiShellTeleportLocationsSubIndex:
                {
                    List<KeyValuePair<string, Vector3>> entries = new List<KeyValuePair<string, Vector3>>();
                    foreach (KeyValuePair<string, Vector3> kvp in this.fastTravelLocations)
                    {
                        entries.Add(kvp);
                    }
                    return this.BuildUguiShellTeleportStaticListContent(parent, "TeleportLocationsContent", x, y, w, h, entries);
                }

                case UguiShellTeleportEventsSubIndex:
                {
                    List<KeyValuePair<string, Vector3>> entries = new List<KeyValuePair<string, Vector3>>();
                    foreach (KeyValuePair<string, Vector3> kvp in this.eventLocations)
                    {
                        entries.Add(kvp);
                    }
                    return this.BuildUguiShellTeleportStaticListContent(parent, "TeleportEventsContent", x, y, w, h, entries);
                }

                case UguiShellTeleportHouseSubIndex:
                {
                    List<KeyValuePair<string, Vector3>> entries = new List<KeyValuePair<string, Vector3>>();
                    for (int i = 0; i < this.houseLocations.Length; i++)
                    {
                        entries.Add(new KeyValuePair<string, Vector3>(
                            string.Format("House Slot #{0}", i + 1), this.houseLocations[i]));
                    }
                    return this.BuildUguiShellTeleportStaticListContent(parent, "TeleportHouseContent", x, y, w, h, entries);
                }

                case UguiShellTeleportCustomSubIndex:
                    return this.BuildUguiShellTeleportCustomContent(parent, x, y, w, h);

                case UguiShellTeleportXyzSubIndex:
                    return this.BuildUguiShellTeleportXyzContent(parent, x, y, w, h);

                case UguiShellTeleportSpawnVehicleSubIndex:
                    return this.BuildUguiShellTeleportSpawnVehicleContent(parent, x, y, w, h);

                default:
                    return this.BuildUguiShellPlaceholder(parent, this.L("Teleport"), "?", x, y, w, h);
            }
        }

        // Called every frame from ProcessUguiShellOnUpdate; each per-sub processor gates itself
        // on "shell visible AND Teleport tab active AND its own sub-tab active".
        private void ProcessUguiShellTeleportContentOnUpdate()
        {
            this.ProcessUguiShellTeleportHomeOnUpdate();
            this.ProcessUguiShellTeleportNpcsOnUpdate();
            this.ProcessUguiShellTeleportCustomOnUpdate();
            this.ProcessUguiShellTeleportXyzOnUpdate();
            this.ProcessUguiShellTeleportSpawnVehicleOnUpdate();
        }

        // ----------------------------------------------------------------------------------------
        // Static list sub-tabs (Animal Care / Locations / Events / House) — row shape (a)
        // ----------------------------------------------------------------------------------------

        // Whole-row 2-line teleport buttons over static data (IMGUI: 440x40 GUI.Buttons stepping
        // 45px). Built once — the data fields never change at runtime. House's 12 rows exceed the
        // block height, so every list scrolls.
        private GameObject BuildUguiShellTeleportStaticListContent(Transform parent, string goName,
            float x, float y, float w, float h, List<KeyValuePair<string, Vector3>> entries)
        {
            GameObject block = this.CreateUguiGo(goName, parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform rowsContent;
            this.CreateUguiTeleportScroll(block.transform, "Rows", 8f, 8f, w - 16f, h - 16f,
                entries.Count * 45f + 16f, out rowsContent);

            // Scroll view w-16 minus viewport insets (4 left + 18 right) minus 8px side pads.
            float rowW = w - 38f - 16f;
            float yCur = 8f;
            for (int i = 0; i < entries.Count; i++)
            {
                Vector3 pos = entries[i].Value; // copy for the click closure
                this.CreateUguiListRow(rowsContent, "Row" + i, 8f, yCur, rowW, 40f,
                    entries[i].Key,
                    string.Format("({0:F0}, {1:F0}, {2:F0})", pos.x, pos.y, pos.z),
                    null,
                    true, true, new System.Action(() => this.TeleportToLocation(pos)),
                    null);
                yCur += 45f;
            }
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Home (sub 0) — per-frame live status, conditional layout
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellTeleportHomeHandle
        {
            public GameObject Root;
            public GameObject HeaderLabel;
            public GameObject TpHomeButton;    // disabled (not hidden) until a home resolves
            public GameObject HomeSetLabel;    // green, only when homePositionSet && !autoHomePositionValid
            public GameObject StatusLabel;     // the 4-way conditional
            public GameObject CopyButton;      // hidden while the player object is missing
            public GameObject CurrentPosLabel;
            public int LayoutSignature = -1;   // homeSetVisible | playerFound packed
            public int ErrorCount;             // per-frame refresh disabled at 3 (LIVE rail idiom)
        }

        private UguiShellTeleportHomeHandle uguiShellTeleportHome;

        private GameObject BuildUguiShellTeleportHomeContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTeleportHome = null;

            UguiShellTeleportHomeHandle handle = new UguiShellTeleportHomeHandle();
            GameObject block = this.CreateUguiGo("TeleportHomeContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);
            handle.Root = block;

            handle.HeaderLabel = this.CreateUguiBodyLabel(block.transform, "Header", "Home Position", 13f);
            handle.TpHomeButton = this.CreateUguiPrimaryButton(block.transform, "TpHome", "TP Home",
                new System.Action(this.TeleportToHome));
            handle.HomeSetLabel = this.CreateUguiLabel(block.transform, "HomeSet", "", 12f, Color.green, false);
            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status", "", 12f, Color.yellow, false);
            handle.CopyButton = this.CreateUguiSecondaryButton(block.transform, "CopyPos", "Copy Position",
                new System.Action(this.OnUguiTeleportHomeCopyClicked));
            handle.CurrentPosLabel = this.CreateUguiBodyLabel(block.transform, "CurrentPos", "", 12f);
            this.TrySetUguiLabelWrapped(handle.CurrentPosLabel);

            // Prime the display so the first shown frame isn't empty; the per-frame processor
            // (which also runs RefreshAutoHomePosition) takes over while the sub-tab is active.
            this.RefreshUguiShellTeleportHomeDisplay(handle);

            this.uguiShellTeleportHome = handle;
            return block;
        }

        // Positions mirror DrawTeleportTab's y-cursor (header 25 → button 45 → [home-set 25] →
        // status 30 → [copy 34] → position label). Reposition/SetActive only when the packed
        // conditional state changes — text updates stay per-frame.
        private void RelayoutUguiShellTeleportHome(UguiShellTeleportHomeHandle handle, bool homeSetVisible, bool playerFound)
        {
            const float pad = 16f;
            float yCur = 12f;
            if (handle.HeaderLabel != null)
            {
                PlaceUguiTopLeft(handle.HeaderLabel, pad, yCur, 260f, 20f);
            }
            yCur += 25f;
            if (handle.TpHomeButton != null)
            {
                PlaceUguiTopLeft(handle.TpHomeButton, pad, yCur, 125f, 35f);
            }
            yCur += 45f;
            SetUguiGoActive(handle.HomeSetLabel, homeSetVisible);
            if (homeSetVisible)
            {
                if (handle.HomeSetLabel != null)
                {
                    PlaceUguiTopLeft(handle.HomeSetLabel, pad, yCur, 340f, 20f);
                }
                yCur += 25f;
            }
            if (handle.StatusLabel != null)
            {
                PlaceUguiTopLeft(handle.StatusLabel, pad, yCur, 340f, 20f);
            }
            yCur += 30f;
            SetUguiGoActive(handle.CopyButton, playerFound);
            if (playerFound)
            {
                if (handle.CopyButton != null)
                {
                    PlaceUguiTopLeft(handle.CopyButton, pad, yCur, 140f, 28f);
                }
                yCur += 34f;
            }
            if (handle.CurrentPosLabel != null)
            {
                PlaceUguiTopLeft(handle.CurrentPosLabel, pad, yCur, 340f, 40f);
            }
        }

        private void RefreshUguiShellTeleportHomeDisplay(UguiShellTeleportHomeHandle handle)
        {
            GameObject playerObj = GameObject.Find("p_player_skeleton(Clone)");
            bool playerFound = playerObj != null;
            bool homeSetVisible = this.homePositionSet && !this.autoHomePositionValid;

            // Disabled, not hidden, when no home is resolvable (Research round's idiom).
            this.SetUguiButtonInteractable(handle.TpHomeButton, this.homePositionSet || this.autoHomePositionValid);

            int signature = (homeSetVisible ? 1 : 0) | (playerFound ? 2 : 0);
            if (signature != handle.LayoutSignature)
            {
                handle.LayoutSignature = signature;
                this.RelayoutUguiShellTeleportHome(handle, homeSetVisible, playerFound);
            }

            if (homeSetVisible)
            {
                this.SetUguiLabelText(handle.HomeSetLabel,
                    $"Home Set: ({this.homePosition.x:F1}, {this.homePosition.y:F1}, {this.homePosition.z:F1})");
            }

            // The 4-way status conditional — copied EXACTLY from DrawTeleportTab (do not simplify):
            // no-player-and-no-auto-home → autoHomeStatus (cyan when valid, yellow when not) →
            // teleport-in-flight → ready.
            string statusText;
            Color statusColor;
            if (playerObj == null && !this.autoHomePositionValid)
            {
                statusText = "Not Ready";
                statusColor = Color.yellow;
            }
            else if (!string.IsNullOrEmpty(this.autoHomeStatus))
            {
                statusText = this.autoHomeStatus;
                statusColor = this.autoHomePositionValid ? new Color(0.45f, 1f, 1f) : Color.yellow;
            }
            else if (this.teleportFramesRemaining > 0)
            {
                statusText = "Teleporting...";
                statusColor = Color.yellow;
            }
            else
            {
                statusText = "Ready to Travel";
                statusColor = Color.green;
            }
            this.SetUguiLabelText(handle.StatusLabel, statusText);
            this.SetUguiLabelColor(handle.StatusLabel, statusColor);

            if (playerFound)
            {
                Vector3 p = playerObj.transform.position;
                this.SetUguiLabelText(handle.CurrentPosLabel,
                    $"Current Position:\n({p.x:F1}, {p.y:F1}, {p.z:F1})");
            }
            else
            {
                this.SetUguiLabelText(handle.CurrentPosLabel, "Current Position:\nPlayer not found");
            }
        }

        private void ProcessUguiShellTeleportHomeOnUpdate()
        {
            UguiShellTeleportHomeHandle handle = this.uguiShellTeleportHome;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellTeleportSubTabActive(UguiShellTeleportHomeSubIndex))
            {
                return;
            }

            try
            {
                // IMGUI parity: DrawTeleportTab runs this unconditionally every repaint while the
                // Home sub-tab shows (cheap/idempotent — it self-throttles internally).
                this.RefreshAutoHomePosition();
                this.RefreshUguiShellTeleportHomeDisplay(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Teleport Home refresh error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // DrawTeleportTab's Copy Position block verbatim: clipboard + the XYZ backing fields +
        // notification. The XYZ sub-tab's 0.5s external sync picks the field change up from here.
        private void OnUguiTeleportHomeCopyClicked()
        {
            try
            {
                GameObject playerObj = GameObject.Find("p_player_skeleton(Clone)");
                if (playerObj == null)
                {
                    return;
                }
                Vector3 p = playerObj.transform.position;
                GUIUtility.systemCopyBuffer = string.Format("{0:F3},{1:F3},{2:F3}", p.x, p.y, p.z);
                this.customTPX = p.x.ToString("F3");
                this.customTPY = p.y.ToString("F3");
                this.customTPZ = p.z.ToString("F3");
                this.AddMenuNotification("Current position copied to clipboard and XYZ fields", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport Home copy error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // NPCs (sub 2) — live search filter, row shapes (a) and (d)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellTeleportNpcsHandle
        {
            public GameObject Root;
            public GameObject StatusLabel;
            public string StatusShownText;      // SetText only on change (TMP re-layout hygiene)
            public bool StatusShownPopulated;
            public InputField SearchField;
            public string LastSearchApplied;    // poll compare + external-change detection
            public Transform RowsContent;
            public readonly List<GameObject> Rows = new List<GameObject>();
            public float RowAreaWidth;
            public int ErrorCount;
        }

        private UguiShellTeleportNpcsHandle uguiShellTeleportNpcs;

        private GameObject BuildUguiShellTeleportNpcsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTeleportNpcs = null;

            UguiShellTeleportNpcsHandle handle = new UguiShellTeleportNpcsHandle();
            GameObject block = this.CreateUguiGo("TeleportNpcsContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);
            handle.Root = block;

            const float pad = 16f;

            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status", "", 12f, Color.yellow, false);
            PlaceUguiTopLeft(handle.StatusLabel, pad, 12f, w - pad * 2f, 22f);

            GameObject refreshBtn = this.CreateUguiSecondaryButton(block.transform, "Refresh", "Refresh NPCs",
                new System.Action(this.OnUguiTeleportNpcsRefreshClicked));
            PlaceUguiTopLeft(refreshBtn, pad, 40f, 120f, 26f);

            GameObject searchLabel = this.CreateUguiBodyLabel(block.transform, "SearchLabel", "Search", 12f);
            PlaceUguiTopLeft(searchLabel, pad + 132f, 43f, 48f, 20f);

            // LIVE per-keystroke filtering — IMGUI recomputes the filtered list from
            // npcTeleportSearchText every repaint, so every keystroke re-filters here too.
            // Same 40-char limit as the IMGUI GUI.TextField.
            handle.LastSearchApplied = this.npcTeleportSearchText ?? string.Empty;
            handle.SearchField = this.CreateUguiInputField(block.transform, "SearchField",
                handle.LastSearchApplied, 40,
                new System.Action<string>(this.OnUguiTeleportNpcsSearchChanged));
            PlaceUguiTopLeft(handle.SearchField.gameObject, pad + 184f, 40f, w - pad - (pad + 184f), 26f);

            Transform rowsContent;
            this.CreateUguiTeleportScroll(block.transform, "Rows", 8f, 76f, w - 16f, h - 84f, 10f, out rowsContent);
            handle.RowsContent = rowsContent;
            handle.RowAreaWidth = w - 38f - 16f;

            this.RefreshUguiShellTeleportNpcStatus(handle);
            this.RebuildUguiShellTeleportNpcRows(handle);

            this.uguiShellTeleportNpcs = handle;
            return block;
        }

        // Status line: npcTeleportStatus, cyan when the cache holds entries, yellow otherwise
        // (IMGUI colors from the unfiltered cache, so this does too).
        private void RefreshUguiShellTeleportNpcStatus(UguiShellTeleportNpcsHandle handle)
        {
            bool populated = this.cachedNpcTeleportEntries != null && this.cachedNpcTeleportEntries.Count > 0;
            string text = this.npcTeleportStatus ?? string.Empty;
            if (populated == handle.StatusShownPopulated
                && string.Equals(text, handle.StatusShownText, StringComparison.Ordinal))
            {
                return;
            }
            handle.StatusShownPopulated = populated;
            handle.StatusShownText = text;
            this.SetUguiLabelText(handle.StatusLabel, text);
            this.SetUguiLabelColor(handle.StatusLabel, populated ? new Color(0.45f, 1f, 1f) : Color.yellow);
        }

        // Filter + row set copied exactly from DrawTeleportTab sub 2: normalize-and-Contains
        // filtering; empty result paints DISABLED preferred-name placeholder rows (further
        // filtered by the same search) whose reason string depends on "no search AND empty cache".
        private void RebuildUguiShellTeleportNpcRows(UguiShellTeleportNpcsHandle handle)
        {
            for (int i = 0; i < handle.Rows.Count; i++)
            {
                GameObject row = handle.Rows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.Rows.Clear();

            Transform content = handle.RowsContent;
            if (content == null)
            {
                return;
            }

            float rowW = handle.RowAreaWidth;
            float yCur = 8f;

            List<KeyValuePair<string, Vector3>> npcEntries = this.GetTeleportNpcEntries(false);
            string npcSearch = this.NormalizeNpcTeleportName(this.npcTeleportSearchText);
            if (!string.IsNullOrWhiteSpace(npcSearch))
            {
                npcEntries = npcEntries
                    .Where(entry => this.NormalizeNpcTeleportName(entry.Key).Contains(npcSearch))
                    .ToList();
            }

            if (npcEntries.Count == 0)
            {
                IEnumerable<string> preferredNames = this.GetNpcTeleportPreferredNames();
                if (!string.IsNullOrWhiteSpace(npcSearch))
                {
                    preferredNames = preferredNames.Where(name => this.NormalizeNpcTeleportName(name).Contains(npcSearch));
                }
                string reason = string.IsNullOrWhiteSpace(this.npcTeleportSearchText)
                        && (this.cachedNpcTeleportEntries == null || this.cachedNpcTeleportEntries.Count == 0)
                    ? "Press Refresh NPCs"
                    : "Live location unavailable";
                int idx = 0;
                foreach (string npcName in preferredNames)
                {
                    UguiListRowHandle row = this.CreateUguiListRow(content, "Npc" + idx, 8f, yCur, rowW, 40f,
                        npcName, reason, null,
                        true, false, null, null); // shape (d): disabled whole-row placeholder
                    handle.Rows.Add(row.Root);
                    yCur += 45f;
                    idx++;
                }
            }
            else
            {
                for (int i = 0; i < npcEntries.Count; i++)
                {
                    Vector3 pos = npcEntries[i].Value; // copy for the click closure
                    UguiListRowHandle row = this.CreateUguiListRow(content, "Npc" + i, 8f, yCur, rowW, 40f,
                        npcEntries[i].Key, null, null, // name only — no coords on this tab (IMGUI parity)
                        true, true, new System.Action(() => this.TeleportToLocation(pos)), null);
                    handle.Rows.Add(row.Root);
                    yCur += 45f;
                }
            }

            this.SetUguiScrollContentHeight(content, yCur + 8f);
        }

        private void OnUguiTeleportNpcsSearchChanged(string value)
        {
            UguiShellTeleportNpcsHandle handle = this.uguiShellTeleportNpcs;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, handle.LastSearchApplied, StringComparison.Ordinal))
                {
                    return; // the gated poll already applied it (or a redundant event)
                }
                handle.LastSearchApplied = text;
                this.npcTeleportSearchText = text; // shared field — IMGUI binds it per keystroke too
                this.RebuildUguiShellTeleportNpcRows(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport NPC search error: " + ex.Message);
            }
        }

        private void OnUguiTeleportNpcsRefreshClicked()
        {
            UguiShellTeleportNpcsHandle handle = this.uguiShellTeleportNpcs;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                this.GetTeleportNpcEntries(true);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport NPC refresh error: " + ex.Message);
            }
            this.RefreshUguiShellTeleportNpcStatus(handle);
            this.RebuildUguiShellTeleportNpcRows(handle);
        }

        private void ProcessUguiShellTeleportNpcsOnUpdate()
        {
            UguiShellTeleportNpcsHandle handle = this.uguiShellTeleportNpcs;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellTeleportSubTabActive(UguiShellTeleportNpcsSubIndex))
            {
                return;
            }

            try
            {
                // .text-vs-last-applied poll compare (uguiPocDropdownPollFallback idiom): catches
                // a missed onValueChanged AND — via the second branch — IMGUI-side edits of the
                // shared search field. Because the live binding writes npcTeleportSearchText on
                // every applied change, the field only ever differs from LastSearchApplied when
                // something OUTSIDE this surface edited it.
                InputField field = handle.SearchField;
                if (field != null)
                {
                    string uiText = field.text ?? string.Empty;
                    if (!string.Equals(uiText, handle.LastSearchApplied, StringComparison.Ordinal))
                    {
                        handle.LastSearchApplied = uiText;
                        this.npcTeleportSearchText = uiText;
                        this.RebuildUguiShellTeleportNpcRows(handle);
                    }
                    else
                    {
                        string fieldText = this.npcTeleportSearchText ?? string.Empty;
                        if (!string.Equals(fieldText, handle.LastSearchApplied, StringComparison.Ordinal))
                        {
                            handle.LastSearchApplied = fieldText;
                            try { field.SetTextWithoutNotify(fieldText); } catch { }
                            this.RebuildUguiShellTeleportNpcRows(handle);
                        }
                    }
                }

                this.RefreshUguiShellTeleportNpcStatus(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Teleport NPCs sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Custom (sub 6) — click-time name field, Save/Del rows (row shape (c))
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellTeleportCustomHandle
        {
            public GameObject Root;
            public InputField NameField;
            public string NameFieldSeen;       // external-change sync cache (backing-field value)
            public GameObject SavedHeader;     // only while the list is non-empty
            public Transform RowsContent;
            public readonly List<GameObject> Rows = new List<GameObject>();
            public float RowAreaWidth;
            public int LastRowCount = -1;      // cross-surface add/delete detection
            public int ErrorCount;
        }

        private UguiShellTeleportCustomHandle uguiShellTeleportCustom;

        private GameObject BuildUguiShellTeleportCustomContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTeleportCustom = null;

            UguiShellTeleportCustomHandle handle = new UguiShellTeleportCustomHandle();
            GameObject block = this.CreateUguiGo("TeleportCustomContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);
            handle.Root = block;

            const float pad = 16f;

            GameObject nameLabel = this.CreateUguiBodyLabel(block.transform, "NameLabel", "Name:", 12f);
            PlaceUguiTopLeft(nameLabel, pad, 15f, 45f, 22f);

            // Click-time-read only (IMGUI's Save reads this.customTeleportName at press time) —
            // no onValueChanged; the Save handler reads .text off the component.
            handle.NameFieldSeen = this.customTeleportName ?? string.Empty;
            handle.NameField = this.CreateUguiInputField(block.transform, "NameField",
                handle.NameFieldSeen, 0, null);
            PlaceUguiTopLeft(handle.NameField.gameObject, pad + 48f, 12f, 160f, 26f);

            GameObject saveBtn = this.CreateUguiSecondaryButton(block.transform, "Save", "Save",
                new System.Action(this.OnUguiTeleportCustomSaveClicked));
            PlaceUguiTopLeft(saveBtn, pad + 216f, 12f, 70f, 26f);

            handle.SavedHeader = this.CreateUguiBodyLabel(block.transform, "SavedHeader", "Saved Teleports", 12f);
            PlaceUguiTopLeft(handle.SavedHeader, pad, 50f, 260f, 20f);

            Transform rowsContent;
            this.CreateUguiTeleportScroll(block.transform, "Rows", 8f, 76f, w - 16f, h - 84f, 10f, out rowsContent);
            handle.RowsContent = rowsContent;
            handle.RowAreaWidth = w - 38f - 16f;

            this.RebuildUguiShellTeleportCustomRows(handle);

            this.uguiShellTeleportCustom = handle;
            return block;
        }

        private void RebuildUguiShellTeleportCustomRows(UguiShellTeleportCustomHandle handle)
        {
            for (int i = 0; i < handle.Rows.Count; i++)
            {
                GameObject row = handle.Rows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.Rows.Clear();

            handle.LastRowCount = this.customTeleportList.Count;
            SetUguiGoActive(handle.SavedHeader, this.customTeleportList.Count > 0);

            Transform content = handle.RowsContent;
            if (content == null)
            {
                return;
            }

            float rowW = handle.RowAreaWidth;
            float yCur = 8f;
            for (int i = 0; i < this.customTeleportList.Count; i++)
            {
                CustomTeleportEntry entry = this.customTeleportList[i];
                if (entry == null)
                {
                    continue;
                }
                Vector3 pos = entry.position;
                int indexCopy = i; // capture copies for the click closures
                UguiListRowHandle row = this.CreateUguiListRow(content, "Saved" + i, 8f, yCur, rowW, 32f,
                    null, null, null, false, true, null,
                    new UguiListRowButtonSpec[]
                    {
                        new UguiListRowButtonSpec
                        {
                            Label = entry.name, Tier = UguiListRowTierSecondary, Width = 0f, Enabled = true,
                            OnClick = new System.Action(() => this.TeleportToLocation(pos))
                        },
                        new UguiListRowButtonSpec
                        {
                            Label = "Del", Tier = UguiListRowTierDanger, Width = 70f, Enabled = true,
                            OnClick = new System.Action(() => this.OnUguiTeleportCustomDeleteClicked(indexCopy))
                        }
                    });
                handle.Rows.Add(row.Root);
                yCur += 38f;
            }

            this.SetUguiScrollContentHeight(content, yCur + 8f);
        }

        // DrawTeleportTab sub 6's Save block: read the name at click time, snapshot the player
        // position, append + persist. Silent no-op when the player object is missing (IMGUI parity).
        private void OnUguiTeleportCustomSaveClicked()
        {
            UguiShellTeleportCustomHandle handle = this.uguiShellTeleportCustom;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                if (handle.NameField != null)
                {
                    this.customTeleportName = handle.NameField.text ?? string.Empty;
                    handle.NameFieldSeen = this.customTeleportName;
                }
                GameObject player = GameObject.Find("p_player_skeleton(Clone)");
                if (player != null)
                {
                    this.customTeleportList.Add(new CustomTeleportEntry
                    {
                        name = this.customTeleportName,
                        position = player.transform.position
                    });
                    this.SaveCustomTeleports();
                }
                this.RebuildUguiShellTeleportCustomRows(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport Custom save error: " + ex.Message);
            }
        }

        // IMGUI removes + saves + BREAKS out of its draw loop; the UGUI analog is a full row
        // rebuild after the removal — no mid-rebuild iteration concern.
        private void OnUguiTeleportCustomDeleteClicked(int index)
        {
            UguiShellTeleportCustomHandle handle = this.uguiShellTeleportCustom;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                if (index >= 0 && index < this.customTeleportList.Count)
                {
                    this.customTeleportList.RemoveAt(index);
                    this.SaveCustomTeleports();
                }
                this.RebuildUguiShellTeleportCustomRows(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport Custom delete error: " + ex.Message);
            }
        }

        private void ProcessUguiShellTeleportCustomOnUpdate()
        {
            UguiShellTeleportCustomHandle handle = this.uguiShellTeleportCustom;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellTeleportSubTabActive(UguiShellTeleportCustomSubIndex))
            {
                return;
            }

            try
            {
                // External name edits (the IMGUI twin binds customTeleportName per keystroke).
                SyncUguiInputFieldFromBackingField(handle.NameField, ref handle.NameFieldSeen, this.customTeleportName);

                // Cross-surface add/delete — one int compare per gated frame.
                if (this.customTeleportList.Count != handle.LastRowCount)
                {
                    this.RebuildUguiShellTeleportCustomRows(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Teleport Custom sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // XYZ (sub 7) — click-time coordinate fields, all-or-nothing silent parse
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellTeleportXyzHandle
        {
            public GameObject Root;
            public InputField XField;
            public InputField YField;
            public InputField ZField;
            public string XFieldSeen;      // external-change sync caches (backing-field values)
            public string YFieldSeen;
            public string ZFieldSeen;
            public float NextSlowSyncAt;   // 0.5s tick
            public int ErrorCount;
        }

        private UguiShellTeleportXyzHandle uguiShellTeleportXyz;

        private GameObject BuildUguiShellTeleportXyzContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTeleportXyz = null;

            UguiShellTeleportXyzHandle handle = new UguiShellTeleportXyzHandle();
            GameObject block = this.CreateUguiGo("TeleportXyzContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);
            handle.Root = block;

            const float pad = 16f;

            GameObject header = this.CreateUguiBodyLabel(block.transform, "Header", "Direct XYZ Teleport", 13f);
            PlaceUguiTopLeft(header, pad, 20f, 260f, 22f);

            // Click-time-read only (IMGUI parses at button press) — no onValueChanged on any of
            // the three; the button handler reads .text off the components.
            handle.XFieldSeen = this.customTPX ?? string.Empty;
            handle.YFieldSeen = this.customTPY ?? string.Empty;
            handle.ZFieldSeen = this.customTPZ ?? string.Empty;

            GameObject xLabel = this.CreateUguiBodyLabel(block.transform, "XLabel", "X:", 12f);
            PlaceUguiTopLeft(xLabel, pad, 55f, 18f, 20f);
            handle.XField = this.CreateUguiInputField(block.transform, "XField", handle.XFieldSeen, 0, null);
            PlaceUguiTopLeft(handle.XField.gameObject, pad + 18f, 52f, 90f, 26f);

            GameObject yLabel = this.CreateUguiBodyLabel(block.transform, "YLabel", "Y:", 12f);
            PlaceUguiTopLeft(yLabel, pad + 118f, 55f, 18f, 20f);
            handle.YField = this.CreateUguiInputField(block.transform, "YField", handle.YFieldSeen, 0, null);
            PlaceUguiTopLeft(handle.YField.gameObject, pad + 136f, 52f, 90f, 26f);

            GameObject zLabel = this.CreateUguiBodyLabel(block.transform, "ZLabel", "Z:", 12f);
            PlaceUguiTopLeft(zLabel, pad + 236f, 55f, 18f, 20f);
            handle.ZField = this.CreateUguiInputField(block.transform, "ZField", handle.ZFieldSeen, 0, null);
            PlaceUguiTopLeft(handle.ZField.gameObject, pad + 254f, 52f, 90f, 26f);

            GameObject tpBtn = this.CreateUguiPrimaryButton(block.transform, "TeleportXyz", "Teleport to XYZ",
                new System.Action(this.OnUguiTeleportXyzClicked));
            PlaceUguiTopLeft(tpBtn, pad, 90f, 344f, 32f);

            this.uguiShellTeleportXyz = handle;
            return block;
        }

        // DrawTeleportTab sub 7 verbatim: write the field texts back into the shared customTP*
        // strings (they stay "bound" for the IMGUI twin), then teleport ONLY if all three parse.
        // A bad parse is a SILENT no-op — IMGUI shows no error message, so neither does this.
        private void OnUguiTeleportXyzClicked()
        {
            UguiShellTeleportXyzHandle handle = this.uguiShellTeleportXyz;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                if (handle.XField != null)
                {
                    this.customTPX = handle.XField.text ?? string.Empty;
                    handle.XFieldSeen = this.customTPX;
                }
                if (handle.YField != null)
                {
                    this.customTPY = handle.YField.text ?? string.Empty;
                    handle.YFieldSeen = this.customTPY;
                }
                if (handle.ZField != null)
                {
                    this.customTPZ = handle.ZField.text ?? string.Empty;
                    handle.ZFieldSeen = this.customTPZ;
                }

                if (float.TryParse(this.customTPX, out float xVal) &&
                    float.TryParse(this.customTPY, out float yVal) &&
                    float.TryParse(this.customTPZ, out float zVal))
                {
                    this.TeleportToLocation(new Vector3(xVal, yVal, zVal));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Teleport XYZ error: " + ex.Message);
            }
        }

        private void ProcessUguiShellTeleportXyzOnUpdate()
        {
            UguiShellTeleportXyzHandle handle = this.uguiShellTeleportXyz;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellTeleportSubTabActive(UguiShellTeleportXyzSubIndex))
            {
                return;
            }

            if (Time.unscaledTime < handle.NextSlowSyncAt)
            {
                return;
            }
            handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;

            try
            {
                // External backing-field changes only (Home's Copy Position, IMGUI edits) —
                // diffed against the last SEEN field value so in-progress typing is never clobbered.
                SyncUguiInputFieldFromBackingField(handle.XField, ref handle.XFieldSeen, this.customTPX);
                SyncUguiInputFieldFromBackingField(handle.YField, ref handle.YFieldSeen, this.customTPY);
                SyncUguiInputFieldFromBackingField(handle.ZField, ref handle.ZFieldSeen, this.customTPZ);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Teleport XYZ sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Spawn Vehicle (sub 8) — Garage/TableCar section + Live Vehicles section
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellTeleportSpawnVehicleHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;

            // Garage/TableCar section statics (built once, repositioned per rebuild)
            public GameObject GarageHeader;
            public GameObject GarageHint;
            public GameObject LoadButton;        // caption swaps Load Vehicle List / Refresh List
            public Toggle GetOnToggle;
            public Toggle OwnedOnlyToggle;
            public GameObject GarageStatusLabel;
            public string GarageStatusShown;
            public GameObject GarageCountLabel;

            public GameObject Divider;

            // Live Vehicles section statics
            public GameObject LiveHeader;
            public GameObject LiveHint;
            public GameObject ScanButton;        // caption swaps Scan Live Vehicles / Rescan
            public GameObject LiveStatusLabel;
            public string LiveStatusShown;
            public GameObject LiveCountLabel;

            public readonly List<GameObject> Rows = new List<GameObject>(); // both sections' rows
            public string RowsSignature;
            public float NextRefreshAt;          // ~1s throttle (Research-tab idiom)
            public int ErrorCount;
        }

        private UguiShellTeleportSpawnVehicleHandle uguiShellTeleportSpawnVehicle;

        private GameObject BuildUguiShellTeleportSpawnVehicleContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellTeleportSpawnVehicle = null;

            UguiShellTeleportSpawnVehicleHandle handle = new UguiShellTeleportSpawnVehicleHandle();
            GameObject block = this.CreateUguiGo("TeleportSpawnVehicleContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);
            handle.Root = block;

            Transform scrollContent;
            this.CreateUguiTeleportScroll(block.transform, "Scroll", 0f, 0f, w, h, 10f, out scrollContent);
            handle.ScrollContent = scrollContent;
            handle.ContentWidth = w - 22f; // viewport insets: 4 left + 18 right

            Color text = this.UguiKitTextColor();
            Color muted = this.UguiKitMutedColor();
            Color hintColor = new Color(muted.r, muted.g, muted.b, 0.8f);   // IMGUI hintStyle alpha
            Color statusColor = new Color(muted.r, muted.g, muted.b, 0.9f); // IMGUI statusStyle alpha

            // IMGUI headerStyle is bold 14 in the PRIMARY text color (not the header/accent color).
            handle.GarageHeader = this.CreateUguiLabel(scrollContent, "GarageHeader", "Spawn Vehicle", 14f, text, false);
            this.TrySetUguiLabelBold(handle.GarageHeader);
            handle.GarageHint = this.CreateUguiLabel(scrollContent, "GarageHint",
                "All vehicles in the game table (TableCar). Press Spawn to send a summon command to the "
                + "server. Vehicles you don't own are rejected server-side with a \"summon failed\" tip.",
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.GarageHint);

            handle.LoadButton = this.CreateUguiPrimaryButton(scrollContent, "LoadButton", "Load Vehicle List",
                new System.Action(this.OnUguiSpawnVehicleLoadClicked));
            // Checkbox toggles instead of IMGUI switches (round-2 deviation, see
            // UguiSettingsMainContent.cs header); DrawSwitchToggle localizes labels, so these do.
            handle.GetOnToggle = this.CreateUguiCheckbox(scrollContent, "GetOnToggle",
                this.L("Get on after spawn"), this.spawnVehicleGetOn,
                new System.Action<bool>(this.OnUguiSpawnVehicleGetOnChanged));
            handle.OwnedOnlyToggle = this.CreateUguiCheckbox(scrollContent, "OwnedOnlyToggle",
                this.L("Show only vehicles I own"), this.spawnVehicleShowOwnedOnly,
                new System.Action<bool>(this.OnUguiSpawnVehicleOwnedOnlyChanged));

            handle.GarageStatusShown = this.spawnVehicleStatus ?? string.Empty;
            handle.GarageStatusLabel = this.CreateUguiLabel(scrollContent, "GarageStatus",
                handle.GarageStatusShown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.GarageStatusLabel);
            handle.GarageCountLabel = this.CreateUguiBodyLabel(scrollContent, "GarageCount", "", 12f);
            this.TrySetUguiLabelBold(handle.GarageCountLabel);

            handle.Divider = this.CreateUguiGo("Divider", scrollContent);
            this.AddUguiImage(handle.Divider, Color.white, false, 1f); // IMGUI: whiteTexture 1px line

            handle.LiveHeader = this.CreateUguiLabel(scrollContent, "LiveHeader",
                "Live Vehicles In World (any owner)", 14f, text, false);
            this.TrySetUguiLabelBold(handle.LiveHeader);
            handle.LiveHint = this.CreateUguiLabel(scrollContent, "LiveHint",
                "Vehicles already spawned nearby/loaded — someone else's parked car, an NPC's, or an "
                + "event vehicle. The normal \"get in\" interact has no ownership check, only seat "
                + "availability, so this calls it directly. Getting on someone's vehicle is visible to them.",
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.LiveHint);

            handle.ScanButton = this.CreateUguiPrimaryButton(scrollContent, "ScanButton", "Scan Live Vehicles",
                new System.Action(this.OnUguiSpawnVehicleScanClicked));
            handle.LiveStatusShown = this.liveVehicleStatus ?? string.Empty;
            handle.LiveStatusLabel = this.CreateUguiLabel(scrollContent, "LiveStatus",
                handle.LiveStatusShown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.LiveStatusLabel);
            handle.LiveCountLabel = this.CreateUguiBodyLabel(scrollContent, "LiveCount", "", 12f);
            this.TrySetUguiLabelBold(handle.LiveCountLabel);

            handle.RowsSignature = this.BuildUguiTeleportSpawnVehicleSignature();
            this.RebuildUguiShellTeleportSpawnVehicleRows(handle);

            this.uguiShellTeleportSpawnVehicle = handle;
            return block;
        }

        // Cheap change signature for the ~1s tick. Garage rows only ever change through
        // TryLoadSpawnVehicleList (full clear + repopulate of the same sorted catalog — count +
        // loaded flag + filter state is sufficient); live rows are small, so they sign per-row
        // (a rescan can flip seat state or owner on an unchanged count). Status strings are
        // deliberately NOT part of this — they refresh as plain SetTexts without a row rebuild.
        private string BuildUguiTeleportSpawnVehicleSignature()
        {
            StringBuilder sb = new StringBuilder(64 + this.liveVehicleRows.Count * 24);
            sb.Append(this.spawnVehicleListLoaded ? '1' : '0')
              .Append('|').Append(this.spawnVehicleShowOwnedOnly ? '1' : '0')
              .Append('|').Append(this.spawnVehicleRows.Count)
              .Append('|').Append(this.spawnVehicleOwnedStaticIds.Count)
              .Append('|').Append(this.liveVehicleListLoaded ? '1' : '0')
              .Append('|').Append(this.liveVehicleRows.Count);
            for (int i = 0; i < this.liveVehicleRows.Count; i++)
            {
                LiveVehicleRow row = this.liveVehicleRows[i];
                sb.Append('\n').Append(row.NetId).Append('|').Append(row.StaticId).Append('|')
                  .Append(row.Seat0Free ? '1' : '0').Append('|').Append(row.OwnerName);
            }
            return sb.ToString();
        }

        private void RefreshUguiTeleportSpawnVehicleStatusLabels(UguiShellTeleportSpawnVehicleHandle handle)
        {
            string garage = this.spawnVehicleStatus ?? string.Empty;
            if (!string.Equals(garage, handle.GarageStatusShown, StringComparison.Ordinal))
            {
                handle.GarageStatusShown = garage;
                this.SetUguiLabelText(handle.GarageStatusLabel, garage);
            }
            string live = this.liveVehicleStatus ?? string.Empty;
            if (!string.Equals(live, handle.LiveStatusShown, StringComparison.Ordinal))
            {
                handle.LiveStatusShown = live;
                this.SetUguiLabelText(handle.LiveStatusLabel, live);
            }
        }

        // Destroys + rebuilds both sections' rows and repositions every static element from the
        // resulting y-cursor (the section-2 positions depend on section 1's row count), mirroring
        // DrawSpawnVehicleTab's cursor accumulation. Only called from clicks and on a signature
        // change — never unconditionally per tick.
        private void RebuildUguiShellTeleportSpawnVehicleRows(UguiShellTeleportSpawnVehicleHandle handle)
        {
            for (int i = 0; i < handle.Rows.Count; i++)
            {
                GameObject row = handle.Rows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.Rows.Clear();

            Transform content = handle.ScrollContent;
            if (content == null)
            {
                return;
            }

            const float left = 8f;
            float w = handle.ContentWidth - left * 2f;
            float yCur = 8f;

            // ---- Garage/TableCar section ----
            PlaceUguiTopLeft(handle.GarageHeader, left, yCur, w, 24f);
            yCur += 30f;
            PlaceUguiTopLeft(handle.GarageHint, left, yCur, w, 44f);
            yCur += 48f;
            this.SetUguiButtonLabel(handle.LoadButton, this.spawnVehicleListLoaded ? "Refresh List" : "Load Vehicle List");
            PlaceUguiTopLeft(handle.LoadButton, left, yCur, 200f, 32f);
            if (handle.GetOnToggle != null)
            {
                PlaceUguiTopLeft(handle.GetOnToggle.gameObject, left + 220f, yCur + 4f, w - 220f, 25f);
            }
            yCur += 38f;
            if (handle.OwnedOnlyToggle != null)
            {
                PlaceUguiTopLeft(handle.OwnedOnlyToggle.gameObject, left, yCur, 260f, 25f);
            }
            yCur += 34f;
            PlaceUguiTopLeft(handle.GarageStatusLabel, left, yCur, w, 34f);
            yCur += 38f;

            if (this.spawnVehicleListLoaded)
            {
                int visibleCount = this.CountVisibleSpawnVehicleRows();
                this.SetUguiLabelText(handle.GarageCountLabel,
                    "Vehicles: " + visibleCount
                    + (this.spawnVehicleShowOwnedOnly ? (" owned (of " + this.spawnVehicleRows.Count + " total)") : string.Empty));
                SetUguiGoActive(handle.GarageCountLabel, true);
                PlaceUguiTopLeft(handle.GarageCountLabel, left, yCur, w, 20f);
                yCur += 26f;

                for (int i = 0; i < this.spawnVehicleRows.Count; i++)
                {
                    SpawnVehicleRow vRow = this.spawnVehicleRows[i];
                    if (this.spawnVehicleShowOwnedOnly && !this.spawnVehicleOwnedStaticIds.Contains(vRow.StaticId))
                    {
                        continue;
                    }
                    int idCopy = vRow.StaticId;      // capture copies for the click closures
                    string nameCopy = vRow.Name;
                    string label = vRow.StaticId + "   " + (string.IsNullOrEmpty(vRow.Name) ? "(vehicle)" : vRow.Name);
                    UguiListRowHandle row = this.CreateUguiListRow(content, "Garage" + i, left, yCur, w, 30f,
                        label, null, null, false, true, null,
                        new UguiListRowButtonSpec[]
                        {
                            new UguiListRowButtonSpec
                            {
                                Label = "Recall", Tier = UguiListRowTierSecondary, Width = 78f, Enabled = true,
                                OnClick = new System.Action(() => this.RecallVehicleById(idCopy, nameCopy))
                            },
                            new UguiListRowButtonSpec
                            {
                                Label = "Spawn", Tier = UguiListRowTierPrimary, Width = 86f, Enabled = true,
                                OnClick = new System.Action(() => this.SpawnVehicleById(idCopy, nameCopy))
                            }
                        });
                    handle.Rows.Add(row.Root);
                    yCur += 30f;
                }
            }
            else
            {
                SetUguiGoActive(handle.GarageCountLabel, false);
            }

            yCur += 20f;
            PlaceUguiTopLeft(handle.Divider, left, yCur, w, 1f);
            yCur += 16f;

            // ---- Live Vehicles section ----
            PlaceUguiTopLeft(handle.LiveHeader, left, yCur, w, 24f);
            yCur += 30f;
            PlaceUguiTopLeft(handle.LiveHint, left, yCur, w, 58f);
            yCur += 62f;
            this.SetUguiButtonLabel(handle.ScanButton, this.liveVehicleListLoaded ? "Rescan" : "Scan Live Vehicles");
            PlaceUguiTopLeft(handle.ScanButton, left, yCur, 200f, 32f);
            yCur += 42f;
            PlaceUguiTopLeft(handle.LiveStatusLabel, left, yCur, w, 34f);
            yCur += 38f;

            if (this.liveVehicleListLoaded)
            {
                this.SetUguiLabelText(handle.LiveCountLabel, "Found: " + this.liveVehicleRows.Count);
                SetUguiGoActive(handle.LiveCountLabel, true);
                PlaceUguiTopLeft(handle.LiveCountLabel, left, yCur, w, 20f);
                yCur += 26f;

                for (int i = 0; i < this.liveVehicleRows.Count; i++)
                {
                    LiveVehicleRow lRow = this.liveVehicleRows[i];
                    string vName = string.IsNullOrEmpty(lRow.Name) ? "(vehicle)" : lRow.Name;
                    string ownerSuffix = string.IsNullOrEmpty(lRow.OwnerName) ? string.Empty : ("   Owner: " + lRow.OwnerName);
                    string label = lRow.NetId + "   " + lRow.StaticId + "   " + vName + ownerSuffix;
                    uint netIdCopy = lRow.NetId;     // capture copies for the click closure
                    int staticIdCopy = lRow.StaticId;
                    bool seatFree = lRow.Seat0Free;
                    string liveNameCopy = vName;
                    UguiListRowHandle row = this.CreateUguiListRow(content, "Live" + i, left, yCur, w, 36f,
                        label,
                        seatFree ? "driver seat open" : "driver seat occupied",
                        seatFree ? new Color(0.45f, 1f, 0.55f, 0.9f) : new Color(1f, 0.6f, 0.5f, 0.85f),
                        false, true, null,
                        new UguiListRowButtonSpec[]
                        {
                            new UguiListRowButtonSpec
                            {
                                Label = "Get On", Tier = UguiListRowTierPrimary, Width = 96f,
                                Enabled = seatFree, // disabled while the driver seat is occupied
                                OnClick = new System.Action(() =>
                                {
                                    if (seatFree) // same guard as IMGUI's `&& row.Seat0Free`
                                    {
                                        this.TryGetOnLiveVehicle(netIdCopy, staticIdCopy, liveNameCopy);
                                    }
                                })
                            }
                        });
                    handle.Rows.Add(row.Root);
                    yCur += 36f;
                }
            }
            else
            {
                SetUguiGoActive(handle.LiveCountLabel, false);
            }

            yCur += 30f;
            this.SetUguiScrollContentHeight(content, yCur);
        }

        // DrawSpawnVehicleTab's Load button block verbatim (status + color-coded notification),
        // then an immediate rebuild so the result shows without waiting for the 1s tick.
        private void OnUguiSpawnVehicleLoadClicked()
        {
            try
            {
                bool ok = this.TryLoadSpawnVehicleList(out string status);
                this.spawnVehicleStatus = status;
                this.AddMenuNotification(status, ok ? new Color(0.45f, 0.88f, 1f) : new Color(1f, 0.55f, 0.45f));
            }
            catch (Exception ex)
            {
                this.spawnVehicleStatus = "Load failed: " + ex.Message;
            }
            UguiShellTeleportSpawnVehicleHandle handle = this.uguiShellTeleportSpawnVehicle;
            if (handle != null && handle.Root != null)
            {
                this.RefreshUguiTeleportSpawnVehicleStatusLabels(handle);
                handle.RowsSignature = this.BuildUguiTeleportSpawnVehicleSignature();
                this.RebuildUguiShellTeleportSpawnVehicleRows(handle);
            }
        }

        private void OnUguiSpawnVehicleScanClicked()
        {
            try
            {
                bool ok = this.TryScanLiveVehicles(out string status);
                this.liveVehicleStatus = status;
                this.AddMenuNotification(status, ok ? new Color(0.45f, 0.88f, 1f) : new Color(1f, 0.55f, 0.45f));
            }
            catch (Exception ex)
            {
                this.liveVehicleStatus = "Scan failed: " + ex.Message;
            }
            UguiShellTeleportSpawnVehicleHandle handle = this.uguiShellTeleportSpawnVehicle;
            if (handle != null && handle.Root != null)
            {
                this.RefreshUguiTeleportSpawnVehicleStatusLabels(handle);
                handle.RowsSignature = this.BuildUguiTeleportSpawnVehicleSignature();
                this.RebuildUguiShellTeleportSpawnVehicleRows(handle);
            }
        }

        // IMGUI just assigns the flag (no save/notify) — same here.
        private void OnUguiSpawnVehicleGetOnChanged(bool value)
        {
            this.spawnVehicleGetOn = value;
        }

        // Changing the filter changes which rows are visible — rebuild immediately (the 1s
        // signature tick would also catch it, one second later).
        private void OnUguiSpawnVehicleOwnedOnlyChanged(bool value)
        {
            if (value == this.spawnVehicleShowOwnedOnly)
            {
                return;
            }
            this.spawnVehicleShowOwnedOnly = value;
            UguiShellTeleportSpawnVehicleHandle handle = this.uguiShellTeleportSpawnVehicle;
            if (handle != null && handle.Root != null)
            {
                handle.RowsSignature = this.BuildUguiTeleportSpawnVehicleSignature();
                this.RebuildUguiShellTeleportSpawnVehicleRows(handle);
            }
        }

        // Both sections' statuses change ASYNC after a click (server round-trips resolved by the
        // event hooks / world-scan timeout in SpawnVehicleFeature.cs), so the 1s tick keeps them
        // and the row lists fresh without requiring the user to leave and re-enter the sub-tab.
        private void ProcessUguiShellTeleportSpawnVehicleOnUpdate()
        {
            UguiShellTeleportSpawnVehicleHandle handle = this.uguiShellTeleportSpawnVehicle;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellTeleportSubTabActive(UguiShellTeleportSpawnVehicleSubIndex))
            {
                return;
            }

            try
            {
                // Cheap every-gated-frame: cross-surface toggle re-sync (fields are also editable
                // from the IMGUI tab). WithoutNotify — never replay a control's own side effects.
                this.SyncUguiToggleFromField(handle.GetOnToggle, this.spawnVehicleGetOn);
                this.SyncUguiToggleFromField(handle.OwnedOnlyToggle, this.spawnVehicleShowOwnedOnly);

                if (Time.unscaledTime < handle.NextRefreshAt)
                {
                    return;
                }
                handle.NextRefreshAt = Time.unscaledTime + 1f;

                this.RefreshUguiTeleportSpawnVehicleStatusLabels(handle);
                string signature = this.BuildUguiTeleportSpawnVehicleSignature();
                if (!string.Equals(signature, handle.RowsSignature, StringComparison.Ordinal))
                {
                    handle.RowsSignature = signature;
                    this.RebuildUguiShellTeleportSpawnVehicleRows(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Teleport Spawn Vehicle refresh error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }
    }
}
