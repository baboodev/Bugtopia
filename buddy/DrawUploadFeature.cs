using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace HeartopiaMod
{
    // Draw -> server upload. Plan: docs/plans/2026-06-11-draw-upload-to-server.md
    //
    // The whole protocol layer (WebRequestUtility + every *NetworkCommand) is embedded-Mono only
    // (present in ilspy-dumps, absent from IL2CPP GameAssembly and BepInEx interop), so managed
    // FindLoadedType + SendCommand can't reach it. We originate draw actions the same way the farm
    // does: call the Mono `DrawBoardProtoManager` static methods directly via AuraMono. Those
    // methods build the network command and call WebRequestUtility internally.
    //
    // Task 1: resolve the Mono DrawBoardProtoManager class and smoke-test SendExitDrawing()
    // (no args, harmless outside a drawing session).
    public partial class HeartopiaComplete
    {
        private const string DrawBoardProtoManagerFullName =
            "XDTDataAndProtocol.ProtocolService.DrawBoard.DrawBoardProtoManager";

        private IntPtr drawBoardProtoClass = IntPtr.Zero;
        private float drawUploadNextResolveAttemptAt;

        // Single, easy-to-find work file for the currently-open drawing (extract -> edit -> upload).
        private const string DrawingWorkFileName = "drawing.png";

        // ---- Chunked upload (split the canvas across several DrawingOperation commands) ----
        // A single whole-canvas op is protocol-valid (verified in-game); chunking exists for
        // behavioral plausibility vs the game's natural delta-save cadence and to bound the network
        // frame size on busy canvases. Slider fields live in PicturesDecryptFeature.cs (same class).
        private const int DrawUploadRunsPerChunkMin = 32;
        private const int DrawUploadRunsPerChunkMax = 256;
        private const int DrawUploadRunsPerChunkDefault = 160;
        private const float DrawUploadChunkDelayMin = 0.05f;
        private const float DrawUploadChunkDelayMax = 1f;
        private const float DrawUploadChunkDelayDefault = 0.25f;

        // Handle of the in-flight chunked send (ModCoroutines token); null when idle.
        private object drawUploadChunkCoroutine = null;

        private bool EnsureDrawBoardProtoResolved(out string status)
        {
            status = "DrawBoardProtoManager not resolved";
            if (this.drawBoardProtoClass != IntPtr.Zero)
            {
                status = "DrawBoardProtoManager cached";
                return true;
            }

            float now = UnityEngine.Time.unscaledTime;
            if (this.drawUploadNextResolveAttemptAt > now)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono not ready (enter world first)";
                this.drawUploadNextResolveAttemptAt = now + 2f;
                return false;
            }

            IntPtr classPtr = this.FindAuraMonoClassByFullName(DrawBoardProtoManagerFullName);
            if (classPtr == IntPtr.Zero)
            {
                status = "DrawBoardProtoManager class not found";
                this.drawUploadNextResolveAttemptAt = now + 2f;
                return false;
            }

            this.drawBoardProtoClass = classPtr;
            this.drawUploadNextResolveAttemptAt = -999f;
            status = "DrawBoardProtoManager resolved";
            ModLogger.Msg("[DrawUpload] resolved DrawBoardProtoManager class");
            return true;
        }

        // Resolve DataModule<DrawSystem>.Instance via AuraMono.
        private IntPtr TryGetDrawSystemInstance(out string status)
        {
            status = "DrawSystem unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono not ready (enter world first)";
                return IntPtr.Zero;
            }

            IntPtr drawSystemClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.Module.Draw.DrawSystem");
            if (drawSystemClass == IntPtr.Zero)
            {
                status = "DrawSystem class not found";
                return IntPtr.Zero;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(drawSystemClass);
            if (instance == IntPtr.Zero)
            {
                status = "DrawSystem.Instance null (open Draw album once?)";
                return IntPtr.Zero;
            }

            status = "DrawSystem resolved";
            return instance;
        }

        // DrawSystem.GetInteractionNetId() -> uint : the focused drawboard (easel) net id. Private
        // 0-arg method; returns 0 when the player isn't focused on a board.
        private bool TryGetDrawBoardNetId(IntPtr drawSystemInstance, out uint boardNetId)
        {
            boardNetId = 0U;
            if (drawSystemInstance == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(drawSystemInstance, out IntPtr boxed, "GetInteractionNetId"))
            {
                return false;
            }

            return this.TryUnboxMonoUInt32(boxed, out boardNetId);
        }

        // Cheap "is the drawing session still open" probe for the chunked sender: the focused-board
        // net id drops to 0 as soon as the player leaves the easel/drawing UI. Transient pointers
        // only — nothing here survives past the return.
        private bool IsDrawBoardSessionActive()
        {
            IntPtr drawSystem = this.TryGetDrawSystemInstance(out _);
            return drawSystem != IntPtr.Zero
                && this.TryGetDrawBoardNetId(drawSystem, out uint boardNetId)
                && boardNetId != 0U;
        }

        // ---- Task 2: DrawingBatchOperationNetworkCommand via DrawBoardProtoManager.DrawingOperation ----
        // The dictionary param is built by assembly-qualified type-name (Type.GetType + Activator),
        // the proven pattern on this build (mono_class_bind_generic_parameters crashes here).

        private const string DrawSeqInfoTypeName = "XDT.Scene.Shared.Modules.DrawBoard.DrawingBatchOperationSequentialInfo";
        private const string DrawSeqInfoAsmQualified = DrawSeqInfoTypeName + ", EcsClient";
        private const string DrawListTypeName = "System.Collections.Generic.List`1[[" + DrawSeqInfoAsmQualified + "]]";
        private const string DrawDictTypeName = "System.Collections.Generic.Dictionary`2[[System.Byte],[" + DrawListTypeName + "]]";

        private IntPtr drawSeqInfoClass = IntPtr.Zero;
        private IntPtr drawSeqInfoStartField = IntPtr.Zero;
        private IntPtr drawSeqInfoLengthField = IntPtr.Zero;
        private IntPtr drawListAddMethod = IntPtr.Zero;

        private unsafe IntPtr CreateMonoObjectByTypeName(string typeName)
        {
            if (auraMonoStringNew == null || auraMonoRuntimeInvoke == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr nameStr = auraMonoStringNew(this.auraMonoRootDomain, typeName);
            if (nameStr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* a = stackalloc IntPtr[1];
            a[0] = nameStr;
            IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)a, ref exc);
            if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            exc = IntPtr.Zero;
            a[0] = typeObj;
            IntPtr obj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)a, ref exc);
            return exc == IntPtr.Zero ? obj : IntPtr.Zero;
        }

        private bool EnsureDrawSeqInfoResolved()
        {
            if (this.drawSeqInfoClass != IntPtr.Zero && this.drawSeqInfoStartField != IntPtr.Zero && this.drawSeqInfoLengthField != IntPtr.Zero)
            {
                return true;
            }

            if (auraMonoClassGetFieldFromName == null)
            {
                return false;
            }

            this.drawSeqInfoClass = this.FindAuraMonoClassByFullName(DrawSeqInfoTypeName);
            if (this.drawSeqInfoClass == IntPtr.Zero)
            {
                return false;
            }

            this.drawSeqInfoStartField = auraMonoClassGetFieldFromName(this.drawSeqInfoClass, "Start");
            this.drawSeqInfoLengthField = auraMonoClassGetFieldFromName(this.drawSeqInfoClass, "Length");
            return this.drawSeqInfoStartField != IntPtr.Zero && this.drawSeqInfoLengthField != IntPtr.Zero;
        }

        private unsafe IntPtr CreateDrawSeqInfo(ushort start, ushort length)
        {
            if (this.drawSeqInfoClass == IntPtr.Zero || auraMonoObjectNew == null || auraMonoFieldSetValue == null)
            {
                return IntPtr.Zero;
            }

            IntPtr o = auraMonoObjectNew(this.auraMonoRootDomain, this.drawSeqInfoClass);
            if (o == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            auraMonoFieldSetValue(o, this.drawSeqInfoStartField, (IntPtr)(&start));
            auraMonoFieldSetValue(o, this.drawSeqInfoLengthField, (IntPtr)(&length));
            return o;
        }

        // Build the Mono Dictionary<byte,List<SequentialInfo>> and call DrawBoardProtoManager
        // .DrawingOperation(dict, step, part). Requires an active drawing session (panel open).
        private unsafe bool TrySendDrawingOperation(Dictionary<byte, List<(int Start, int Length)>> runs, int step, int part, out string status)
        {
            status = "draw op unavailable";
            if (runs == null || runs.Count == 0)
            {
                status = "no runs";
                return false;
            }

            if (!this.EnsureDrawBoardProtoResolved(out status))
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
            {
                status = "AuraMono not ready";
                return false;
            }

            if (!this.EnsureDrawSeqInfoResolved())
            {
                status = "SequentialInfo type/fields not resolved";
                return false;
            }

            IntPtr dictObj = this.CreateMonoObjectByTypeName(DrawDictTypeName);
            if (dictObj == IntPtr.Zero)
            {
                status = "Dictionary<byte,List<SeqInfo>> construction failed (type name?)";
                return false;
            }

            IntPtr dictClass = auraMonoObjectGetClass(dictObj);
            IntPtr setItem = this.FindAuraMonoMethodOnHierarchy(dictClass, "set_Item", 2);
            if (setItem == IntPtr.Zero)
            {
                status = "Dictionary.set_Item not found";
                return false;
            }

            int totalRuns = 0;
            foreach (KeyValuePair<byte, List<(int Start, int Length)>> kv in runs)
            {
                IntPtr listObj = this.CreateMonoObjectByTypeName(DrawListTypeName);
                if (listObj == IntPtr.Zero)
                {
                    status = "List<SeqInfo> construction failed";
                    return false;
                }

                if (this.drawListAddMethod == IntPtr.Zero)
                {
                    this.drawListAddMethod = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(listObj), "Add", 1);
                }

                if (this.drawListAddMethod == IntPtr.Zero)
                {
                    status = "List.Add not found";
                    return false;
                }

                foreach ((int Start, int Length) run in kv.Value)
                {
                    IntPtr seq = this.CreateDrawSeqInfo((ushort)run.Start, (ushort)run.Length);
                    if (seq == IntPtr.Zero)
                    {
                        status = "SequentialInfo alloc failed";
                        return false;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr* addArgs = stackalloc IntPtr[1];
                    addArgs[0] = auraMonoObjectUnbox(seq); // value-type arg -> pointer to value
                    auraMonoRuntimeInvoke(this.drawListAddMethod, listObj, (IntPtr)addArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "List.Add threw";
                        return false;
                    }

                    totalRuns++;
                }

                byte keyByte = kv.Key;
                IntPtr excSet = IntPtr.Zero;
                IntPtr* setArgs = stackalloc IntPtr[2];
                setArgs[0] = (IntPtr)(&keyByte); // byte key -> pointer to value
                setArgs[1] = listObj;            // List value -> object pointer
                auraMonoRuntimeInvoke(setItem, dictObj, (IntPtr)setArgs, ref excSet);
                if (excSet != IntPtr.Zero)
                {
                    status = "Dictionary.set_Item threw";
                    return false;
                }
            }

            IntPtr drawOp = this.FindAuraMonoMethodOnHierarchy(this.drawBoardProtoClass, "DrawingOperation", 3);
            if (drawOp == IntPtr.Zero)
            {
                status = "DrawingOperation method not found";
                return false;
            }

            int stepLocal = step;
            int partLocal = part;
            IntPtr excOp = IntPtr.Zero;
            IntPtr* opArgs = stackalloc IntPtr[3];
            opArgs[0] = dictObj;                  // Dictionary ref -> object pointer
            opArgs[1] = (IntPtr)(&stepLocal);
            opArgs[2] = (IntPtr)(&partLocal);
            auraMonoRuntimeInvoke(drawOp, IntPtr.Zero, (IntPtr)opArgs, ref excOp);
            if (excOp != IntPtr.Zero)
            {
                status = "DrawingOperation threw";
                return false;
            }

            status = "DrawingOperation sent (" + runs.Count + " colors, " + totalRuns + " runs)";
            return true;
        }

        // Resolve the DrawingBoardComponent of the easel the player is focused on (drawing open).
        private bool TryGetFocusedBoardComponent(out IntPtr boardComp, out string status)
        {
            boardComp = IntPtr.Zero;
            IntPtr drawSystem = this.TryGetDrawSystemInstance(out status);
            if (drawSystem == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetDrawBoardNetId(drawSystem, out uint boardNetId) || boardNetId == 0U)
            {
                status = "no focused board (open a drawing at your easel)";
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(boardNetId, out IntPtr entity) || entity == IntPtr.Zero)
            {
                status = "board entity not found (netId=" + boardNetId + ")";
                return false;
            }

            IntPtr cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Drawing.DrawingBoardComponent");
            if (cls == IntPtr.Zero)
            {
                status = "DrawingBoardComponent class not found";
                return false;
            }

            if (!this.TryAuraMonoEntityGetComponent(entity, cls, out boardComp) || boardComp == IntPtr.Zero)
            {
                status = "board component not found";
                return false;
            }

            status = "ok";
            return true;
        }

        // Read the live canvas of the open board: CanvasPainter.pixelData (R8 byte[], bottom-up) + size.
        private unsafe bool TryReadOpenCanvasPixels(out byte[] indexBytes, out int width, out int height, out string status)
        {
            indexBytes = null;
            width = 0;
            height = 0;
            if (!this.TryGetFocusedBoardComponent(out IntPtr boardComp, out status))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(boardComp, out IntPtr painting, "get_CurrentPainting") || painting == IntPtr.Zero)
            {
                status = "no current painting (open a drawing for editing)";
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(painting, out IntPtr wb, "get_Width") || !this.TryUnboxMonoInt32(wb, out width)
                || !this.TryInvokeAuraMonoZeroArg(painting, out IntPtr hb, "get_Height") || !this.TryUnboxMonoInt32(hb, out height)
                || width <= 0 || height <= 0)
            {
                status = "canvas size read failed";
                return false;
            }

            IntPtr pclass = auraMonoObjectGetClass(painting);
            IntPtr field = pclass != IntPtr.Zero && auraMonoClassGetFieldFromName != null
                ? auraMonoClassGetFieldFromName(pclass, "pixelData")
                : IntPtr.Zero;
            if (field == IntPtr.Zero || auraMonoFieldGetValueObject == null || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
            {
                status = "pixelData field/array API unavailable";
                return false;
            }

            IntPtr arr = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, painting);
            if (arr == IntPtr.Zero)
            {
                status = "pixelData null";
                return false;
            }

            int len = (int)auraMonoArrayLength(arr);
            if (len != width * height)
            {
                status = "pixelData len " + len + " != " + (width * height);
                return false;
            }

            IntPtr addr = auraMonoArrayAddrWithSize(arr, 1, UIntPtr.Zero);
            if (addr == IntPtr.Zero)
            {
                status = "pixelData addr null";
                return false;
            }

            indexBytes = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(addr, indexBytes, 0, len);
            status = "ok";
            return true;
        }

        // Overwrite the live canvas (CanvasPainter.pixelData) with our index bytes and Apply(), so
        // the canvas texture shows our image. On close the game uploads this as the preview and
        // writes the local ScreenCapture cache (DrawingPanel OnPanelClose), fixing the stale thumbnail.
        private unsafe bool TryWriteOpenCanvasPixels(byte[] indexBytes, out string status)
        {
            status = "write canvas unavailable";
            if (indexBytes == null || indexBytes.Length == 0)
            {
                return false;
            }

            if (!this.TryGetFocusedBoardComponent(out IntPtr boardComp, out status))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(boardComp, out IntPtr painting, "get_CurrentPainting") || painting == IntPtr.Zero)
            {
                status = "no current painting";
                return false;
            }

            IntPtr pclass = auraMonoObjectGetClass(painting);
            IntPtr field = pclass != IntPtr.Zero && auraMonoClassGetFieldFromName != null
                ? auraMonoClassGetFieldFromName(pclass, "pixelData")
                : IntPtr.Zero;
            if (field == IntPtr.Zero || auraMonoFieldGetValueObject == null || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
            {
                status = "pixelData API unavailable";
                return false;
            }

            IntPtr arr = auraMonoFieldGetValueObject(this.auraMonoRootDomain, field, painting);
            if (arr == IntPtr.Zero)
            {
                status = "pixelData null";
                return false;
            }

            int len = (int)auraMonoArrayLength(arr);
            if (len != indexBytes.Length)
            {
                status = "pixelData len " + len + " != " + indexBytes.Length;
                return false;
            }

            IntPtr addr = auraMonoArrayAddrWithSize(arr, 1, UIntPtr.Zero);
            if (addr == IntPtr.Zero)
            {
                status = "pixelData addr null";
                return false;
            }

            System.Runtime.InteropServices.Marshal.Copy(indexBytes, 0, addr, len); // write our image into the canvas
            IntPtr apply = this.FindAuraMonoMethodOnHierarchy(pclass, "Apply", 0);
            if (apply != IntPtr.Zero && auraMonoRuntimeInvoke != null)
            {
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(apply, painting, IntPtr.Zero, ref exc);
            }

            status = "canvas updated";
            return true;
        }

        // Extract the drawing currently OPEN for editing straight from the live canvas, convert to a
        // colored PNG, and write ScreenCaptureDecrypted/drawing.png — one predictable file to edit.
        internal void DrawExtractOpenDrawing()
        {
            if (!this.TryReadOpenCanvasPixels(out byte[] idx, out int w, out int h, out string status))
            {
                ModLogger.Msg("[DrawUpload] extract: " + status);
                this.picturesLastStatus = "Draw extract: " + status;
                return;
            }

            string destRoot = this.GetScreenCaptureDecryptedRootPath(this.TryGetScreenCaptureRootPath());
            if (!DrawColorCodec.TryResolveLut(destRoot, out UnityEngine.Color32[] lut, out _) || lut == null || lut.Length == 0)
            {
                this.picturesLastStatus = "Draw extract: LUT unavailable";
                return;
            }

            byte[] colored = DrawColorCodec.IndexBytesToColoredPng(idx, w, h, lut);
            if (colored == null || colored.Length == 0)
            {
                this.picturesLastStatus = "Draw extract: convert failed";
                return;
            }

            try
            {
                Directory.CreateDirectory(destRoot);
                string outPath = Path.Combine(destRoot, DrawingWorkFileName);
                File.WriteAllBytes(outPath, colored);
                ModLogger.Msg("[DrawUpload] extracted live canvas " + w + "x" + h + " -> " + outPath);
                this.picturesLastStatus = "Extracted open drawing " + w + "x" + h + " -> " + DrawingWorkFileName + " (edit it, then Upload)";
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[DrawUpload] extract write failed: " + ex.Message);
                this.picturesLastStatus = "Draw extract: write failed";
            }
        }

        // Greedily pack the per-color run lists into sub-dictionaries whose TOTAL run count stays
        // <= maxRunsPerChunk. A single color's runs may span several chunks (its list is split);
        // run order within a color is preserved, empty lists are skipped and no individual run is
        // ever split. Runs paint absolute, non-overlapping pixel ranges, so any partition applies
        // to the same final image regardless of chunk boundaries.
        private static List<Dictionary<byte, List<(int Start, int Length)>>> BuildRunChunks(
            Dictionary<byte, List<(int Start, int Length)>> runs,
            int maxRunsPerChunk)
        {
            List<Dictionary<byte, List<(int Start, int Length)>>> chunks = new List<Dictionary<byte, List<(int Start, int Length)>>>();
            if (runs == null || runs.Count == 0)
            {
                return chunks;
            }

            if (maxRunsPerChunk < 1)
            {
                maxRunsPerChunk = 1;
            }

            Dictionary<byte, List<(int Start, int Length)>> current = null;
            int currentCount = 0;
            foreach (KeyValuePair<byte, List<(int Start, int Length)>> kv in runs)
            {
                List<(int Start, int Length)> source = kv.Value;
                if (source == null || source.Count == 0)
                {
                    continue;
                }

                int taken = 0;
                while (taken < source.Count)
                {
                    if (current == null || currentCount >= maxRunsPerChunk)
                    {
                        current = new Dictionary<byte, List<(int Start, int Length)>>();
                        chunks.Add(current);
                        currentCount = 0;
                    }

                    int take = Math.Min(source.Count - taken, maxRunsPerChunk - currentCount);
                    if (!current.TryGetValue(kv.Key, out List<(int Start, int Length)> slice))
                    {
                        slice = new List<(int Start, int Length)>(take);
                        current[kv.Key] = slice;
                    }

                    for (int i = 0; i < take; i++)
                    {
                        slice.Add(source[taken + i]);
                    }

                    taken += take;
                    currentCount += take;
                }
            }

            return chunks;
        }

        // CanvasPainter.DrawStep of the open canvas (public zero-arg getter) — the monotonic
        // draw-step counter the game's own delta-saves stamp into stepNum. Best-effort: callers
        // fall back to step base 1 when unavailable.
        private bool TryReadOpenCanvasDrawStep(out int drawStep)
        {
            drawStep = 0;
            if (!this.TryGetFocusedBoardComponent(out IntPtr boardComp, out _))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(boardComp, out IntPtr painting, "get_CurrentPainting") || painting == IntPtr.Zero)
            {
                return false;
            }

            return this.TryInvokeAuraMonoZeroArg(painting, out IntPtr boxed, "get_DrawStep")
                && this.TryUnboxMonoInt32(boxed, out drawStep);
        }

        // Dribble the chunks to the server over time with incrementing stepNum, mirroring the
        // game's own delta-save cadence. On any failure (or the drawing session going away) stop
        // early WITHOUT the cosmetic canvas write, so a partial send is visible as such.
        // GC-safety: no mono IntPtr is ever held across a yield — TrySendDrawingOperation /
        // IsDrawBoardSessionActive / TryWriteOpenCanvasPixels are each self-contained per call and
        // return before the next yield.
        private IEnumerator DrawUploadChunkedSendRoutine(
            List<Dictionary<byte, List<(int Start, int Length)>>> chunks,
            byte[] indexBytes,
            int stepBase,
            float delaySeconds)
        {
            // Step off the synchronous StartCoroutine prologue first: Unity runs a coroutine up to
            // its first yield inside Start itself, so an immediate failure would hit the finally
            // (nulling the handle) BEFORE the caller stores it — the dead handle would then wedge
            // the busy gate. Yielding once up front guarantees the handle is stored first.
            yield return null;

            try
            {
                int total = chunks.Count;
                for (int i = 0; i < total; i++)
                {
                    // Abort cleanly if the session vanished mid-send (panel closed, world change) —
                    // later chunks would only throw into a dead session.
                    if (!this.IsDrawBoardSessionActive())
                    {
                        ModLogger.Msg("[DrawUpload] chunked upload aborted at chunk " + (i + 1) + "/" + total + ": drawing session gone");
                        this.picturesLastStatus = "Draw upload: aborted at chunk " + (i + 1) + "/" + total + " (drawing session closed)";
                        yield break;
                    }

                    if (!this.TrySendDrawingOperation(chunks[i], stepBase + i, 0, out string st))
                    {
                        ModLogger.Msg("[DrawUpload] chunked upload failed at chunk " + (i + 1) + "/" + total + ": " + st);
                        this.picturesLastStatus = "Draw upload: chunk " + (i + 1) + "/" + total + " failed - " + st;
                        yield break;
                    }

                    this.picturesLastStatus = "Draw upload: chunk " + (i + 1) + "/" + total + " sent (step " + (stepBase + i) + ")";
                    yield return new UnityEngine.WaitForSeconds(delaySeconds);
                }

                // All chunks landed: update the live canvas ONCE so the game caches/previews our
                // image on close (fixes the stale thumbnail in the drawing list).
                this.TryWriteOpenCanvasPixels(indexBytes, out string canvasStatus);
                ModLogger.Msg("[DrawUpload] chunked upload done: " + total + " chunks | canvas: " + canvasStatus);
                this.picturesLastStatus = "Draw upload: " + total + " chunks sent | canvas: " + canvasStatus;
            }
            finally
            {
                this.drawUploadChunkCoroutine = null;
            }
        }

        // Upload drawing.png to the drawing currently OPEN for editing (DrawingOperation targets the
        // active session). Verifies it matches the open canvas size. Close without manual strokes,
        // then reopen to verify. Small canvases go out as the proven single op; anything above the
        // runs-per-chunk budget is split and dribbled by DrawUploadChunkedSendRoutine.
        internal void DrawUploadSendForOpenDrawing()
        {
            if (this.drawUploadChunkCoroutine != null)
            {
                this.picturesLastStatus = "Draw upload: chunked send already running";
                return;
            }

            if (!this.TryReadOpenCanvasPixels(out _, out int cw, out int ch, out string status))
            {
                ModLogger.Msg("[DrawUpload] upload: " + status);
                this.picturesLastStatus = "Draw upload: " + status;
                return;
            }

            string destRoot = this.GetScreenCaptureDecryptedRootPath(this.TryGetScreenCaptureRootPath());
            string workFile = Path.Combine(destRoot, DrawingWorkFileName);
            if (!File.Exists(workFile))
            {
                this.picturesLastStatus = "Draw upload: no " + DrawingWorkFileName + " — Extract first";
                return;
            }

            if (!DrawColorCodec.TryResolveLut(destRoot, out UnityEngine.Color32[] lut, out _) || lut == null || lut.Length == 0)
            {
                this.picturesLastStatus = "Draw upload: LUT unavailable";
                return;
            }

            byte[] idx = DrawColorCodec.ColoredPngToIndexBytes(File.ReadAllBytes(workFile), lut, out int fw, out int fh);
            if (idx == null)
            {
                this.picturesLastStatus = "Draw upload: decode failed";
                return;
            }

            if (fw != cw || fh != ch)
            {
                ModLogger.Msg("[DrawUpload] upload: drawing.png " + fw + "x" + fh + " != open canvas " + cw + "x" + ch);
                this.picturesLastStatus = "Draw upload: drawing.png size != open drawing — Extract the open one first";
                return;
            }

            Dictionary<byte, List<(int Start, int Length)>> runs = DrawColorCodec.BuildPixelRuns(idx, fw, fh);
            if (runs == null)
            {
                this.picturesLastStatus = "Draw upload: canvas too large (>65536 px)";
                return;
            }

            int totalRuns = 0;
            foreach (List<(int Start, int Length)> colorRuns in runs.Values)
            {
                totalRuns += colorRuns != null ? colorRuns.Count : 0;
            }

            int budget = UnityEngine.Mathf.Clamp(this.drawUploadRunsPerChunk, DrawUploadRunsPerChunkMin, DrawUploadRunsPerChunkMax);
            if (totalRuns <= budget)
            {
                // Fits in one command: keep the proven single-op path (one DrawingOperation, step 1).
                bool ok = this.TrySendDrawingOperation(runs, 1, 0, out string st);

                // Update the live canvas so the game caches/previews our image on close (fixes the
                // stale thumbnail in the drawing list).
                string canvasStatus = "skipped";
                if (ok)
                {
                    this.TryWriteOpenCanvasPixels(idx, out canvasStatus);
                }

                ModLogger.Msg("[DrawUpload] upload drawing.png " + fw + "x" + fh + " ok=" + ok + " " + st + " | canvas: " + canvasStatus);
                this.picturesLastStatus = "Draw upload: " + st + " | canvas: " + canvasStatus;
                return;
            }

            List<Dictionary<byte, List<(int Start, int Length)>>> chunks = BuildRunChunks(runs, budget);
            if (chunks.Count == 0)
            {
                this.picturesLastStatus = "Draw upload: no chunks to send";
                return;
            }

            // Continue the canvas's natural step sequence when the live DrawStep is readable;
            // otherwise start at 1 (both accepted by the server — steps only need to increment).
            int stepBase = this.TryReadOpenCanvasDrawStep(out int liveStep) && liveStep >= 0 ? liveStep + 1 : 1;
            float delay = UnityEngine.Mathf.Clamp(this.drawUploadChunkDelaySeconds, DrawUploadChunkDelayMin, DrawUploadChunkDelayMax);
            ModLogger.Msg("[DrawUpload] chunked upload start: drawing.png " + fw + "x" + fh + ", " + totalRuns
                + " runs -> " + chunks.Count + " chunks (budget " + budget + ", delay " + delay.ToString("0.00")
                + "s, stepBase " + stepBase + ")");
            this.picturesLastStatus = "Draw upload: sending " + chunks.Count + " chunks (" + totalRuns + " runs)...";
            this.drawUploadChunkCoroutine = ModCoroutines.Start(this.DrawUploadChunkedSendRoutine(chunks, idx, stepBase, delay));
            if (this.drawUploadChunkCoroutine == null)
            {
                this.picturesLastStatus = "Draw upload: could not start chunked send (coroutine host unavailable)";
            }
        }
    }
}
