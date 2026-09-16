using System;
using System.Collections.Generic;
using System.Text.Json;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// The expert view: every path as a field, the save profile and server, the mod build, the status
    /// grid, the log, and the steps Launch otherwise runs by itself. Ported from ui.html's #expert-view
    /// and the parts of render() that fill it.
    /// </summary>
    internal sealed unsafe partial class Win32Host
    {
        private const int IdExDetect = 120, IdExBrowseGame = 121, IdSourceZip = 122, IdSourceFolder = 123, IdDlBepInEx = 124,
                          IdStorageBrowse = 125, IdStorageDefault = 126, IdUnityBrowse = 127, IdDlUnity = 128,
                          IdProfile = 129, IdProfileCreate = 130, IdServer = 131, IdProfileOk = 132, IdProfileCancel = 133,
                          IdModVersion = 134, IdModLoad = 135, IdModInstall = 136, IdForce = 137, IdPrepare = 138, IdGenerate = 139;

        /// <summary>input[type=text] and #log: rgba(15,23,42,0.6) over the card, flattened so a text box can use it as its brush.</summary>
        private const uint FieldBg = 0x0e1628;

        /// <summary>.input-hint is the muted colour at 75%; its links are the page's default link colour at 75%.</summary>
        private const uint HintText = 0x728093, HintLink = 0x7f8cc6;

        private static readonly string[] SearchIcon = { "M18 11A7 7 0 1 1 4 11A7 7 0 1 1 18 11", "M21 21L16.65 16.65" };
        private static readonly string[] FolderIcon = { "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" };
        private static readonly string[] PlusIcon = { "M12 5L12 19", "M5 12L19 12" };

        private static readonly (int Value, string Name)[] Servers =
        {
            (8, "Global"), (9, "SEA"), (10, "America"), (12, "Asia"), (14, "TW / HK / MO"),
        };

        /// <summary>A text box with the page's input look drawn around it.</summary>
        private sealed class Field
        {
            internal nint Hwnd;
            internal float X, Y, W, H;
            internal bool Placed;
        }

        private Field fieldGame, fieldSource, fieldStorage, fieldUnity, fieldNewProfile;
        private Field[] fields;
        private nint log, fieldBrush;
        private float logX, logY, logW, logH;

        private Button exDetect, exBrowseGame, sourceZip, sourceFolder, dlBepInEx, storageBrowse, storageDefault, unityBrowse,
                       dlUnity, profileSelect, profileCreate, serverSelect, profileOk, profileCancel, modSelect, modLoad,
                       modInstall, force, prepare, generate;

        private bool newProfileShown;
        private bool profilesAvailable;
        private string activeProfile = "";
        private readonly List<string> profileNames = new List<string>();
        private int serverValue = Servers[0].Value;
        private string profileHint = "";

        private readonly List<(string Tag, string Asset)> modReleases = new List<(string, string)>();
        private string modChosen = "";
        private string modHint = "";

        private List<TextRun> sourceHint = new List<TextRun>(), unityHint = new List<TextRun>();

        // Layout results, in content pixels.
        private readonly List<(string Text, float X, float Y)> labels = new List<(string, float, float)>();
        private readonly List<(TextBlock Block, float X, float Y)> hints = new List<(TextBlock, float, float)>();
        private readonly (string Label, string Value, uint Color)[] status = new (string, string, uint)[4];
        private float statusY, statusW, statusH;

        private void CreateExpertControls()
        {
            fieldBrush = CreateSolidBrush(ColorRef(FieldBg));

            fieldGame = CreateField(true, "Detecting...");
            exDetect = AddButton(IdExDetect, ButtonKind.SecondaryLarge, "Detect", SearchIcon);
            exBrowseGame = AddButton(IdExBrowseGame, ButtonKind.SecondaryLarge, "Browse...", FolderIcon);

            fieldSource = CreateField(true, "Folder holding BepInEx\\ and dotnet\\");
            sourceZip = AddButton(IdSourceZip, ButtonKind.Secondary, "Zip...");
            sourceFolder = AddButton(IdSourceFolder, ButtonKind.Secondary, "Folder...");
            dlBepInEx = AddButton(IdDlBepInEx, ButtonKind.Secondary, "Download");

            fieldStorage = CreateField(true, null);
            storageBrowse = AddButton(IdStorageBrowse, ButtonKind.SecondaryLarge, "Browse...");
            storageDefault = AddButton(IdStorageDefault, ButtonKind.SecondaryLarge, "Default");

            fieldUnity = CreateField(true, "optional");
            unityBrowse = AddButton(IdUnityBrowse, ButtonKind.SecondaryLarge, "Browse...");
            dlUnity = AddButton(IdDlUnity, ButtonKind.SecondaryLarge, "Download");

            profileSelect = AddButton(IdProfile, ButtonKind.Select, "No profiles found");
            profileCreate = AddButton(IdProfileCreate, ButtonKind.Secondary, "", PlusIcon);
            SetText(profileCreate.Hwnd, "Create new profile");   // the name a screen reader reads, as the page's title did
            serverSelect = AddButton(IdServer, ButtonKind.Select, Servers[0].Name);
            fieldNewProfile = CreateField(false, "New profile name");
            profileOk = AddButton(IdProfileOk, ButtonKind.Primary, "Create");
            profileCancel = AddButton(IdProfileCancel, ButtonKind.Secondary, "Cancel");

            modSelect = AddButton(IdModVersion, ButtonKind.Select, "Newest release");
            modLoad = AddButton(IdModLoad, ButtonKind.Secondary, "Load versions");
            modInstall = AddButton(IdModInstall, ButtonKind.Secondary, "Install");

            fixed (char* cls = "EDIT")
                log = CreateWindowExW(0, cls, null, WS_CHILD | WS_CLIPSIBLINGS | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
                                      0, 0, 0, 0, Hwnd, 0, GetModuleHandleW(null), 0);
            SendMessageW(log, EM_SETLIMITTEXT, 0, 0);
            DarkControl(log);

            force = AddButton(IdForce, ButtonKind.Checkbox, "Force regenerate");
            prepare = AddButton(IdPrepare, ButtonKind.SecondaryLarge, "Prepare");
            generate = AddButton(IdGenerate, ButtonKind.SecondaryLarge, "Generate interop");

            fields = new[] { fieldGame, fieldSource, fieldStorage, fieldUnity, fieldNewProfile };
            ExpertFontsChanged();
        }

        private Field CreateField(bool readOnly, string placeholder)
        {
            var field = new Field();
            fixed (char* cls = "EDIT")
                field.Hwnd = CreateWindowExW(0, cls, null, WS_CHILD | WS_CLIPSIBLINGS | WS_TABSTOP | ES_AUTOHSCROLL | (readOnly ? ES_READONLY : 0),
                                             0, 0, 0, 0, Hwnd, 0, GetModuleHandleW(null), 0);
            SendMessageW(field.Hwnd, EM_SETMARGINS, 3, 0);
            if (placeholder != null)
                fixed (char* cue = placeholder)
                    SendMessageW(field.Hwnd, EM_SETCUEBANNER, 1, (nint)cue);
            return field;
        }

        /// <summary>The text boxes hold on to their font handle, so a new DPI's fonts have to be handed over.</summary>
        private void ExpertFontsChanged()
        {
            if (fields == null)
                return;
            foreach (Field field in fields)
                SendMessageW(field.Hwnd, WM_SETFONT, Fonts.Input.Handle, 1);
            SendMessageW(log, WM_SETFONT, Fonts.Log.Handle, 1);
        }

        private nint ExpertControlColor(nint dc, nint control)
        {
            if (fields == null)
                return 0;
            if (control == log)
            {
                SetTextColor(dc, ColorRef(TextMuted));
                SetBkColor(dc, ColorRef(FieldBg));
                return fieldBrush;
            }
            foreach (Field field in fields)
            {
                if (field.Hwnd != control)
                    continue;
                SetTextColor(dc, ColorRef(TextMain));
                SetBkColor(dc, ColorRef(FieldBg));
                return fieldBrush;
            }
            return 0;
        }

        // ---- render --------------------------------------------------------------

        private void renderExpert()
        {
            bool downloads = B("downloads"), gameOk = B("gameOk"), prepared = B("prepared");

            SetField(fieldGame, S("game"));
            SetField(fieldSource, S("source"));
            SetField(fieldStorage, S("storage"));
            SetField(fieldUnity, S("unityLibsZip"));

            const string sourceLead = "The Unity.IL2CPP win-x64 archive — the zip as downloaded, or a folder already unpacked from it. ";
            sourceHint = downloads
                ? Plain(sourceLead + "Needed only until Prepare.")
                : new List<TextRun> { new TextRun(sourceLead), new TextRun("Download it here", S("bepInExUrl")), new TextRun(".") };

            unityHint = S("unityLibsUrl").Length > 0
                ? (downloads
                    ? Plain("Optional — BepInEx fetches these itself during the first generation.")
                    : new List<TextRun>
                      {
                          new TextRun("Optional — "),
                          new TextRun("download " + S("unityVersion") + ".zip", S("unityLibsUrl")),
                          new TextRun("."),
                      })
                : Plain("Optional. Set the game directory first to resolve the Unity version.");

            renderModVersions();

            // The same words the step cards use, so the two views never disagree about what is installed.
            status[0] = ("GAME", gameOk ? "Found" : S("game").Length > 0 ? "Not an IL2CPP build" : "Not found", gameOk ? Success : Warning);
            status[1] = ("BEPINEX", prepared ? "Installed" : "Not installed", prepared ? Success : Warning);
            status[2] = ("MOD", B("hasPlugin") ? "Installed" : "Not installed", B("hasPlugin") ? Success : Warning);
            status[3] = ("INTEROP",
                         !B("hasInterop") ? "Not built" : B("interopStale") ? "Expired" : "Up to date",
                         B("hasInterop") && !B("interopStale") ? Success : Warning);

            Enable(prepare, !busy && S("source").Length > 0);
            Enable(generate, !busy && prepared && gameOk);
            Enable(dlBepInEx, !busy);
            Enable(sourceZip, !busy);
            Enable(dlUnity, !busy && gameOk);
        }

        private static void SetField(Field field, string value)
        {
            // Only when it differs: resetting the text would drop a selection someone is about to copy.
            if (WindowText(field.Hwnd) != value)
                SetText(field.Hwnd, value);
        }

        /// <summary>The release dropdown. Filled only once someone asks for it - the API counts requests.</summary>
        private void renderModVersions()
        {
            modReleases.Clear();
            if (haveState && state.TryGetProperty("modReleases", out JsonElement releases) && releases.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement r in releases.EnumerateArray())
                    modReleases.Add((Str(r, "tag") ?? "", Str(r, "asset") ?? ""));
            }
            if (!modReleases.Exists(r => r.Tag == modChosen))
                modChosen = "";
            SetButtonText(modSelect, modChosen.Length == 0 ? "Newest release" : ModOption(modReleases.Find(r => r.Tag == modChosen)));

            string version = S("pluginVersion"), update = S("modUpdate");
            modHint = version.Length == 0
                ? (modReleases.Count > 0 ? "" : "Nothing installed yet.")
                : "Installed: " + version +
                  (B("pluginPinned") ? " (pinned - launches will not move past it)"
                   : update.Length > 0 ? " - Launch updates it to " + update
                   : "") +
                  (modReleases.Count > 0 ? "" : " - press Load versions to pick another.");

            Enable(modSelect, !busy);
            Enable(modLoad, !busy);
            Enable(modInstall, !busy);
        }

        private static string ModOption((string Tag, string Asset) release) => release.Tag + "  (" + release.Asset + ")";

        private void loadProfiles()
        {
            Call("profiles", null, data =>
            {
                profileNames.Clear();
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("profiles", out JsonElement list) &&
                    list.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement name in list.EnumerateArray())
                        if (name.ValueKind == JsonValueKind.String)
                            profileNames.Add(name.GetString());
                }
                activeProfile = Str(data, "active") ?? "";
                profilesAvailable = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("available", out JsonElement a) &&
                                    a.ValueKind == JsonValueKind.True;
                profileHint = profilesAvailable
                    ? (activeProfile.Length > 0 ? "" : "No active profile yet.")
                    : "The game has not created its save folder yet.";
                UpdateProfileControls();

                if (activeProfile.Length > 0)
                {
                    string profile = activeProfile;
                    Call("serverGet", w => w.WriteString("profile", profile), value =>
                    {
                        if (value.ValueKind == JsonValueKind.Number && value.GetInt32() > 0)
                        {
                            serverValue = value.GetInt32();
                            UpdateProfileControls();
                        }
                    });
                }
            });
        }

        private void UpdateProfileControls()
        {
            string shown = profileNames.Count == 0 ? "No profiles found"
                         : profileNames.Exists(n => n == activeProfile) ? activeProfile
                         : profileNames[0];
            SetButtonText(profileSelect, shown);

            string server = Servers[0].Name;
            foreach (var s in Servers)
                if (s.Value == serverValue)
                    server = s.Name;
            SetButtonText(serverSelect, server);

            Enable(profileSelect, profilesAvailable);
            Enable(serverSelect, profilesAvailable);
            Enable(profileCreate, profilesAvailable);
            Layout();
            Invalidate();
        }

        /// <summary>The page appends every line, in both views; the simple view just does not show the box.</summary>
        private void appendLog(string text)
        {
            if (log == 0 || text == null)
                return;

            var info = new SCROLLINFO { cbSize = (uint)sizeof(SCROLLINFO), fMask = SIF_ALL };
            GetScrollInfo(log, SB_VERT, &info);
            bool atBottom = IsWindowVisible(log) == 0 || info.nMax <= 0 ||
                            info.nPos + (int)info.nPage >= info.nMax - 1;
            int firstBefore = (int)SendMessageW(log, EM_GETFIRSTVISIBLELINE, 0, 0);

            int length = GetWindowTextLengthW(log);
            SendMessageW(log, EM_SETSEL, length, length);
            fixed (char* line = (length > 0 ? "\r\n" : "") + text)
                SendMessageW(log, EM_REPLACESEL, 0, (nint)line);

            UpdateLogScrollBar();

            // Someone scrolled up to read something: the new line must not yank them back down.
            if (!atBottom)
            {
                int firstAfter = (int)SendMessageW(log, EM_GETFIRSTVISIBLELINE, 0, 0);
                SendMessageW(log, EM_LINESCROLL, 0, firstBefore - firstAfter);
            }
        }

        /// <summary>#log is overflow-y: auto - a scrollbar only once there is more than fits.</summary>
        private void UpdateLogScrollBar()
        {
            if (log == 0 || Fonts == null)
                return;
            float border = MathF.Max(1, MathF.Round(S(1)));
            int lines = (int)SendMessageW(log, EM_GETLINECOUNT, 0, 0);
            int fits = (int)((logH - (border + S(10)) * 2) / MathF.Max(1, Fonts.Log.Height));
            ShowScrollBar(log, SB_VERT, lines > fits ? 1 : 0);
        }

        // ---- commands ----------------------------------------------------------------

        private void ClickedExpert(int id)
        {
            switch (id)
            {
                case IdExDetect: detectGame(); break;
                case IdExBrowseGame: pick("game", false, "Select folder"); break;
                case IdSourceZip: chooseArchive(); break;
                case IdSourceFolder: pick("source", false, "Select folder"); break;
                case IdDlBepInEx: run("downloadBepInEx"); break;
                case IdStorageBrowse: pick("storage", false, "Select folder"); break;
                case IdStorageDefault: save("storage", S("defaultStorage")); break;
                case IdUnityBrowse: pick("unityLibsZip", true, "Unity base libraries zip"); break;
                case IdDlUnity: run("downloadUnityLibs"); break;
                case IdModLoad: run("modReleases"); break;
                case IdPrepare: run("prepare"); break;

                case IdModInstall:
                {
                    string tag = modChosen;
                    run("installMod", w => w.WriteString("tag", tag));
                    break;
                }

                case IdGenerate:
                {
                    bool forced = force.Checked;
                    run("generateInterop", w => w.WriteBoolean("force", forced));
                    break;
                }

                case IdForce:
                    force.Checked = !force.Checked;
                    InvalidateRect(force.Hwnd, null, 0);
                    break;

                case IdModVersion:
                {
                    var items = new List<string> { "Newest release" };
                    foreach (var r in modReleases)
                        items.Add(ModOption(r));
                    int current = modChosen.Length == 0 ? 0 : modReleases.FindIndex(r => r.Tag == modChosen) + 1;
                    int chosen = Popup(modSelect, items, current);
                    if (chosen >= 0)
                    {
                        modChosen = chosen == 0 ? "" : modReleases[chosen - 1].Tag;
                        SetButtonText(modSelect, items[chosen]);
                    }
                    break;
                }

                // Both dropdowns take effect the moment they change, so a failure cannot be left showing
                // on screen as if it had worked: loadProfiles puts them back to what is actually on disk.
                case IdProfile:
                {
                    if (profileNames.Count == 0)
                        break;
                    int chosen = Popup(profileSelect, profileNames, profileNames.IndexOf(activeProfile));
                    if (chosen < 0)
                        break;
                    string name = profileNames[chosen];
                    SetButtonText(profileSelect, name);
                    Call("profileSwitch", w => w.WriteString("name", name), _ => loadProfiles(), e =>
                    {
                        appendLog("• " + e);
                        say(e, true);
                        loadProfiles();
                    });
                    break;
                }

                case IdServer:
                {
                    var names = new List<string>();
                    int current = 0;
                    for (int i = 0; i < Servers.Length; i++)
                    {
                        names.Add(Servers[i].Name);
                        if (Servers[i].Value == serverValue)
                            current = i;
                    }
                    int chosen = Popup(serverSelect, names, current);
                    if (chosen < 0)
                        break;
                    serverValue = Servers[chosen].Value;
                    SetButtonText(serverSelect, Servers[chosen].Name);
                    string profile = activeProfile;
                    int value = serverValue;
                    Call("serverSet", w =>
                    {
                        w.WriteString("profile", profile);
                        w.WriteNumber("value", value);
                    }, null, e =>
                    {
                        appendLog("• " + e);
                        say(e, true);
                        loadProfiles();
                    });
                    break;
                }

                case IdProfileCreate:
                    newProfileShown = true;
                    SetText(fieldNewProfile.Hwnd, "");
                    Layout();
                    Invalidate();
                    SetFocus(fieldNewProfile.Hwnd);
                    break;

                case IdProfileCancel:
                    HideNewProfile();
                    break;

                case IdProfileOk:
                {
                    string name = WindowText(fieldNewProfile.Hwnd).Trim();
                    if (name.Length == 0)
                        break;
                    Call("profileCreate", w => w.WriteString("name", name), _ =>
                    {
                        HideNewProfile();
                        loadProfiles();
                    }, e =>
                    {
                        appendLog("• " + e);
                        say(e, true);
                        HideNewProfile();
                        loadProfiles();
                    });
                    break;
                }
            }
        }

        private void HideNewProfile()
        {
            newProfileShown = false;
            SetText(fieldNewProfile.Hwnd, "");
            Layout();
            Invalidate();
        }

        /// <summary>Enter in a text field. Only the new profile's name has anything to submit.</summary>
        private void EnterInField(nint focus)
        {
            if (fieldNewProfile != null && focus == fieldNewProfile.Hwnd && newProfileShown)
                ClickedExpert(IdProfileOk);
        }

        /// <summary>A select's list, under it and as wide as it. Returns the index chosen, or -1.</summary>
        private int Popup(Button anchor, IReadOnlyList<string> items, int current) =>
            ListPopup.Show(this, anchor, items, current);

        // ---- layout --------------------------------------------------------------

        private void BeginExpertLayout()
        {
            labels.Clear();
            hints.Clear();
            if (fields != null)
                foreach (Field field in fields)
                    field.Placed = false;
        }

        private void EndExpertLayout()
        {
            if (fields == null)
                return;
            foreach (Field field in fields)
                if (!field.Placed && IsWindowVisible(field.Hwnd) != 0)
                    ShowWindow(field.Hwnd, SW_HIDE);
            if (!expert && IsWindowVisible(log) != 0)
                ShowWindow(log, SW_HIDE);
        }

        private float LayoutExpert(float y, float padX, float width, float clientH)
        {
            float gap = S(14);
            bool downloads = B("downloads");

            y = Label("Heartopia Directory", padX, y);
            y = Row(y, padX, width, fieldGame, null, exDetect, exBrowseGame);
            y += gap;

            y = Label("BepInEx source", padX, y);
            y = Row(y, padX, width, fieldSource, null, sourceZip, sourceFolder, downloads ? dlBepInEx : null);
            y = Hint(sourceHint, padX, y, width);
            y += gap;

            y = Label("Storage folder", padX, y);
            y = Row(y, padX, width, fieldStorage, null, storageBrowse, storageDefault);
            y += gap;

            y = Label("Unity libraries zip", padX, y);
            y = Row(y, padX, width, fieldUnity, null, unityBrowse, downloads ? dlUnity : null);
            y = Hint(unityHint, padX, y, width);
            y += gap;

            // Save profile and server, side by side.
            float colW = (width - S(12)) / 2, rightX = padX + colW + S(12);
            float rowY = Label("Save Profile", padX, y);
            Label("Server", rightX, y);
            float left = Row(rowY, padX, colW, null, profileSelect, profileCreate);
            float right = Row(rowY, rightX, colW, null, serverSelect);
            y = MathF.Max(left, right);
            if (newProfileShown)
                y = Row(y + S(6), padX, width, fieldNewProfile, null, profileOk, profileCancel);
            y = Hint(Plain(profileHint), padX, y, width);
            y += gap;

            if (B("pluginFromGitHub"))
            {
                y = Label("Bugtopia build", padX, y);
                y = Row(y, padX, width, null, modSelect, modLoad, modInstall);
                y = Hint(Plain(modHint), padX, y, width);
                y += gap;
            }

            // Status grid: four equal boxes.
            float border = MathF.Max(1, MathF.Round(S(1)));
            statusW = (width - S(12) * 3) / 4;
            statusH = border * 2 + S(12) * 2 + Fonts.StatusLabel.LineHeight + S(4) + Fonts.StatusValue.LineHeight;
            statusY = y;
            y += statusH + gap;

            // The banner always speaks in this view.
            y = LayoutBanner(y, padX, width) + gap;

            // The footer is measured first: the log takes whatever height is left, but never less than 96px.
            var forceSize = Measure(force);
            var prepareSize = Measure(prepare);
            var generateSize = Measure(generate);
            var playSize = Measure(play);

            // The buttons never shrink (white-space: nowrap); the checkbox's label wraps instead when the
            // row is too narrow for all of it, as the page's flex row does.
            float buttonsW = prepareSize.Width + generateSize.Width + playSize.Width + S(12) * 2;
            float forceRoom = width - buttonsW - S(12);
            if (forceSize.Width > forceRoom)
            {
                // Never narrower than its longest word: a label may wrap between words, not inside one.
                float longestWord = 0;
                foreach (string word in force.Text.Split(' '))
                    longestWord = MathF.Max(longestWord, Fonts.Measure(Fonts.Check, word));
                float minimum = S(16) + S(8) + longestWord + 1;
                forceSize = (MathF.Max(minimum, forceRoom),
                             TextBlock.Layout(new List<TextRun> { new TextRun(force.Text) }, Fonts.Check, Fonts.Check,
                                              MathF.Max(minimum, forceRoom) - S(24), Fonts.Check.LineHeight).Height);
            }
            float rowH = MathF.Max(forceSize.Height, MathF.Max(prepareSize.Height, MathF.Max(generateSize.Height, playSize.Height)));
            float kbdHeight = Fonts.Kbd.LineHeight + S(1) * 2 + border * 2;
            float footerH = rowH + S(10) + MathF.Max(Fonts.Hint.LineHeight, kbdHeight);

            logX = padX;
            logY = y;
            logW = width;
            logH = MathF.Max(S(96), clientH - S(24) - footerH - gap - y);
            SetWindowPos(log, 0, (int)MathF.Round(logX + border + S(14)), (int)MathF.Round(logY + border + S(10) - scrollY),
                         (int)MathF.Round(logW - (border + S(14)) * 2 + S(10)), (int)MathF.Round(logH - (border + S(10)) * 2),
                         SWP_MOVECHILD | SWP_SHOWWINDOW);
            UpdateLogScrollBar();
            y += logH + gap;

            // [Force regenerate] ........ [Prepare] [Generate interop] [Launch]
            PlaceScrolled(force, padX, y + (rowH - forceSize.Height) / 2, forceSize.Width, forceSize.Height, true);
            float bx = padX + width - playSize.Width;
            PlaceScrolled(play, bx, y + (rowH - playSize.Height) / 2, playSize.Width, playSize.Height, true);
            bx -= S(12) + generateSize.Width;
            PlaceScrolled(generate, bx, y + (rowH - generateSize.Height) / 2, generateSize.Width, generateSize.Height, true);
            bx -= S(12) + prepareSize.Width;
            PlaceScrolled(prepare, bx, y + (rowH - prepareSize.Height) / 2, prepareSize.Width, prepareSize.Height, true);
            y += rowH + S(10);

            return LayoutHint(y, padX, width);
        }

        private float Label(string text, float x, float y)
        {
            labels.Add((text, x, y));
            return y + Fonts.Label.LineHeight + S(6);
        }

        private float Hint(List<TextRun> runs, float x, float y, float width)
        {
            // The section's 6px gap, then the hint's own margin-top: 0.3rem.
            y += S(6) + S(4.8f);
            if (runs.Count == 0 || (runs.Count == 1 && runs[0].Text.Length == 0))
                return y;
            TextBlock block = TextBlock.Layout(runs, Fonts.InputHint, Fonts.InputHint, width, Fonts.InputHint.Em * 1.35f);
            hints.Add((block, x, y));
            return y + block.Height;
        }

        /// <summary>
        /// .input-group: one flexible item - a text field or a select - and buttons after it, 10px apart,
        /// every item stretched to the tallest.
        /// </summary>
        private float Row(float y, float x, float width, Field field, Button flex, params Button[] buttons)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            float rowH = field != null ? border * 2 + S(10) * 2 + Fonts.Input.LineHeight : Measure(flex).Height;
            float buttonsW = 0;
            var sizes = new (float W, float H)[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                    continue;
                sizes[i] = Measure(buttons[i]);
                buttonsW += sizes[i].W + S(10);
                rowH = MathF.Max(rowH, sizes[i].H);
            }

            float flexW = MathF.Max(S(40), width - buttonsW);
            if (field != null)
                PlaceField(field, x, y, flexW, rowH);
            else
                PlaceScrolled(flex, x, y, flexW, rowH, true);

            float bx = x + flexW + S(10);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                    continue;
                PlaceScrolled(buttons[i], bx, y, sizes[i].W, rowH, true);
                bx += sizes[i].W + S(10);
            }
            return y + rowH;
        }

        private void PlaceField(Field field, float x, float y, float w, float h)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            field.X = x;
            field.Y = y;
            field.W = w;
            field.H = h;
            field.Placed = true;
            float inset = border + S(14) - S(3);   // less the edit box's own 3px margin
            SetWindowPos(field.Hwnd, 0, (int)MathF.Round(x + inset), (int)MathF.Round(y + (h - Fonts.Input.Height) / 2 - scrollY),
                         (int)MathF.Round(w - inset * 2), (int)MathF.Round(Fonts.Input.Height),
                         SWP_MOVECHILD | SWP_SHOWWINDOW);
        }

        // ---- painting ------------------------------------------------------------

        private void RenderExpertShapes(nint g, float sy, float border)
        {
            nint focus = GetFocus();
            foreach (Field field in fields)
            {
                if (!field.Placed)
                    continue;
                Gdip.FillRoundRect(g, Gdip.Argb(FieldBg), field.X, field.Y + sy, field.W, field.H, S(10));
                Gdip.StrokeRoundRect(g, focus == field.Hwnd ? Gdip.Argb(Accent) : Gdip.Argb(CardBorder, 0.08f),
                                     field.X, field.Y + sy, field.W, field.H, S(10), border);
            }

            for (int i = 0; i < 4; i++)
            {
                float x = bannerX + i * (statusW + S(12));
                Gdip.FillRoundRect(g, Gdip.Argb(Bg, 0.5f), x, statusY + sy, statusW, statusH, S(12));
                Gdip.StrokeRoundRect(g, Gdip.Argb(CardBorder, 0.08f), x, statusY + sy, statusW, statusH, S(12), border);
            }

            Gdip.FillRoundRect(g, Gdip.Argb(FieldBg), logX, logY + sy, logW, logH, S(10));
            Gdip.StrokeRoundRect(g, Gdip.Argb(CardBorder, 0.08f), logX, logY + sy, logW, logH, S(10), border);
        }

        private void RenderExpertText(nint dc, float sy)
        {
            foreach (var label in labels)
                DrawLabel(dc, Fonts.Label, label.Text, label.X, label.Y + sy, 10000, Fonts.Label.LineHeight, TextMuted, false);
            foreach (var hint in hints)
                hint.Block.Draw(dc, hint.X, hint.Y + sy, ColorRef(HintText), ColorRef(HintLink));

            float border = MathF.Max(1, MathF.Round(S(1)));
            for (int i = 0; i < 4; i++)
            {
                if (status[i].Label == null)
                    continue;
                float x = bannerX + i * (statusW + S(12)) + border + S(14);
                float top = statusY + sy + border + S(12);
                DrawLabel(dc, Fonts.StatusLabel, status[i].Label, x, top, 10000, Fonts.StatusLabel.LineHeight, TextMuted, false);
                DrawLabel(dc, Fonts.StatusValue, EllipsisFit(Fonts.StatusValue, status[i].Value, statusW - (border + S(14)) * 2),
                          x, top + Fonts.StatusLabel.LineHeight + S(4), 10000, Fonts.StatusValue.LineHeight, status[i].Color, false);
            }
        }

        private string ExpertLinkAt(float x, float contentY)
        {
            foreach (var hint in hints)
            {
                string url = hint.Block.LinkAt(x - hint.X, contentY - hint.Y);
                if (url != null)
                    return url;
            }
            return null;
        }
    }
}
