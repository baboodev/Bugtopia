#if LOADER_BEPINEX
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.LowLevel;

namespace HeartopiaMod
{
    // STAGE 1 — MEASUREMENT ONLY. Proves whether a Unity player-loop node can call back into CoreCLR
    // at all. Nothing in the mod is driven from it: the injected MonoBehaviour still pumps everything,
    // so this cannot break the mod no matter how it turns out.
    //
    // WHY A SECOND ATTEMPT. The first one crashed the game (dump xdt.exe.25792, 0xC0000005 at
    // UnityPlayer.dll+0x7CC8A6) and the cause is now proven at instruction level, from the disassembly
    // at the faulting site:
    //     mov  rax,[rdi+58h]   ; rax = node->updateFunction   (our value)
    //     test rax,rax
    //     je   <managed updateDelegate path>
    //     mov  rcx,[rax]       ; *** DEREFERENCES IT ***
    //     test rcx,rcx
    //     je   <skip node>
    //     call rcx             ; fault
    // Register state confirmed it byte for byte: RAX held exactly the thunk address we had logged,
    // and RCX held the FIRST EIGHT BYTES OF OUR THUNK'S MACHINE CODE loaded as a pointer.
    //
    // ⇒ `updateFunction` is `void(**)()`, NOT `void(*)()`. It points at a writable slot that holds the
    // code pointer. Unity's own nodes prove the shape: every built-in `updateFunction` is an address
    // in UnityPlayer's `.data` (RW) and the value inside it is in `.text` (R-X) — one contiguous
    // 8-byte-stride dispatch table. A built-in node with `*slot == 0` exists, which is what the
    // `test rcx,rcx; je` is for: a null slot is the engine's own "subsystem disabled" state.
    //
    // Everything else in the first attempt was already correct and is kept: a null `subSystemList`,
    // `Il2CppSystem.*` as `type`, and `loopConditionFunction == 0` were all accepted — Unity even
    // allocated our node a profiler marker. `[UnmanagedCallersOnly]` was right too; the 8-byte spacing
    // between the two thunks was a red herring (CoreCLR FixupPrecode entries are exactly 8 bytes and
    // are packed into chunks — they are real, stable entry points).
    //
    // The callee is invoked with ZERO arguments (`call rcx` with no argument setup), which is exactly
    // what a zero-arg void thunk expects.
    //
    // ⚠️ WHAT IS STILL UNPROVEN — and the only thing this stage measures: whether a reverse P/Invoke
    // into CoreCLR is safe FROM THIS CALL SITE. Hence the thunk bodies below do nothing but bump a
    // counter: no logging, no Il2CppInterop, no allocation, nothing that could fault for any reason
    // other than the transition itself. If the counter advances, the transition is proven and the real
    // pump can be built on it. If it still crashes, the route is dead for good — and we learn that for
    // one run instead of guessing.
    internal static unsafe class PlayerLoopProbe
    {
        internal static long UpdateTicks;
        internal static long LateTicks;
        internal static string Status = "not installed";

        // STAGE 2 — the question that actually decides this route: are Il2CppInterop calls legal from
        // the player-loop call site? This is precisely what killed the embedded-Mono pump, where the
        // FIRST interop call faulted (`Time.get_time()` -> IL_STUB_PInvoke -> il2cpp_runtime_invoke,
        // dump coreclr_25768). So the probe makes that same call — `UnityEngine.Time.time` — and
        // nothing else. Still nothing is driven from it; the injected MonoBehaviour remains the pump.
        internal static long InteropOk;
        internal static long InteropFail;
        internal static string InteropError = string.Empty;
        private static bool _interopProbed;

        // STAGE 3 — drive the mod from here instead of from an injected MonoBehaviour.
        // Both prerequisites are now live-proven on this build:
        //   stage 1: ticks upd=529 late=529          -> reverse P/Invoke from the player loop works
        //   stage 2: IL2CPP INTEROP OK FROM PLAYER LOOP -> Time.time succeeded from the thunk, the very
        //            call that faulted from the Mono-side pump (coreclr_25768)
        // Note the payoff only lands together with `BepInEx.cfg [Logging] UnityLogListening = false`:
        // BepInEx installs the 5 ClassInjector detours itself, before plugins load, to redirect Unity
        // logs — so with logging on, dropping our injection changes the hook count by zero.
        internal static bool DriveMod = true;

        private static HeartopiaComplete _mod;
        private static volatile bool _shuttingDown;

        // Unmanaged cells holding the code pointers. Deliberately never freed: the engine keeps the
        // slot address inside its player loop for the life of the process.
        private static IntPtr _updateSlot;
        private static IntPtr _lateSlot;

        private const float ReportIntervalSeconds = 5f;
        private const int MaxReports = 12;

        // ---- the thunks: minimal by design (see header) -----------------------------------------

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void UpdateThunk()
        {
            long n = Interlocked.Increment(ref UpdateTicks);

            // Probe interop exactly once, and only after the counter path has already proven itself
            // for a few frames — so a fault here is unambiguously attributable to the interop call
            // rather than to the transition. try/catch because this returns into native engine code.
            if (n == 30L && !_interopProbed)
            {
                _interopProbed = true;
                try
                {
                    float t = UnityEngine.Time.time;
                    if (t >= 0f)
                    {
                        Interlocked.Increment(ref InteropOk);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref InteropFail);
                    try { InteropError = ex.GetType().Name + ": " + ex.Message; } catch { }
                }
            }

            if (!DriveMod || _shuttingDown)
            {
                return;
            }

            try
            {
                if (_mod == null)
                {
                    // Deferred to the first tick, not done in Load(): the mod is only constructed once
                    // we KNOW the pump is alive, so a dead pump can never leave a half-initialised mod.
                    _mod = new HeartopiaComplete();
                    _mod.OnInitializeMelon();
                    PlayerLoopPumpMarker.Clear();
                    ModLogger.Msg("[PlayerLoopProbe] first tick — mod now driven by the Unity player loop "
                                  + "(no MonoBehaviour injected by this mod).");
                }

                _mod.OnUpdate();
            }
            catch (Exception ex)
            {
                try { ModEntryGuard.Report("PlayerLoop.OnUpdate", ex); } catch { }
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void LateThunk()
        {
            Interlocked.Increment(ref LateTicks);

            if (!DriveMod || _shuttingDown || _mod == null)
            {
                return;
            }

            try
            {
                _mod.OnLateUpdate();
            }
            catch (Exception ex)
            {
                try { ModEntryGuard.Report("PlayerLoop.OnLateUpdate", ex); } catch { }
            }
        }

        // ---- install -----------------------------------------------------------------------------

        internal static bool Install()
        {
            try
            {
                // Pre-JIT so the very first engine call does not run the CLR prestub inside the
                // player-loop frame.
                PreJit("UpdateThunk");
                PreJit("LateThunk");

                _updateSlot = MakeSlot((IntPtr)(delegate* unmanaged[Cdecl]<void>)&UpdateThunk);
                _lateSlot = MakeSlot((IntPtr)(delegate* unmanaged[Cdecl]<void>)&LateThunk);

                PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
                Il2CppReferenceArray<PlayerLoopSystem> phases = root?.subSystemList;
                if (phases == null || phases.Length < 4)
                {
                    return Fail("player loop not ready (phases=" + (phases == null ? -1 : phases.Length) + ")");
                }

                // Distinct `type` per node so each gets its own profiler marker — makes the read-back
                // unambiguous. The type is only a label here, not behaviour.
                if (!InsertAfter(phases, "UnityEngine.PlayerLoop.Update", "ScriptRunBehaviourUpdate",
                                 _updateSlot, Il2CppType.Of<Il2CppSystem.Object>()))
                {
                    return false;
                }

                if (!InsertAfter(phases, "UnityEngine.PlayerLoop.PreLateUpdate", "ScriptRunBehaviourLateUpdate",
                                 _lateSlot, Il2CppType.Of<Il2CppSystem.ValueType>()))
                {
                    return false;
                }

                PlayerLoop.SetPlayerLoop(root);

                // Read back BOTH levels: the slot address must be in the node, and the code pointer
                // must still be inside the slot. Checking only the first is what let the previous
                // attempt report success while being fundamentally wrong.
                if (!NodeCarries(_updateSlot) || !NodeCarries(_lateSlot))
                {
                    return Fail("SetPlayerLoop did not retain our nodes");
                }

                AppDomain.CurrentDomain.ProcessExit += (_, __) => OnProcessExit();

                Status = "installed";
                ModLogger.Msg("[PlayerLoopProbe] installed — updSlot=0x" + _updateSlot.ToInt64().ToString("X")
                              + " *updSlot=0x" + (*(IntPtr*)_updateSlot).ToInt64().ToString("X")
                              + " lateSlot=0x" + _lateSlot.ToInt64().ToString("X")
                              + " *lateSlot=0x" + (*(IntPtr*)_lateSlot).ToInt64().ToString("X")
                              + " (both levels verified)");
                StartReporter();
                return true;
            }
            catch (Exception ex)
            {
                Status = "install threw: " + ex.GetType().Name + ": " + ex.Message;
                ModLogger.Msg("[PlayerLoopProbe] " + Status);
                return false;
            }
        }

        private static IntPtr MakeSlot(IntPtr codePtr)
        {
            IntPtr slot = Marshal.AllocHGlobal(IntPtr.Size);
            *(IntPtr*)slot = codePtr;
            return slot;
        }

        // Nulling the slot is the engine's OWN disable mechanism (`test rcx,rcx; je <skip>`), so this
        // stops the call before it can ever reach the CLR — strictly better than a managed flag,
        // which would still require entering managed code to be read.
        private static void Disable()
        {
            try
            {
                if (_updateSlot != IntPtr.Zero)
                {
                    *(IntPtr*)_updateSlot = IntPtr.Zero;
                }

                if (_lateSlot != IntPtr.Zero)
                {
                    *(IntPtr*)_lateSlot = IntPtr.Zero;
                }
            }
            catch
            {
            }
        }

        private static void PreJit(string methodName)
        {
            try
            {
                System.Reflection.MethodInfo m = typeof(PlayerLoopProbe).GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (m != null)
                {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                }
            }
            catch
            {
            }
        }

        private static bool InsertAfter(Il2CppReferenceArray<PlayerLoopSystem> phases,
                                        string phaseFullName, string anchorSimpleName,
                                        IntPtr slot, Il2CppSystem.Type nodeType)
        {
            for (int p = 0; p < phases.Length; p++)
            {
                // Indexing a value-type Il2CppReferenceArray yields a BOXED COPY — every edit must be
                // written back through phases[p] or it silently goes nowhere.
                PlayerLoopSystem phase = phases[p];
                Il2CppSystem.Type phaseType = phase?.type;
                if (phaseType == null || phaseType.FullName != phaseFullName)
                {
                    continue;
                }

                Il2CppReferenceArray<PlayerLoopSystem> kids = phase.subSystemList;
                if (kids == null || kids.Length == 0)
                {
                    return Fail(phaseFullName + " has no children");
                }

                int at = kids.Length;
                for (int k = 0; k < kids.Length; k++)
                {
                    Il2CppSystem.Type kidType = kids[k]?.type;
                    if (kidType != null && kidType.Name == anchorSimpleName)
                    {
                        at = k + 1;
                        break;
                    }
                }

                Il2CppReferenceArray<PlayerLoopSystem> grown =
                    new Il2CppReferenceArray<PlayerLoopSystem>(kids.Length + 1);
                for (int k = 0, w = 0; k < kids.Length; k++, w++)
                {
                    if (w == at)
                    {
                        w++;
                    }

                    grown[w] = kids[k];
                }

                PlayerLoopSystem node = new PlayerLoopSystem();
                node.type = nodeType;        // never null: the native side names a profiler marker from it
                node.updateFunction = slot;  // POINTER TO the code pointer (see header)
                                             // updateDelegate deliberately left null: assigning it goes
                                             // through DelegateSupport -> ClassInjector.
                grown[at] = node;

                phase.subSystemList = grown;
                phases[p] = phase;           // mandatory write-back
                return true;
            }

            return Fail("phase " + phaseFullName + " not found");
        }

        private static bool NodeCarries(IntPtr slot)
        {
            try
            {
                if (slot == IntPtr.Zero || *(IntPtr*)slot == IntPtr.Zero)
                {
                    return false;
                }

                PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
                Il2CppReferenceArray<PlayerLoopSystem> phases = root?.subSystemList;
                if (phases == null)
                {
                    return false;
                }

                for (int p = 0; p < phases.Length; p++)
                {
                    Il2CppReferenceArray<PlayerLoopSystem> kids = phases[p]?.subSystemList;
                    if (kids == null)
                    {
                        continue;
                    }

                    for (int k = 0; k < kids.Length; k++)
                    {
                        PlayerLoopSystem kid = kids[k];
                        if (kid != null && kid.updateFunction == slot)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // Reads counters from a background thread — needs no Unity API and no main thread, so it
        // cannot itself perturb what it is measuring.
        private static void StartReporter()
        {
            try
            {
                Thread t = new Thread(ReportLoop)
                {
                    IsBackground = true,
                    Name = "BugtopiaPlayerLoopProbe",
                };
                t.Start();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[PlayerLoopProbe] reporter start failed: " + ex.Message);
            }
        }

        private static void ReportLoop()
        {
            try
            {
                for (int i = 0; i < MaxReports; i++)
                {
                    Thread.Sleep((int)(ReportIntervalSeconds * 1000f));
                    long u = Interlocked.Read(ref UpdateTicks);
                    long l = Interlocked.Read(ref LateTicks);
                    long ok = Interlocked.Read(ref InteropOk);
                    long bad = Interlocked.Read(ref InteropFail);
                    string interop = ok > 0L
                        ? "  => IL2CPP INTEROP OK FROM PLAYER LOOP"
                        : (bad > 0L ? "  => INTEROP THREW: " + InteropError : "  interop=pending");

                    ModLogger.Msg("[PlayerLoopProbe] ticks upd=" + u + " late=" + l
                                  + (u > 0L ? "  reverse-pinvoke OK" : "  => still zero")
                                  + interop);

                    // Pump-dead verdict: node installed, engine never called it. Record it so the next
                    // launch injects instead of shipping another session with no mod at all.
                    if (DriveMod && u == 0L && i >= 2)
                    {
                        PlayerLoopPumpMarker.Write();
                        ModLogger.Msg("[PlayerLoopProbe] NO TICKS — recorded; the next launch will use the "
                                      + "injected MonoBehaviour pump. THIS session has no mod pump.");
                        return;
                    }

                    if (u > 0L && (ok > 0L || bad > 0L) && i >= 1)
                    {
                        return;   // both questions answered; stop spamming
                    }
                }
            }
            catch (Exception ex)
            {
                try { ModLogger.Msg("[PlayerLoopProbe] reporter failed: " + ex.Message); } catch { }
            }
        }

        private static bool Fail(string why)
        {
            Status = why;
            ModLogger.Msg("[PlayerLoopProbe] " + why);
            return false;
        }

        internal static void OnProcessExit()
        {
            _shuttingDown = true;
            Disable();
            try { _mod?.OnDeinitializeMelon(); }
            catch { }
        }
    }

    // Safety net for the pump switch. With injection removed there is nothing else to run the mod, so
    // a pump that installs but never ticks would leave it silently dead — forever, on every launch.
    // The reporter thread records that verdict on disk instead, and the next launch reads it and goes
    // straight back to injecting. Worst case is one degraded session, automatically, rather than a
    // permanently broken mod that looks like a code bug.
    internal static class PlayerLoopPumpMarker
    {
        private const string FileName = "playerloop_pump_dead.flag";

        private static string Path_
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Bugtopia");
                return System.IO.Path.Combine(dir, FileName);
            }
        }

        internal static bool Exists()
        {
            try { return System.IO.File.Exists(Path_); }
            catch { return false; }
        }

        internal static void Write()
        {
            try
            {
                string p = Path_;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                System.IO.File.WriteAllText(p, "PlayerLoop node installed but never ticked.\n");
            }
            catch
            {
            }
        }

        internal static void Clear()
        {
            try
            {
                string p = Path_;
                if (System.IO.File.Exists(p))
                {
                    System.IO.File.Delete(p);
                }
            }
            catch
            {
            }
        }
    }
}
#endif
