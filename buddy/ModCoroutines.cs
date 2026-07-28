using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Fully managed coroutine scheduler — no il2cpp bridge, no Unity coroutine host.
//
// WHY IT WAS REPLACED: the old BepInEx backend ran every routine through `WrapToIl2Cpp()`, i.e.
// `Il2CppManagedEnumerator`, and the MelonLoader backend through ML's `MonoEnumeratorWrapper`. Both
// are managed types INJECTED into the il2cpp domain, so either one drags in
// `ClassInjector.RegisterTypeInIl2Cpp` -> `InjectorHelpers.Setup()` -> five inline detours inside
// GameAssembly.dll's `.text`. That injection is the entire remaining reason the mod touches the
// module the anti-cheat integrity model hashes.
//
// It also deleted a whole class of crash: with the bridge, il2cpp held the only reference to the
// wrapper while the coreclr GC could not see it, so a GC triggered mid-coroutine collected the
// bridge and the next trampoline `MoveNext` dereferenced a dead target (coreclr.dll+0x1D1FDD). The
// old code fought that with an explicit `_liveRoots` root set. Here the iterators are ordinary
// managed objects held by an ordinary managed list, so the GC simply sees them — the hazard cannot
// exist and the root-set bookkeeping is gone.
//
// SEMANTICS: routines are stepped once per frame from `ModCoroutines.Tick()` (HeartopiaComplete's
// OnUpdate), matching Unity's "resume after Update" behaviour for `yield return null`. Supported
// yields — the complete set this mod actually uses:
//   * `null`                       -> resume next frame
//   * `ModWait.Seconds(t)`         -> scaled time   (Unity's WaitForSeconds)
//   * `ModWait.Realtime(t)`        -> unscaled time (Unity's WaitForSecondsRealtime)
//   * a nested `IEnumerator`       -> pushed on this routine's stack and driven to completion first
// Anything else is treated as a single-frame wait and logged ONCE — see ModWait for why yielding a
// Unity YieldInstruction is no longer allowed.
public static class ModCoroutines
{
    private sealed class Routine
    {
        public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        public float ResumeScaledAt = float.NegativeInfinity;
        public float ResumeUnscaledAt = float.NegativeInfinity;
        public bool Dead;
    }

    private static readonly List<Routine> _routines = new List<Routine>();
    private static readonly List<Routine> _pending = new List<Routine>();
    private static bool _ticking;
    private static bool _warnedUnknownYield;

    // Kept so the existing per-loader entry points compile and keep their call order; the scheduler
    // itself is loader-agnostic, so these are now no-ops.
    public static void InitMelonLoader()
    {
    }

    public static void InitBepInEx()
    {
    }

    public static void SetHost(MonoBehaviour host)
    {
    }

    public static object Start(IEnumerator routine)
    {
        if (routine == null)
        {
            return null;
        }

        Routine r = new Routine();
        r.Stack.Push(routine);

        // Starting a coroutine from inside a coroutine is common; never mutate the list mid-tick.
        if (_ticking)
        {
            _pending.Add(r);
        }
        else
        {
            _routines.Add(r);
        }

        return r;
    }

    public static void Stop(object coroutine)
    {
        if (coroutine is Routine r)
        {
            r.Dead = true;
        }
    }

    public static void StopAll()
    {
        for (int i = 0; i < _routines.Count; i++)
        {
            _routines[i].Dead = true;
        }

        _pending.Clear();
    }

    internal static int ActiveCount => _routines.Count;

    // One step per routine per frame.
    public static void Tick()
    {
        if (_routines.Count == 0 && _pending.Count == 0)
        {
            return;
        }

        float scaled = Time.time;
        float unscaled = Time.unscaledTime;

        _ticking = true;
        try
        {
            for (int i = 0; i < _routines.Count; i++)
            {
                Routine r = _routines[i];
                if (r.Dead)
                {
                    continue;
                }

                if (scaled < r.ResumeScaledAt || unscaled < r.ResumeUnscaledAt)
                {
                    continue;
                }

                StepRoutine(r, scaled, unscaled);
            }
        }
        finally
        {
            _ticking = false;
        }

        if (_pending.Count > 0)
        {
            _routines.AddRange(_pending);
            _pending.Clear();
        }

        // Compact only when there is something to drop, so the steady state costs one count check.
        for (int i = _routines.Count - 1; i >= 0; i--)
        {
            if (_routines[i].Dead || _routines[i].Stack.Count == 0)
            {
                _routines.RemoveAt(i);
            }
        }
    }

    private static void StepRoutine(Routine r, float scaled, float unscaled)
    {
        while (true)
        {
            if (r.Stack.Count == 0)
            {
                r.Dead = true;
                return;
            }

            IEnumerator current = r.Stack.Peek();
            bool moved;
            try
            {
                moved = current.MoveNext();
            }
            catch (Exception ex)
            {
                // One faulting routine must never take the scheduler (or the other routines) with it.
                ModEntryGuard.Report("Coroutine", ex);
                r.Dead = true;
                return;
            }

            if (!moved)
            {
                r.Stack.Pop();
                if (r.Stack.Count == 0)
                {
                    r.Dead = true;
                    return;
                }

                // The parent resumes on the NEXT frame, matching Unity: finishing a nested routine
                // does not give the parent a free extra step this frame.
                return;
            }

            object yielded = null;
            try
            {
                yielded = current.Current;
            }
            catch (Exception ex)
            {
                ModEntryGuard.Report("Coroutine.Current", ex);
                r.Dead = true;
                return;
            }

            if (yielded == null)
            {
                return; // next frame
            }

            if (yielded is ModWait wait)
            {
                if (wait.UseUnscaled)
                {
                    r.ResumeUnscaledAt = unscaled + wait.Duration;
                }
                else
                {
                    r.ResumeScaledAt = scaled + wait.Duration;
                }

                return;
            }

            if (yielded is IEnumerator nested)
            {
                // Drive the nested routine first; loop so it gets its own first step this frame,
                // exactly like Unity.
                r.Stack.Push(nested);
                continue;
            }

            if (!_warnedUnknownYield)
            {
                _warnedUnknownYield = true;
                ModLogger.Msg("[ModCoroutines] unsupported yield '" + yielded.GetType().Name
                              + "' treated as one frame — use ModWait.Seconds/Realtime instead of a Unity YieldInstruction.");
            }

            return; // next frame
        }
    }
}

// Wait tokens for the managed scheduler.
//
// These deliberately replace `new WaitForSeconds(t)` / `new WaitForSecondsRealtime(t)`. Those are
// il2cpp objects: reading their duration back would mean reaching into game-side fields
// (`m_Seconds`, `<waitTime>k__BackingField`) through interop, which couples the scheduler to the
// game's type layout for no benefit. A plain managed token keeps the whole coroutine path inside
// coreclr, which is the entire point of the rewrite.
public sealed class ModWait
{
    public readonly float Duration;
    public readonly bool UseUnscaled;

    private ModWait(float seconds, bool useUnscaled)
    {
        this.Duration = seconds < 0f ? 0f : seconds;
        this.UseUnscaled = useUnscaled;
    }

    /// Scaled time — the equivalent of Unity's WaitForSeconds (affected by Time.timeScale).
    public static ModWait Seconds(float seconds) => new ModWait(seconds, false);

    /// Unscaled time — the equivalent of Unity's WaitForSecondsRealtime.
    public static ModWait Realtime(float seconds) => new ModWait(seconds, true);
}
