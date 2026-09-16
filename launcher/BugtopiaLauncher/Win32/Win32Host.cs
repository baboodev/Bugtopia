using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// The launcher as a native window - what ui.html draws, without a browser engine under it.
    ///
    /// It talks to <see cref="Api"/> exactly as the page does: JSON commands in through
    /// <see cref="Api.Dispatch"/>, JSON replies and events out through the send callback. So the Api is
    /// the same code in both builds, and this is a port of the page's script rather than a second
    /// implementation of the launcher. Where it mirrors a function of the page, it keeps its name.
    ///
    /// This file holds the bridge, the window and the simple screen; Win32Host.Expert.cs the expert view.
    ///
    /// Everything here runs on the window thread. The Api answers from worker threads during jobs, so
    /// what it sends is queued and posted, and picked up by the message loop.
    /// </summary>
    internal sealed unsafe partial class Win32Host : Surface, IDialogs
    {
        private const uint WM_INBOX = WM_APP + 1, WM_RESIZE_REQUEST = WM_APP + 2, WM_REVEAL = WM_APP + 3;
        private const nuint TimerReveal = 1, TimerCountdown = 2, TimerSweep = 3;
        private const int IdDetect = 101, IdBrowse = 102, IdArchive = 103, IdCancel = 104, IdPlay = 105, IdAuto = 106,
                          IdExpert = 107;

        /// <summary>The page's rocket, path for path.</summary>
        private static readonly string[] Rocket =
        {
            "M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z",
            "M12 15l-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z",
            "M9 12l-4 4",
            "M15 9l4-4",
        };

        // .step.working::before and .step::before, as gradient stops.
        private static readonly uint[] SweepColors =
        {
            Gdip.Argb(Accent, 0), Gdip.Argb(Accent, 0.10f), Gdip.Argb(Accent, 0.40f), Gdip.Argb(Accent, 0.10f), Gdip.Argb(Accent, 0),
        };
        private static readonly float[] SweepStops = { 0f, 0.35f, 0.5f, 0.65f, 1f };
        private static readonly uint[] FillColors = { Gdip.Argb(Accent, 0.45f), Gdip.Argb(Accent, 0.18f) };
        private static readonly float[] FillStops = { 0f, 1f };

        private static readonly string[] MarkDone = { "M20 6L9 17L4 12" };
        private static readonly string[] MarkTodo = { "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12", "M12 8L12 13", "dot:12,16.5" };
        private static readonly string[] MarkInfo = { "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12", "M12 11L12 16", "dot:12,7.5" };

        private sealed class Card
        {
            internal string Key, Title;
            internal bool Hidden;
            internal string Mark = "info";
            internal List<TextRun> Detail = new List<TextRun>();
            internal Button[] Actions = Array.Empty<Button>();
            internal bool[] Shown = Array.Empty<bool>();
            internal bool HasActions;
            internal int Progress;
            internal bool Working;

            // Layout, in content pixels (before scrolling).
            internal float X, Y, W, H, MarkY, BodyX, BodyY;
            internal TextBlock Block;
        }

        private Api api;
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private int inboxPosted;
        private readonly Dictionary<string, (Action<JsonElement> Ok, Action<string> Fail)> pending =
            new Dictionary<string, (Action<JsonElement>, Action<string>)>();
        private int nextId = 1;

        private JsonDocument stateDoc;
        private JsonElement state;
        private bool haveState, busy;

        /// <summary>body.expert: which of the two views is on screen, from the state once it arrives.</summary>
        private bool expert;

        private string workingTarget = "", workingText = "";
        private string progressTarget = "";
        private int progressValue, bannerProgress;

        private string bannerText = "";
        private bool bannerWarning;

        private bool windowShown, autoArmed, countdown;
        private int autoLeft;
        private ConfirmWindow modal;

        private Card cardGame, cardClean, cardBepInEx, cardMod, cardInterop;
        private Card[] cards;
        private Button expertBox, detect, browse, archive, cancel, play, auto;

        private nint logo;
        private float logoWidth, logoHeight;

        // Layout results, in content pixels.
        private int scrollY, contentHeight;
        private float logoX, logoY, logoW, logoH, titleX, titleY, versionX, versionY;
        private TextBlock versionBlock, bannerBlock;
        private float bannerX, bannerY, bannerW, bannerH;
        private float hintY, hintX, kbdX, kbdW, kbdH;
        private string pressedLink;

        internal static int Run() => new Win32Host().Start();

        private int Start()
        {
            Gdip.Startup();
            PreferDarkMode();
            logo = Gdip.LoadImage(ReadResource("logo.png"), out logoWidth, out logoHeight);

            api = new Api(Send, this);
            expert = api.Expert;

            Create("BugtopiaLauncher", "Bugtopia", WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_VSCROLL, WS_EX_CONTROLPARENT,
                   CW_USEDEFAULT, CW_USEDEFAULT, Api.WindowWidth, Api.WindowHeight(expert), 0, false);
            DarkTitleBar(Hwnd);
            DarkControl(Hwnd);   // the scrollbar
            SetScale(GetDpiForWindow(Hwnd));
            SetIcons();
            CreateControls();
            SizeAndCentre(Api.WindowWidth, Api.WindowHeight(expert));

            Call("state", null, SetState, error => say(error, true));

            // A safety net, as in PhotinoHost: a window must appear even if the first state never does.
            SetTimer(Hwnd, TimerReveal, 5000, 0);

            MSG msg;
            while (GetMessageW(&msg, 0, 0, 0) > 0)
            {
                // Any key stops the countdown, not only Escape: the reflex when one appears is to hit something.
                if (countdown && (msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN))
                    stopAutoLaunch();

                if (modal != null && modal.Hwnd != 0 && IsDialogMessageW(modal.Hwnd, &msg) != 0)
                    continue;
                if (IsDialogMessageW(Hwnd, &msg) != 0)
                    continue;
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
            return 0;
        }

        /// <summary>
        /// Every control, created in the page's document order - which is the order Tab walks them in.
        /// </summary>
        private void CreateControls()
        {
            expertBox = AddButton(IdExpert, ButtonKind.Checkbox, "Expert mode");

            detect = AddButton(IdDetect, ButtonKind.Secondary, "Detect");
            browse = AddButton(IdBrowse, ButtonKind.Secondary, "Browse...");
            archive = AddButton(IdArchive, ButtonKind.Secondary, "Choose the zip...");

            cardGame = new Card { Key = "game", Title = "Heartopia", Actions = new[] { detect, browse }, Shown = new bool[2], HasActions = true };
            cardClean = new Card { Key = "clean", Title = "Game folder" };
            cardBepInEx = new Card { Key = "bepinex", Title = "BepInEx", Actions = new[] { archive }, Shown = new bool[1], HasActions = true };
            cardMod = new Card { Key = "mod", Title = "Bugtopia", Hidden = true };
            cardInterop = new Card { Key = "interop", Title = "Interop assemblies" };
            cards = new[] { cardGame, cardClean, cardBepInEx, cardMod, cardInterop };

            CreateExpertControls();

            cancel = AddButton(IdCancel, ButtonKind.Danger, "Cancel");
            play = AddButton(IdPlay, ButtonKind.PrimaryLarge, "Launch Heartopia", Rocket);
            auto = AddButton(IdAuto, ButtonKind.Checkbox, "Launch automatically");
        }

        private static byte[] ReadResource(string name)
        {
            using Stream stream = typeof(Win32Host).Assembly.GetManifestResourceStream(name);
            if (stream == null)
                return null;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        /// <summary>The exe's own icon resource (group 32512 in Bugtopia.rc), at the sizes this DPI wants.</summary>
        private void SetIcons()
        {
            uint dpi = GetDpiForWindow(Hwnd);
            nint instance = GetModuleHandleW(null);
            nint big = LoadImageW(instance, 32512, 1, GetSystemMetricsForDpi(SM_CXICON, dpi), GetSystemMetricsForDpi(SM_CYICON, dpi), 0);
            nint small = LoadImageW(instance, 32512, 1, GetSystemMetricsForDpi(SM_CXSMICON, dpi), GetSystemMetricsForDpi(SM_CYSMICON, dpi), 0);
            if (big != 0)
                SendMessageW(Hwnd, WM_SETICON, (nint)ICON_BIG, big);
            if (small != 0)
                SendMessageW(Hwnd, WM_SETICON, (nint)ICON_SMALL, small);
        }

        private void SizeAndCentre(int cssWidth, int cssHeight)
        {
            int w = (int)MathF.Round(cssWidth * Scale), h = (int)MathF.Round(cssHeight * Scale);
            nint monitor = MonitorFromWindow(Hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            GetMonitorInfoW(monitor, &info);
            RECT work = info.rcWork;
            w = Math.Min(w, work.Width);
            h = Math.Min(h, work.Height);
            SetWindowPos(Hwnd, 0, work.left + (work.Width - w) / 2, work.top + (work.Height - h) / 2, w, h,
                         SWP_NOZORDER | SWP_NOACTIVATE);
        }

        protected override void Report(Exception ex) => api?.ReportUnhandled(ex);

        internal void ReportFromDialog(Exception ex) => Report(ex);

        // ---- the bridge to Api -------------------------------------------------

        /// <summary>What the Api sends, from whatever thread it is on. One post per burst.</summary>
        private void Send(string json)
        {
            inbox.Enqueue(json);
            nint hwnd = Hwnd;
            if (hwnd != 0 && Interlocked.Exchange(ref inboxPosted, 1) == 0)
                PostMessageW(hwnd, WM_INBOX, 0, 0);
        }

        private void DrainInbox()
        {
            Interlocked.Exchange(ref inboxPosted, 0);
            while (inbox.TryDequeue(out string json))
                Receive(json);
        }

        private void Call(string cmd, Action<Utf8JsonWriter> args, Action<JsonElement> ok = null, Action<string> fail = null)
        {
            string id = "w" + nextId++;
            pending[id] = (ok, fail);

            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("cmd", cmd);
                w.WritePropertyName("args");
                w.WriteStartObject();
                args?.Invoke(w);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            api.Dispatch(Encoding.UTF8.GetString(buffer.ToArray()));
        }

        /// <summary>The page's run(): a command whose answer, when it is the state, redraws the screen.</summary>
        private void run(string cmd, Action<Utf8JsonWriter> args = null)
        {
            Call(cmd, args,
                 data =>
                 {
                     if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("storage", out _))
                         SetState(data);
                 },
                 error =>
                 {
                     appendLog("• " + error);
                     say(error, true);
                 });
        }

        private void Receive(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("event", out JsonElement ev))
            {
                switch (ev.GetString())
                {
                    case "log":
                        appendLog(Str(root, "text"));
                        break;
                    case "busy":
                        busy = root.GetProperty("value").GetBoolean();
                        if (!busy)
                        {
                            setProgress("", 0);
                            setWorking("", "");
                        }
                        render();
                        break;
                    case "progress":
                        setProgress(Str(root, "target"), root.GetProperty("value").GetInt32());
                        render();
                        break;
                    case "working":
                        setWorking(Str(root, "target"), Str(root, "text"));
                        render();
                        break;
                    case "state":
                        // A state push means the step that was working has finished.
                        setWorking("", "");
                        SetState(root.GetProperty("data"));
                        break;
                    case "confirm":
                        ask(root);
                        break;
                }
                return;
            }

            string id = Str(root, "id");
            if (id == null || !pending.Remove(id, out var callbacks))
                return;
            if (root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
                callbacks.Ok?.Invoke(root.TryGetProperty("data", out JsonElement data) ? data : default);
            else
                callbacks.Fail?.Invoke(Str(root, "error") ?? "Failed.");
        }

        private bool profilesLoaded;

        private void SetState(JsonElement data)
        {
            stateDoc?.Dispose();
            stateDoc = JsonDocument.Parse(data.GetRawText());
            state = stateDoc.RootElement;
            haveState = true;
            render();

            // The page's refresh() loads the profiles once, after the first state.
            if (!profilesLoaded)
            {
                profilesLoaded = true;
                loadProfiles();
            }
        }

        // ---- state accessors, with the page's truthiness ------------------------

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private string S(string name) => haveState ? Str(state, name) ?? "" : "";

        private bool B(string name) =>
            haveState && state.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        private bool IsFalse(string name) =>
            haveState && state.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.False;

        private JsonElement Existing =>
            haveState && state.TryGetProperty("existing", out JsonElement v) && v.ValueKind == JsonValueKind.Object ? v : default;

        private static bool Truthy(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
                return false;
            return v.ValueKind == JsonValueKind.True ||
                   (v.ValueKind == JsonValueKind.String && v.GetString().Length > 0) ||
                   (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0);
        }

        // ---- the page's script, ported ------------------------------------------

        /// <summary>Fills one step card, and clears every other; in expert mode, the banner instead.</summary>
        private void setProgress(string target, int percent)
        {
            if (!string.IsNullOrEmpty(target))
                setWorking("", "");   // a number replaces the sweep
            progressTarget = target ?? "";
            progressValue = percent;
            // Only in expert mode, which has no cards. Filling both at once showed the same bar twice.
            bannerProgress = expert ? percent : 0;
        }

        private void setWorking(string target, string text)
        {
            workingTarget = target ?? "";
            workingText = text ?? "";
            // Every fill is cleared: one thing runs at a time.
            progressTarget = "";
            progressValue = 0;
        }

        private void say(string text, bool warning)
        {
            bannerText = text ?? "";
            bannerWarning = warning;
            Layout();
            Invalidate();
        }

        private static List<TextRun> Plain(string text) => new List<TextRun> { new TextRun(text) };

        private static void step(Card card, string mark, List<TextRun> detail)
        {
            card.Mark = mark;
            card.Detail = detail;
        }

        private void render()
        {
            if (!haveState)
                return;

            expert = B("expert");
            expertBox.Checked = expert;
            InvalidateRect(expertBox.Hwnd, null, 0);
            auto.Checked = !IsFalse("autoLaunch");
            InvalidateRect(auto.Hwnd, null, 0);
            renderSteps();
            if (expert)
                renderExpert();

            // Launch runs every missing step itself, so the button leads.
            bool ready = B("prepared") && B("hasInterop");
            if (!countdown)
                SetButtonText(play, ready ? "Launch Heartopia" : "Set up and launch");
            play.Kind = expert ? ButtonKind.PrimaryMedium : ButtonKind.PrimaryLarge;

            JsonElement existing = Existing;
            bool gameOk = B("gameOk");
            if (Truthy(existing, "found"))
                say(Truthy(existing, "root")
                    ? "An existing install already loads the mod, from " + Str(existing, "root") +
                      ". Launch will ask before moving it here" + (Truthy(existing, "interop") ? ", interop assemblies and all." : ".")
                    : (Truthy(existing, "melon") ? "MelonLoader" : "Another loader") +
                      " is installed in the game folder and boots before this launcher can. Launch will ask before removing it.",
                    false);
            else if (!gameOk && S("game").Length == 0)
                say("Heartopia was not found automatically — press Detect, or point at it with Browse.", false);
            else if (!gameOk)
                say("That folder is not an IL2CPP Heartopia build — point at the game with Browse.", true);
            else if (!B("prepared") && S("source").Length == 0 && !B("downloads"))
                say("Point at the BepInEx zip — Launch does everything after that.", false);
            else if (!ready)
                say("Nothing else to press: Launch sets the rest up first, which takes several minutes.", false);
            else
                say("Ready.", false);

            Enable(play, !busy);
            Enable(detect, !busy);
            Enable(browse, !busy);
            Enable(archive, !busy);

            bool sweeping = false;
            if (!expert)
                foreach (Card card in cards)
                    sweeping |= card.Working && !card.Hidden;
            if (sweeping)
                SetTimer(Hwnd, TimerSweep, 33, 0);
            else
                KillTimer(Hwnd, TimerSweep);

            Layout();
            Invalidate();
            armAutoLaunch();
            reveal();
        }

        /// <summary>The simple screen. Everything here is derived from the same state the expert panel shows.</summary>
        private void renderSteps()
        {
            JsonElement existing = Existing;
            bool gameOk = B("gameOk");

            step(cardGame, gameOk ? "done" : "todo",
                 Plain(gameOk ? "Found" : S("game").Length > 0 ? "Not an IL2CPP build" : "Not found"));
            cardGame.Shown[0] = cardGame.Shown[1] = !gameOk;

            if (!gameOk)
            {
                // Nothing to be clean about yet: this card is an opinion about a folder, and there is no folder.
                step(cardClean, "info", Plain("Checked once the game is found."));
            }
            else if (Truthy(existing, "found"))
            {
                step(cardClean, "info", Plain(Truthy(existing, "root")
                    ? "An existing install at " + Str(existing, "root") + " loads the mod. Launch will ask before moving it here" +
                      (Truthy(existing, "interop") ? ", interop assemblies and all." : ".")
                    : (Truthy(existing, "melon") ? "MelonLoader" : "Another loader") +
                      " is installed here and boots first. Launch will ask before removing it."));
            }
            else
            {
                step(cardClean, "done", Plain("Clean - nothing else loads into this game."));
            }

            // Every state that is not "installed" ends with the same link, so the archive is one click away.
            bool prepared = B("prepared"), downloads = B("downloads");
            bool haveSource = S("source").Length > 0 || prepared;
            if (prepared)
            {
                step(cardBepInEx, "done", Plain("Installed"));
            }
            else
            {
                string lead = haveSource
                    ? (S("sourcePath").Length > 0 ? "Launch installs " + S("sourcePath") + "."
                                                  : "Not installed - Launch installs the archive you chose.")
                    : downloads ? "Not installed - Launch downloads it."
                    : "Not installed - choose the zip.";
                step(cardBepInEx, haveSource || downloads ? "info" : "todo", new List<TextRun>
                {
                    new TextRun(lead + " "),
                    new TextRun("Download it here", S("bepInExUrl")),
                    new TextRun("."),
                });
            }
            cardBepInEx.Shown[0] = !prepared && !downloads;

            // Only an online build has anything to say here: an offline one installs the copy it carries.
            cardMod.Hidden = !B("pluginFromGitHub");
            if (!cardMod.Hidden)
            {
                string update = S("modUpdate");
                step(cardMod, B("hasPlugin") ? "done" : "info",
                     Plain(!B("hasPlugin") ? "Not installed - Launch downloads it."
                           : update.Length > 0 ? "Installed - Launch updates it to " + update + "."
                           : "Installed"));
            }

            bool hasInterop = B("hasInterop"), stale = B("interopStale");
            step(cardInterop, !hasInterop ? "info" : stale ? "todo" : "done",
                 Plain(!hasInterop ? "Not built - Launch builds them, which takes several minutes, once."
                       : stale ? "Expired - the game has changed since; Launch rebuilds them."
                       : "Up to date"));

            // keepWorking: a state push rebuilds every card, which knows nothing of the step running now.
            foreach (Card card in cards)
            {
                card.Working = workingTarget.Length > 0 && card.Key == workingTarget;
                if (card.Working)
                    card.Detail = Plain(workingText);
                card.Progress = progressTarget.Length > 0 && card.Key == progressTarget ? progressValue : 0;
            }
        }

        // ---- starting on its own ------------------------------------------------

        /// <summary>Every card that is showing is green - which is literally what the user sees.</summary>
        private bool everythingGreen()
        {
            int shown = 0;
            foreach (Card card in cards)
            {
                if (card.Hidden)
                    continue;
                shown++;
                if (card.Mark != "done")
                    return false;
            }
            return shown > 0;
        }

        private void armAutoLaunch()
        {
            // Not in expert mode: someone with every field open is configuring, not waiting to play. And
            // not before the window is up: a countdown started off-screen has spent part of its three
            // seconds before anyone can see or stop it.
            if (autoArmed || countdown || busy || !windowShown || !haveState || expert ||
                IsFalse("autoLaunch") || !everythingGreen())
                return;

            autoArmed = true;
            autoLeft = 3;
            countdown = true;
            showCountdown();
            SetTimer(Hwnd, TimerCountdown, 1000, 0);
            Layout();
            Invalidate();
        }

        private void showCountdown() => SetButtonText(play, "Launching in " + autoLeft + "…");

        private void stopAutoLaunch()
        {
            if (!countdown)
                return;
            KillTimer(Hwnd, TimerCountdown);
            countdown = false;
            SetButtonText(play, "Launch Heartopia");
            Layout();
            Invalidate();
        }

        /// <summary>Puts the window up once, after the first full frame exists to show.</summary>
        private void reveal()
        {
            if (windowShown)
                return;
            windowShown = true;
            KillTimer(Hwnd, TimerReveal);
            ShowWindow(Hwnd, SW_SHOW);
            UpdateWindow(Hwnd);
            SetForegroundWindow(Hwnd);
            armAutoLaunch();
        }

        // ---- confirmation --------------------------------------------------------

        private void ask(JsonElement m)
        {
            string placeholder = m.TryGetProperty("placeholder", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            modal = new ConfirmWindow(this, Str(m, "ticket"), Str(m, "title") ?? "", Str(m, "text") ?? "",
                                      Str(m, "confirmLabel") ?? "Continue", placeholder, Str(m, "initial") ?? "");
            Dimmed = true;
            EnableWindow(Hwnd, 0);
            Invalidate();
            modal.Open();
        }

        internal void ConfirmClosed(ConfirmWindow window, string ticket, bool ok, string value)
        {
            if (modal == window)
                modal = null;
            Dimmed = false;
            EnableWindow(Hwnd, 1);
            SetForegroundWindow(Hwnd);
            Invalidate();
            Call("confirmResult", w =>
            {
                w.WriteString("ticket", ticket);
                w.WriteBoolean("ok", ok);
                w.WriteString("value", value ?? "");
            });
        }

        // ---- commands ----------------------------------------------------------------

        /// <summary>The page's #detect: shared by the simple card and the expert field.</summary>
        private void detectGame()
        {
            Call("detectGame", null, data =>
            {
                string found = data.ValueKind == JsonValueKind.String ? data.GetString() : null;
                if (!string.IsNullOrEmpty(found))
                    save("game", found);
                else
                    say("No Heartopia install found. Point at it with Browse.", true);
            }, e => say(e, true));
        }

        /// <summary>The page's save(): one path changed, the rest as they are.</summary>
        private void save(string key, string value)
        {
            Call("setPaths", w => w.WriteString(key, value ?? ""), SetState, e =>
            {
                appendLog("• " + e);
                say(e, true);
            });
        }

        /// <summary>A data-pick button: a folder, or with data-file a zip, into one of the path fields.</summary>
        private void pick(string key, bool file, string title)
        {
            Call(file ? "pickFile" : "pickFolder", w =>
            {
                w.WriteString("title", title);
                if (!file)
                    w.WriteString("current", S(key));
            }, data =>
            {
                string chosen = data.ValueKind == JsonValueKind.String ? data.GetString() : null;
                if (!string.IsNullOrEmpty(chosen))
                    save(key, chosen);
            });
        }

        // Both views take the zip the same way: it is unpacked into storage.
        private void chooseArchive()
        {
            Call("pickFile", w => w.WriteString("title", "BepInEx-Unity.IL2CPP-win-x64 archive"), data =>
            {
                string chosen = data.ValueKind == JsonValueKind.String ? data.GetString() : null;
                if (!string.IsNullOrEmpty(chosen))
                    run("useArchive", w => w.WriteString("path", chosen));
            });
        }

        private void Clicked(int id)
        {
            switch (id)
            {
                case IdDetect:
                    detectGame();
                    return;
                case IdBrowse:
                    pick("game", false, "Select folder");
                    return;
                case IdArchive:
                    chooseArchive();
                    return;
                case IdCancel:
                    stopAutoLaunch();
                    return;
                case IdPlay:
                    stopAutoLaunch();
                    run("play");
                    return;

                case IdExpert:
                {
                    stopAutoLaunch();
                    bool value = !expertBox.Checked;
                    expertBox.Checked = value;
                    InvalidateRect(expertBox.Hwnd, null, 0);
                    run("setExpert", w => w.WriteBoolean("value", value));
                    return;
                }

                // Touching it means someone is here, so no countdown for the rest of this run whichever
                // way it was set. The choice is for next time.
                case IdAuto:
                {
                    auto.Checked = !auto.Checked;
                    InvalidateRect(auto.Hwnd, null, 0);
                    autoArmed = true;
                    stopAutoLaunch();
                    bool value = auto.Checked;
                    run("setAutoLaunch", w => w.WriteBoolean("value", value));
                    return;
                }
            }
            ClickedExpert(id);
        }

        // ---- layout --------------------------------------------------------------

        private void Layout()
        {
            if (Fonts == null || cards == null)
                return;

            RECT rc;
            GetClientRect(Hwnd, &rc);
            float clientW = rc.Width, clientH = rc.Height;
            float padX = S(32), padY = S(24);
            float width = MathF.Max(S(200), clientW - padX * 2);

            foreach (Button b in Buttons)
                b.Placed = false;
            BeginExpertLayout();

            float y = LayoutHeader(padY, padX, width);

            y = expert ? LayoutExpert(y, padX, width, clientH) : LayoutSimple(y, padX, width);

            // Whatever this pass did not put somewhere belongs to the other view.
            foreach (Button b in Buttons)
                if (!b.Placed)
                    Place(b, 0, 0, 0, 0, false);
            EndExpertLayout();

            contentHeight = (int)MathF.Ceiling(y + padY);
            UpdateScroll((int)clientH);
        }

        /// <summary>The brand in the middle of the window, the logo to its left, the Expert switch on the right.</summary>
        private float LayoutHeader(float y, float padX, float width)
        {
            const string title = "Bugtopia Launcher";
            var versionRuns = new List<TextRun> { new TextRun(S("version")) };
            if (S("updateVersion").Length > 0)
            {
                versionRuns.Add(new TextRun("  ·  "));
                versionRuns.Add(new TextRun(S("updateVersion") + " available", S("releasesPage")));
            }
            versionBlock = TextBlock.Layout(versionRuns, Fonts.Subtitle, Fonts.SubtitleLink, float.MaxValue / 4, Fonts.Subtitle.LineHeight);

            float titleW = Fonts.Measure(Fonts.Title, title);
            float brandW = MathF.Max(titleW, versionBlock.Width);
            float brandH = Fonts.Title.LineHeight + S(1) + Fonts.Subtitle.LineHeight;
            logoH = logo != 0 ? S(60) : 0;
            logoW = logo != 0 && logoHeight > 0 ? logoH * logoWidth / logoHeight : 0;
            var box = Measure(expertBox);
            float headerH = MathF.Max(logoH, MathF.Max(brandH, box.Height));
            float side = (width - brandW - S(16) * 2) / 2;

            logoX = padX + side - S(16) - logoW;
            logoY = y + (headerH - logoH) / 2;
            titleX = padX + (width - titleW) / 2;
            titleY = y + (headerH - brandH) / 2;
            versionX = padX + (width - versionBlock.Width) / 2;
            versionY = titleY + Fonts.Title.LineHeight + S(1);
            PlaceScrolled(expertBox, padX + width - box.Width, y + (headerH - box.Height) / 2, box.Width, box.Height, true);
            return y + headerH + S(14);
        }

        private float LayoutSimple(float y, float padX, float width)
        {
            bool first = true;
            foreach (Card card in cards)
            {
                if (card.Hidden)
                    continue;
                if (!first)
                    y += S(10);
                first = false;
                LayoutCard(card, padX, y, width);
                y += card.H;
            }
            y += S(14);

            // The banner, only when it has something of its own to report.
            if (bannerWarning && bannerText.Length > 0)
                y = LayoutBanner(y, padX, width) + S(14);
            else
                bannerBlock = null;

            // Footer: [Cancel] [Launch], the checkbox, the hint.
            var playSize = Measure(play);
            var cancelSize = Measure(cancel);
            float rowH = MathF.Max(playSize.Height, countdown ? cancelSize.Height : 0);
            float rowW = playSize.Width + (countdown ? cancelSize.Width + S(12) : 0);
            float rowX = padX + (width - rowW) / 2;
            if (countdown)
                PlaceScrolled(cancel, rowX, y + (rowH - cancelSize.Height) / 2, cancelSize.Width, cancelSize.Height, true);
            PlaceScrolled(play, countdown ? rowX + cancelSize.Width + S(12) : rowX, y + (rowH - playSize.Height) / 2,
                          playSize.Width, playSize.Height, true);
            y += rowH + S(10);

            var autoSize = Measure(auto);
            PlaceScrolled(auto, padX + (width - autoSize.Width) / 2, y, autoSize.Width, autoSize.Height, true);
            y += autoSize.Height + S(10);

            return LayoutHint(y, padX, width);
        }

        private float LayoutBanner(float y, float padX, float width)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            bannerBlock = TextBlock.Layout(new List<TextRun> { new TextRun(bannerText) }, Fonts.Banner, Fonts.Banner,
                                           width - S(16) * 2 - border * 2, Fonts.Banner.LineHeight);
            bannerX = padX;
            bannerY = y;
            bannerW = width;
            bannerH = border * 2 + S(10) * 2 + bannerBlock.Height;
            return y + bannerH;
        }

        private const string HintBefore = "In game, press ", HintKey = "Insert", HintAfter = " to open the mod menu.";

        /// <summary>.launch-hint, centred: text, a key cap, text.</summary>
        private float LayoutHint(float y, float padX, float width)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            kbdW = Fonts.Measure(Fonts.Kbd, HintKey) + S(7) * 2 + border * 2;
            kbdH = Fonts.Kbd.LineHeight + S(1) * 2 + border * 2;
            float hintW = Fonts.Measure(Fonts.Hint, HintBefore) + S(1) + kbdW + S(1) + Fonts.Measure(Fonts.Hint, HintAfter);
            hintX = padX + (width - hintW) / 2;
            kbdX = hintX + Fonts.Measure(Fonts.Hint, HintBefore) + S(1);
            hintY = y;
            return y + MathF.Max(Fonts.Hint.LineHeight, kbdH);
        }

        private void LayoutCard(Card card, float x, float y, float width)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            float padH = S(16), padV = S(13), mark = S(22), gap = S(14);
            float innerX = x + border + padH, innerW = width - (border + padH) * 2;

            float actionsW = 0, actionsH = 0;
            var sizes = new (float W, float H)[card.Actions.Length];
            for (int i = 0; i < card.Actions.Length; i++)
            {
                if (!card.Shown[i])
                    continue;
                sizes[i] = Measure(card.Actions[i]);
                actionsW += (actionsW > 0 ? S(8) : 0) + sizes[i].W;
                actionsH = MathF.Max(actionsH, sizes[i].H);
            }

            float bodyW = innerW - mark - gap - (card.HasActions ? actionsW + gap : 0);
            card.Block = TextBlock.Layout(card.Detail, Fonts.Detail, Fonts.DetailLink, bodyW, Fonts.Detail.Em * 1.4f);
            float bodyH = Fonts.StepTitle.LineHeight + S(3) + card.Block.Height;
            float contentH = MathF.Max(mark, MathF.Max(bodyH, actionsH));

            card.X = x;
            card.Y = y;
            card.W = width;
            card.H = (border + padV) * 2 + contentH;
            float top = y + border + padV;
            card.MarkY = top + (contentH - mark) / 2;
            card.BodyX = innerX + mark + gap;
            card.BodyY = top + (contentH - bodyH) / 2;

            float ax = x + width - border - padH - actionsW;
            for (int i = 0; i < card.Actions.Length; i++)
            {
                if (!card.Shown[i])
                    continue;
                PlaceScrolled(card.Actions[i], ax, top + (contentH - sizes[i].H) / 2, sizes[i].W, sizes[i].H, true);
                ax += sizes[i].W + S(8);
            }
        }

        private void PlaceScrolled(Button b, float x, float y, float w, float h, bool visible) =>
            Place(b, x, y - scrollY, w, h, visible);

        private bool updatingScroll;

        private void UpdateScroll(int clientH)
        {
            int max = Math.Max(0, contentHeight - clientH);
            int clamped = Math.Clamp(scrollY, 0, max);
            var info = new SCROLLINFO
            {
                cbSize = (uint)sizeof(SCROLLINFO),
                fMask = SIF_RANGE | SIF_PAGE | SIF_POS,
                nMin = 0,
                nMax = Math.Max(0, contentHeight - 1),
                nPage = (uint)Math.Max(0, clientH),
                nPos = clamped,
            };

            bool scrollChanged = clamped != scrollY;
            scrollY = clamped;
            if (updatingScroll)
                return;

            // Showing or hiding the bar changes the client width, and so the wrapping: lay out once more.
            updatingScroll = true;
            try
            {
                RECT before;
                GetClientRect(Hwnd, &before);
                SetScrollInfo(Hwnd, SB_VERT, &info, 1);
                RECT after;
                GetClientRect(Hwnd, &after);
                if (after.Width != before.Width || after.Height != before.Height || scrollChanged)
                    Layout();
            }
            finally
            {
                updatingScroll = false;
            }
        }

        private void ScrollTo(int y)
        {
            RECT rc;
            GetClientRect(Hwnd, &rc);
            int clamped = Math.Clamp(y, 0, Math.Max(0, contentHeight - rc.Height));
            if (clamped == scrollY)
                return;
            scrollY = clamped;
            Layout();
            Invalidate();
        }

        // ---- painting ------------------------------------------------------------

        protected override void Render(nint dc, int width, int height)
        {
            float sy = -scrollY;
            float border = MathF.Max(1, MathF.Round(S(1)));
            nint g = Gdip.Begin(dc);

            // The page's backdrop: #090d16, two soft glows fixed to the window, the card tint over them.
            Gdip.FillRect(g, Gdip.Argb(0x090d16), 0, 0, width, height);
            float reach = MathF.Sqrt(width * 0.8f * width * 0.8f + height * 0.8f * height * 0.8f) * 0.5f;
            Gdip.FillGlow(g, width * 0.2f, height * 0.2f, reach, Gdip.Argb(Accent, 0.2f));
            Gdip.FillGlow(g, width * 0.8f, height * 0.8f, reach, Gdip.Argb(0xa855f7, 0.15f));
            Gdip.FillRect(g, Gdip.Argb(Bg, 0.75f), 0, 0, width, height);

            if (logo != 0)
                Gdip.DrawImage(g, logo, logoX, logoY + sy, logoW, logoH);

            if (expert)
                RenderExpertShapes(g, sy, border);
            else
                RenderSimpleShapes(g, sy, border);

            if (bannerBlock != null)
            {
                Gdip.FillRoundRect(g, Gdip.Argb(Bg, 0.6f), bannerX, bannerY + sy, bannerW, bannerH, S(10));
                if (bannerProgress > 0)
                    Gdip.FillSweep(g, bannerX, bannerY + sy, bannerW, bannerH, S(10), bannerX,
                                   MathF.Max(2, bannerW * Math.Clamp(bannerProgress, 0, 100) / 100f), FillColors, FillStops);
                Gdip.StrokeRoundRect(g, bannerWarning ? Gdip.Argb(Warning, 0.35f) : Gdip.Argb(CardBorder, 0.06f),
                                     bannerX, bannerY + sy, bannerW, bannerH, S(10), border);
            }

            // Box-shadows of the footer buttons: drawn here, since a button cannot draw outside itself.
            if (IsWindowVisible(play.Hwnd) != 0)
                ButtonGlow(g, play, Accent, play.Hover && IsWindowEnabled(play.Hwnd) != 0);
            if (countdown)
                ButtonGlow(g, cancel, Danger, cancel.Hover);

            Gdip.FillRoundRect(g, Gdip.Argb(0x334155, 0.5f), kbdX, hintY + sy, kbdW, kbdH, S(5));
            Gdip.StrokeRoundRect(g, Gdip.Argb(CardBorder, 0.08f), kbdX, hintY + sy, kbdW, kbdH, S(5), border);
            Gdip.End(g);

            // Text, with GDI.
            SetBkMode(dc, TRANSPARENT);
            DrawLabel(dc, Fonts.Title, "Bugtopia Launcher", titleX, titleY + sy, 10000, Fonts.Title.LineHeight, 0xeef2f7, false);
            versionBlock?.Draw(dc, versionX, versionY + sy, ColorRef(TextMuted), ColorRef(Accent));

            if (expert)
                RenderExpertText(dc, sy);
            else
                RenderSimpleText(dc, sy);

            if (bannerBlock != null)
                bannerBlock.Draw(dc, bannerX + border + S(16), bannerY + sy + border + S(10),
                                 ColorRef(bannerWarning ? Warning : TextMuted), ColorRef(bannerWarning ? Warning : TextMuted));

            float hintLine = MathF.Max(Fonts.Hint.LineHeight, kbdH);
            DrawLabel(dc, Fonts.Hint, HintBefore, hintX, hintY + sy, 10000, hintLine, TextMuted, false);
            DrawLabel(dc, Fonts.Kbd, HintKey, kbdX, hintY + sy, kbdW, kbdH, TextMain, true);
            DrawLabel(dc, Fonts.Hint, HintAfter, kbdX + kbdW + S(1), hintY + sy, 10000, hintLine, TextMuted, false);

            if (Dimmed)
            {
                nint scrim = Gdip.Begin(dc);
                Gdip.FillRect(scrim, Gdip.Argb(0x090d16, 0.72f), 0, 0, width, height);
                Gdip.End(scrim);
            }
        }

        private void RenderSimpleShapes(nint g, float sy, float border)
        {
            float phase = (Environment.TickCount64 % 1500) / 1500f;
            foreach (Card card in cards)
            {
                if (card.Hidden || card.Block == null)
                    continue;
                float cy = card.Y + sy;
                Gdip.FillRoundRect(g, Gdip.Argb(Bg, 0.5f), card.X, cy, card.W, card.H, S(12));

                if (card.Working)
                {
                    // Work with nothing to count sweeps instead of filling.
                    float bandX = card.X + (phase * 2 - 1) * card.W;
                    Gdip.FillSweep(g, card.X, cy, card.W, card.H, S(12), bandX, card.W, SweepColors, SweepStops);
                }
                else if (card.Progress > 0)
                {
                    float fill = card.W * Math.Clamp(card.Progress, 0, 100) / 100f;
                    Gdip.FillSweep(g, card.X, cy, card.W, card.H, S(12), card.X, MathF.Max(2, fill), FillColors, FillStops);
                }

                Gdip.StrokeRoundRect(g, Gdip.Argb(CardBorder, 0.08f), card.X, cy, card.W, card.H, S(12), border);

                float markX = card.X + border + S(16);
                switch (card.Mark)
                {
                    case "done":
                        Gdip.StrokeIcon(g, MarkDone, markX, card.MarkY + sy, S(22), Gdip.Argb(Success), 2.5f);
                        break;
                    case "todo":
                        Gdip.StrokeIcon(g, MarkTodo, markX, card.MarkY + sy, S(22), Gdip.Argb(Warning), 2f);
                        break;
                    default:
                        Gdip.StrokeIcon(g, MarkInfo, markX, card.MarkY + sy, S(22), Gdip.Argb(TextMuted), 2f);
                        break;
                }
            }
        }

        private void RenderSimpleText(nint dc, float sy)
        {
            foreach (Card card in cards)
            {
                if (card.Hidden || card.Block == null)
                    continue;
                DrawLabel(dc, Fonts.StepTitle, card.Title, card.BodyX, card.BodyY + sy, 10000, Fonts.StepTitle.LineHeight, TextMain, false);
                card.Block.Draw(dc, card.BodyX, card.BodyY + sy + Fonts.StepTitle.LineHeight + S(3), ColorRef(TextMuted), ColorRef(Accent));
            }
        }

        /// <summary>box-shadow: 0 4px 14px (hover: 0 6px 20px) in the button's own colour.</summary>
        private void ButtonGlow(nint g, Button b, uint rgb, bool hover)
        {
            RECT r = b.Bounds;
            float offset = S(hover ? 6 : 4), blur = S(hover ? 20 : 14);
            Gdip.Shadow(g, r.left, r.top + offset, r.Width, r.Height, S(10), blur, rgb, hover ? 0.6f : 0.4f);
        }

        protected override void HoverChanged(Button button)
        {
            if (button == play || button == cancel)
                Invalidate();   // the glow under it lives in the window's frame
        }

        // ---- messages -------------------------------------------------------------

        private string LinkAt(int x, int y)
        {
            float cy = y + scrollY;
            if (versionBlock != null)
            {
                string url = versionBlock.LinkAt(x - versionX, cy - versionY);
                if (url != null)
                    return url;
            }
            if (expert)
                return ExpertLinkAt(x, cy);

            foreach (Card card in cards)
            {
                if (card.Hidden || card.Block == null)
                    continue;
                string url = card.Block.LinkAt(x - card.BodyX, cy - (card.BodyY + Fonts.StepTitle.LineHeight + S(3)));
                if (url != null)
                    return url;
            }
            return null;
        }

        protected override nint Handle(uint msg, nint w, nint l)
        {
            switch (msg)
            {
                case WM_INBOX:
                    DrainInbox();
                    return 0;

                case WM_SIZE:
                    Layout();
                    Invalidate();
                    return 0;

                case WM_GETMINMAXINFO:
                {
                    var info = (MINMAXINFO*)l;
                    info->ptMinTrackSize.x = (int)(560 * Scale);
                    info->ptMinTrackSize.y = (int)(480 * Scale);
                    return 0;
                }

                case WM_DPICHANGED:
                {
                    SetScale((uint)LoWord(w));
                    SetIcons();
                    ExpertFontsChanged();
                    var suggested = (RECT*)l;
                    SetWindowPos(Hwnd, 0, suggested->left, suggested->top, suggested->Width, suggested->Height,
                                 SWP_NOZORDER | SWP_NOACTIVATE);
                    Layout();
                    Invalidate();
                    return 0;
                }

                case WM_CTLCOLOREDIT:
                case WM_CTLCOLORSTATIC:
                {
                    nint brush = ExpertControlColor(w, l);
                    if (brush != 0)
                        return brush;
                    break;
                }

                case WM_VSCROLL:
                {
                    RECT rc;
                    GetClientRect(Hwnd, &rc);
                    int line = (int)S(40);
                    switch (LoWord(w))
                    {
                        case SB_LINEUP: ScrollTo(scrollY - line); break;
                        case SB_LINEDOWN: ScrollTo(scrollY + line); break;
                        case SB_PAGEUP: ScrollTo(scrollY - rc.Height); break;
                        case SB_PAGEDOWN: ScrollTo(scrollY + rc.Height); break;
                        case SB_TOP: ScrollTo(0); break;
                        case SB_BOTTOM: ScrollTo(contentHeight); break;
                        case SB_THUMBTRACK:
                        case SB_THUMBPOSITION:
                        {
                            var info = new SCROLLINFO { cbSize = (uint)sizeof(SCROLLINFO), fMask = SIF_TRACKPOS };
                            GetScrollInfo(Hwnd, SB_VERT, &info);
                            ScrollTo(info.nTrackPos);
                            break;
                        }
                    }
                    return 0;
                }

                case WM_MOUSEWHEEL:
                {
                    int delta = HiWord(w);
                    uint lines = 3;
                    SystemParametersInfoW(SPI_GETWHEELSCROLLLINES, 0, &lines, 0);
                    ScrollTo(scrollY - delta * (int)Math.Max(1, lines) * (int)S(20) / 120);
                    return 0;
                }

                case WM_SETCURSOR:
                    if (w == Hwnd && LoWord(l) == 1)   // HTCLIENT
                    {
                        POINT pt;
                        GetCursorPos(&pt);
                        ScreenToClient(Hwnd, &pt);
                        if (LinkAt(pt.x, pt.y) != null)
                        {
                            SetCursor(LoadCursorW(0, IDC_HAND));
                            return 1;
                        }
                    }
                    break;

                case WM_LBUTTONDOWN:
                    pressedLink = LinkAt(LoWord(l), HiWord(l));
                    return 0;

                case WM_LBUTTONUP:
                {
                    string url = LinkAt(LoWord(l), HiWord(l));
                    if (url != null && url == pressedLink)
                        Call("openUrl", jw => jw.WriteString("url", url));
                    pressedLink = null;
                    return 0;
                }

                case WM_COMMAND:
                {
                    int id = LoWord(w), code = HiWord(w);
                    if (l != 0 && ButtonFor(l) != null && code == (int)BN_CLICKED)
                    {
                        Clicked(id);
                        return 0;
                    }
                    if (l != 0 && (code == EN_SETFOCUS || code == EN_KILLFOCUS))
                    {
                        Invalidate();   // a text field's border follows its focus
                        return 0;
                    }
                    if (l == 0 && id == IDOK)
                    {
                        // Enter: presses the button that has the focus, as it would on the page.
                        Button focused = ButtonFor(GetFocus());
                        if (focused != null && IsWindowEnabled(focused.Hwnd) != 0)
                            Clicked(focused.Id);
                        else
                            EnterInField(GetFocus());
                        return 0;
                    }
                    if (l == 0 && id == IDCANCEL)
                    {
                        stopAutoLaunch();
                        return 0;
                    }
                    break;
                }

                case WM_TIMER:
                    switch ((nuint)w)
                    {
                        case TimerReveal:
                            reveal();
                            break;
                        case TimerCountdown:
                            autoLeft -= 1;
                            if (autoLeft > 0)
                            {
                                showCountdown();
                            }
                            else
                            {
                                stopAutoLaunch();
                                run("play");
                            }
                            break;
                        case TimerSweep:
                            Invalidate();
                            break;
                    }
                    return 0;

                case WM_RESIZE_REQUEST:
                    SizeAndCentre((int)w, (int)l);
                    return 0;

                case WM_REVEAL:
                    reveal();
                    return 0;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    return 0;
            }
            return base.Handle(msg, w, l);
        }

        // ---- IDialogs --------------------------------------------------------------

        public string PickFolder(string title, string current) => FileDialogs.PickFolder(Hwnd, title, current);

        public string PickFile(string title, string filterName, string[] extensions) =>
            FileDialogs.PickFile(Hwnd, title, filterName, extensions);

        /// <summary>The switch between the two views asks for this: the expert view is taller.</summary>
        public void Resize(int width, int height) => PostMessageW(Hwnd, WM_RESIZE_REQUEST, width, height);

        public void Reveal() => PostMessageW(Hwnd, WM_REVEAL, 0, 0);

        public void Close() => PostMessageW(Hwnd, WM_CLOSE, 0, 0);
    }
}
