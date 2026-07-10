using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;

// Loader-neutral coroutine front. The active entry point installs its backend; loader
// assemblies are only referenced inside the adapter classes, so they are resolved lazily
// and only under the loader that actually hosts us.
public static class ModCoroutines
{
    private static Func<IEnumerator, object> _start;
    private static Action<object> _stop;

    public static void InitMelonLoader()
    {
        _start = MelonCoroutineAdapter.Start;
        _stop = MelonCoroutineAdapter.Stop;
    }

    public static void InitBepInEx()
    {
        _start = BepInExCoroutineHost.Start;
        _stop = BepInExCoroutineHost.Stop;
    }

    public static void SetHost(MonoBehaviour host) => BepInExCoroutineHost.SetHost(host);

    public static object Start(IEnumerator routine)
    {
        if (routine == null || _start == null)
        {
            return null;
        }

        return _start(routine);
    }

    public static void Stop(object coroutine)
    {
        if (coroutine == null)
        {
            return;
        }

        _stop?.Invoke(coroutine);
    }
}

internal static class MelonCoroutineAdapter
{
    public static object Start(IEnumerator routine) => MelonLoader.MelonCoroutines.Start(routine);

    public static void Stop(object token) => MelonLoader.MelonCoroutines.Stop(token);
}

internal static class BepInExCoroutineHost
{
    private static MonoBehaviour _host;

    // GC roots for in-flight coroutines. WrapToIl2Cpp() bridges our managed IEnumerator into an
    // Il2CppManagedEnumerator that Unity drives via a MoveNext trampoline every frame. il2cpp holds
    // that bridge, but the coreclr GC cannot see il2cpp's reference — so once Start() returns,
    // nothing on the managed side keeps the bridge (and the iterator it stores) alive. A GC
    // triggered by our own AuraMono allocations mid-coroutine then collects it, and the next
    // trampoline MoveNext dereferences a dead managed target → NULL read inside coreclr (recurring
    // crash coreclr.dll+0x1D1FDD). Rooting the bridge here for the coroutine's lifetime fixes it;
    // the bridge transitively keeps the wrapped iterator alive.
    private static readonly HashSet<object> _liveRoots = new HashSet<object>();
    private static readonly Dictionary<object, object> _wrapperByHandle = new Dictionary<object, object>();

    public static void SetHost(MonoBehaviour host) => _host = host;

    public static object Start(IEnumerator routine)
    {
        if (_host == null || routine == null)
        {
            return null;
        }

        // Outer iterator removes the roots in its finally when the coroutine ends on its own (most
        // do — they null their handle without calling Stop), preventing a slow leak. holder[0] =
        // the il2cpp bridge (rooted in _liveRoots), holder[1] = the Coroutine handle (added to
        // _wrapperByHandle, whose value also roots the bridge, so both must be cleared).
        object[] tokenHolder = new object[2];
        IEnumerator tracked = TrackRoutine(routine, tokenHolder);
        Il2CppSystem.Collections.IEnumerator wrapped = tracked.WrapToIl2Cpp();
        tokenHolder[0] = wrapped;
        _liveRoots.Add(wrapped);

        Coroutine handle = _host.StartCoroutine(wrapped);
        if (handle != null)
        {
            tokenHolder[1] = handle;
            _wrapperByHandle[handle] = wrapped;
        }
        return handle;
    }

    private static IEnumerator TrackRoutine(IEnumerator inner, object[] tokenHolder)
    {
        try
        {
            while (true)
            {
                // MoveNext is the il2cpp → coreclr boundary; an exception escaping it can corrupt
                // the trampoline, so swallow per-step faults instead of throwing across.
                bool moved;
                try
                {
                    moved = inner.MoveNext();
                }
                catch (System.Exception ex)
                {
                    ModEntryGuard.Report("Coroutine", ex);
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return inner.Current;
            }
        }
        finally
        {
            if (tokenHolder != null)
            {
                if (tokenHolder[0] != null)
                {
                    _liveRoots.Remove(tokenHolder[0]);
                }
                if (tokenHolder[1] != null)
                {
                    _wrapperByHandle.Remove(tokenHolder[1]);
                }
            }
        }
    }

    public static void Stop(object coroutine)
    {
        if (coroutine is Coroutine unityCoroutine)
        {
            _host?.StopCoroutine(unityCoroutine);
        }

        if (_wrapperByHandle.TryGetValue(coroutine, out object wrapper))
        {
            _liveRoots.Remove(wrapper);
            _wrapperByHandle.Remove(coroutine);
        }
    }
}
