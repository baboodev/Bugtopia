/*
 * bugtopia_inject.dll - starts BepInEx inside an already-running IL2CPP game.
 *
 * This is the small part of UnityDoorstop we actually need, reimplemented so its configuration can
 * come from somewhere other than an ini beside the game executable. Doorstop 4.5 on Windows reads
 * doorstop_config.ini and nothing else, which is exactly the file this design exists to avoid.
 *
 * What it does, in order:
 *   1. guards (no CLR already hosted, IL2CPP runtime up, an interop set present);
 *   2. hops to the Unity main thread by subclassing the game window - a window-proc pointer lives in
 *      the window struct, not in a module's .text, so nothing is patched in GameAssembly;
 *   3. hosts CoreCLR out of BepInEx's own dotnet\ folder via coreclr_initialize, exactly as doorstop
 *      does (there is no hostfxr in that folder);
 *   4. calls BepInEx.Unity.IL2CPP!Doorstop.Entrypoint.Start().
 *
 * Everything lands in bugtopia_inject.log next to this DLL: until step 4 succeeds there is no
 * BepInEx logger, no game console and no UI, so a silent failure would be undiagnosable.
 *
 * Design: docs/plans/2026-08-27-bepinex-injector.md section 4.3.
 * Build:  launcher/BugtopiaInject/build.bat
 */

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <share.h>

#define BUGTOPIA_WM_BOOTSTRAP (WM_APP + 0xBEE)
#define IL2CPP_WAIT_MS        60000
#define RENDEZVOUS_WAIT_MS    30000

/* ------------------------------------------------------------------ logging */

static wchar_t g_module_dir[MAX_PATH];
static wchar_t g_log_path[MAX_PATH];
static CRITICAL_SECTION g_log_lock;
static BOOL g_log_lock_ready;

/*
 * Two threads write here - the bootstrap thread and, after the rendezvous, the Unity main thread -
 * and the injector tails the file while they do. The first version opened with the CRT default
 * (deny-write) and gave up silently when that failed, which is how the very first line the main
 * thread logged went missing from an otherwise successful run. A diagnostic channel that drops
 * lines under contention is worse than useless, so: shared open, bounded retry, and our own writes
 * serialised.
 */
static void log_line(const wchar_t *fmt, ...)
{
    wchar_t message[2048];
    va_list args;
    va_start(args, fmt);
    _vsnwprintf_s(message, 2048, _TRUNCATE, fmt, args);
    va_end(args);

    SYSTEMTIME now;
    GetLocalTime(&now);

    if (g_log_lock_ready)
        EnterCriticalSection(&g_log_lock);

    FILE *file = NULL;
    for (int attempt = 0; attempt < 20 && !file; attempt++)
    {
        file = _wfsopen(g_log_path, L"a, ccs=UTF-8", _SH_DENYNO);
        if (!file)
            Sleep(5);
    }

    if (file)
    {
        fwprintf(file, L"[%02d:%02d:%02d.%03d] %s\n",
                 now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, message);
        fclose(file);
    }

    if (g_log_lock_ready)
        LeaveCriticalSection(&g_log_lock);

    /* Always, so a line is still recoverable under a debugger even if the file never opened. */
    OutputDebugStringW(message);
}

/* --------------------------------------------------------------- utilities */

static BOOL path_exists(const wchar_t *path)
{
    return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES;
}

/*
 * Self-test mode (BUGTOPIA_INJECT_SELFTEST=1): host CoreCLR and resolve Doorstop.Entrypoint.Start,
 * but stop short of calling it. That exercises the payload layout, the TPA list and the hosting
 * properties on any machine, with no game and nothing to clean up - the parts that are otherwise
 * only reachable through a live injection.
 */
static BOOL is_selftest(void)
{
    wchar_t value[8];
    return GetEnvironmentVariableW(L"BUGTOPIA_INJECT_SELFTEST", value, 8) > 0 && value[0] == L'1';
}

static void join_path(wchar_t *out, size_t cap, const wchar_t *base, const wchar_t *leaf)
{
    _snwprintf_s(out, cap, _TRUNCATE, L"%s\\%s", base, leaf);
}

/* A growable wide string, used to build the TPA list (a few hundred paths). */
typedef struct
{
    wchar_t *data;
    size_t   length;
    size_t   capacity;
} wbuf;

static BOOL wbuf_reserve(wbuf *b, size_t extra)
{
    if (b->length + extra + 1 <= b->capacity)
        return TRUE;

    size_t capacity = b->capacity ? b->capacity : 4096;
    while (capacity < b->length + extra + 1)
        capacity *= 2;

    wchar_t *grown = (wchar_t *)realloc(b->data, capacity * sizeof(wchar_t));
    if (!grown)
        return FALSE;

    b->data = grown;
    b->capacity = capacity;
    return TRUE;
}

static BOOL wbuf_append(wbuf *b, const wchar_t *text)
{
    size_t length = wcslen(text);
    if (!wbuf_reserve(b, length))
        return FALSE;
    memcpy(b->data + b->length, text, length * sizeof(wchar_t));
    b->length += length;
    b->data[b->length] = L'\0';
    return TRUE;
}

static void wbuf_free(wbuf *b)
{
    free(b->data);
    b->data = NULL;
    b->length = b->capacity = 0;
}

/* CoreCLR's hosting API is UTF-8 even on Windows. */
static char *to_utf8(const wchar_t *text)
{
    int size = WideCharToMultiByte(CP_UTF8, 0, text, -1, NULL, 0, NULL, NULL);
    if (size <= 0)
        return NULL;
    char *out = (char *)malloc((size_t)size);
    if (!out)
        return NULL;
    if (WideCharToMultiByte(CP_UTF8, 0, text, -1, out, size, NULL, NULL) <= 0)
    {
        free(out);
        return NULL;
    }
    return out;
}

/* Appends every *.dll in `directory` to `list`, separated by ';'. */
static BOOL append_assemblies(wbuf *list, const wchar_t *directory)
{
    wchar_t pattern[MAX_PATH];
    join_path(pattern, MAX_PATH, directory, L"*.dll");

    WIN32_FIND_DATAW found;
    HANDLE handle = FindFirstFileW(pattern, &found);
    if (handle == INVALID_HANDLE_VALUE)
    {
        log_line(L"no assemblies found in %s", directory);
        return FALSE;
    }

    int count = 0;
    do
    {
        if (found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            continue;

        wchar_t full[MAX_PATH];
        join_path(full, MAX_PATH, directory, found.cFileName);

        if (list->length && !wbuf_append(list, L";"))
            break;
        if (!wbuf_append(list, full))
            break;
        count++;
    } while (FindNextFileW(handle, &found));

    FindClose(handle);
    log_line(L"  %d assemblies from %s", count, directory);
    return count > 0;
}

/* ----------------------------------------------------------------- config */

typedef struct
{
    wchar_t storage[MAX_PATH];      /* holds BepInEx\ and dotnet\ */
    wchar_t game_exe[MAX_PATH];     /* this process's own image   */
    wchar_t game_dir[MAX_PATH];
    wchar_t core_dir[MAX_PATH];
    wchar_t runtime_dir[MAX_PATH];
    wchar_t coreclr[MAX_PATH];
    wchar_t preloader[MAX_PATH];
    wchar_t interop_dir[MAX_PATH];
} config;

/*
 * Storage comes from BUGTOPIA_STORAGE when the launcher created this process, and otherwise from
 * bugtopia_inject.cfg next to this DLL. The file path exists so the bootstrap can be driven against
 * a game that was started by hand - a process's environment cannot be changed from outside.
 */
static BOOL read_storage(wchar_t *out, size_t cap)
{
    if (GetEnvironmentVariableW(L"BUGTOPIA_STORAGE", out, (DWORD)cap) > 0)
    {
        log_line(L"storage from BUGTOPIA_STORAGE: %s", out);
        return TRUE;
    }

    wchar_t cfg_path[MAX_PATH];
    join_path(cfg_path, MAX_PATH, g_module_dir, L"bugtopia_inject.cfg");

    FILE *file = NULL;
    if (_wfopen_s(&file, cfg_path, L"r, ccs=UTF-8") != 0 || !file)
    {
        log_line(L"no BUGTOPIA_STORAGE and no %s", cfg_path);
        return FALSE;
    }

    wchar_t line[MAX_PATH * 2];
    BOOL found = FALSE;
    while (fgetws(line, (int)(sizeof(line) / sizeof(wchar_t)), file))
    {
        if (_wcsnicmp(line, L"storage=", 8) != 0)
            continue;

        wcsncpy_s(out, cap, line + 8, _TRUNCATE);
        size_t length = wcslen(out);
        while (length && (out[length - 1] == L'\n' || out[length - 1] == L'\r' || out[length - 1] == L' '))
            out[--length] = L'\0';
        found = length > 0;
        break;
    }
    fclose(file);

    if (found)
        log_line(L"storage from %s: %s", cfg_path, out);
    else
        log_line(L"%s has no 'storage=' line", cfg_path);
    return found;
}

static BOOL build_config(config *cfg)
{
    ZeroMemory(cfg, sizeof(*cfg));

    if (!read_storage(cfg->storage, MAX_PATH))
        return FALSE;

    if (GetModuleFileNameW(NULL, cfg->game_exe, MAX_PATH) == 0)
    {
        log_line(L"GetModuleFileNameW(NULL) failed: %lu", GetLastError());
        return FALSE;
    }
    wcscpy_s(cfg->game_dir, MAX_PATH, cfg->game_exe);
    wchar_t *slash = wcsrchr(cfg->game_dir, L'\\');
    if (slash)
        *slash = L'\0';

    wchar_t bepinex[MAX_PATH];
    join_path(bepinex, MAX_PATH, cfg->storage, L"BepInEx");
    join_path(cfg->core_dir, MAX_PATH, bepinex, L"core");
    join_path(cfg->interop_dir, MAX_PATH, bepinex, L"interop");
    join_path(cfg->runtime_dir, MAX_PATH, cfg->storage, L"dotnet");
    join_path(cfg->coreclr, MAX_PATH, cfg->runtime_dir, L"coreclr.dll");
    join_path(cfg->preloader, MAX_PATH, cfg->core_dir, L"BepInEx.Unity.IL2CPP.dll");

    log_line(L"game exe:  %s", cfg->game_exe);
    log_line(L"core:      %s", cfg->core_dir);
    log_line(L"runtime:   %s", cfg->runtime_dir);
    log_line(L"preloader: %s", cfg->preloader);
    return TRUE;
}

/* ----------------------------------------------------------------- guards */

typedef void *(*il2cpp_domain_get_fn)(void);

static BOOL wait_for_il2cpp(void)
{
    DWORD deadline = GetTickCount() + IL2CPP_WAIT_MS;
    for (;;)
    {
        HMODULE game_assembly = GetModuleHandleW(L"GameAssembly.dll");
        if (game_assembly)
        {
            il2cpp_domain_get_fn domain_get =
                (il2cpp_domain_get_fn)GetProcAddress(game_assembly, "il2cpp_domain_get");
            if (!domain_get)
            {
                log_line(L"GameAssembly.dll is loaded but exports no il2cpp_domain_get");
                return FALSE;
            }
            if (domain_get() != NULL)
                return TRUE;
        }

        if (GetTickCount() > deadline)
        {
            log_line(L"timed out waiting for the IL2CPP runtime (GameAssembly loaded: %s)",
                     game_assembly ? L"yes, domain still null" : L"no");
            return FALSE;
        }
        Sleep(50);
    }
}

static BOOL check_guards(const config *cfg)
{
    /* One CoreCLR per process. If doorstop is still installed it got here first. */
    if (GetModuleHandleW(L"coreclr.dll"))
    {
        log_line(L"ABORT: coreclr.dll is already loaded - a CLR is already hosted in this process. "
                 L"Is doorstop (winhttp.dll) still installed in the game folder?");
        return FALSE;
    }

    if (!path_exists(cfg->coreclr))
    {
        log_line(L"ABORT: missing %s", cfg->coreclr);
        return FALSE;
    }
    if (!path_exists(cfg->preloader))
    {
        log_line(L"ABORT: missing %s", cfg->preloader);
        return FALSE;
    }

    /*
     * Presence only. Whether the interop set is *current* is the launcher's call - it has the real
     * hash (MD5 over GameAssembly plus every unity-libs\*.dll) and refuses to inject when stale.
     * Generating here would freeze the game for minutes on the main thread.
     */
    wchar_t hash_file[MAX_PATH];
    join_path(hash_file, MAX_PATH, cfg->interop_dir, L"assembly-hash.txt");
    if (!path_exists(hash_file))
    {
        log_line(L"ABORT: no interop set at %s - run the generator first.", cfg->interop_dir);
        return FALSE;
    }

    if (is_selftest())
    {
        log_line(L"SELFTEST: skipping the IL2CPP runtime guard");
        return TRUE;
    }

    if (!wait_for_il2cpp())
        return FALSE;

    log_line(L"guards passed; IL2CPP runtime is up");
    return TRUE;
}

/* ------------------------------------------------------- the actual bootstrap */

typedef int(__stdcall *coreclr_initialize_fn)(const char *exePath,
                                              const char *appDomainFriendlyName,
                                              int propertyCount,
                                              const char **propertyKeys,
                                              const char **propertyValues,
                                              void **hostHandle,
                                              unsigned int *domainId);

typedef int(__stdcall *coreclr_create_delegate_fn)(void *hostHandle,
                                                   unsigned int domainId,
                                                   const char *entryPointAssemblyName,
                                                   const char *entryPointTypeName,
                                                   const char *entryPointMethodName,
                                                   void **delegate);

typedef void(__stdcall *entrypoint_fn)(void);

static BOOL start_bepinex(const config *cfg)
{
    /*
     * BepInEx's entry point takes its paths from the environment, and derives its root as the
     * grandparent of DOORSTOP_INVOKE_DLL_PATH. The other two doorstop variables are deliberately
     * left unset: null is a valid value for both and means "work it out from the exe".
     */
    SetEnvironmentVariableW(L"DOORSTOP_PROCESS_PATH", cfg->game_exe);
    SetEnvironmentVariableW(L"DOORSTOP_INVOKE_DLL_PATH", cfg->preloader);

    HMODULE clr = LoadLibraryW(cfg->coreclr);
    if (!clr)
    {
        log_line(L"LoadLibraryW(%s) failed: %lu", cfg->coreclr, GetLastError());
        return FALSE;
    }

    coreclr_initialize_fn initialize =
        (coreclr_initialize_fn)GetProcAddress(clr, "coreclr_initialize");
    coreclr_create_delegate_fn create_delegate =
        (coreclr_create_delegate_fn)GetProcAddress(clr, "coreclr_create_delegate");
    if (!initialize || !create_delegate)
    {
        log_line(L"coreclr.dll is missing the hosting exports");
        return FALSE;
    }

    /*
     * TPA covers dotnet\ and BepInEx\core\ both. Doorstop only adds the target assembly and lets
     * BepInEx's AppDomain.AssemblyResolve find the rest, but that handler is installed by code that
     * has to load first - listing core\ up front costs nothing and removes the ordering question.
     */
    wbuf tpa = {0};
    log_line(L"building TPA list");
    BOOL have_runtime = append_assemblies(&tpa, cfg->runtime_dir);
    BOOL have_core = append_assemblies(&tpa, cfg->core_dir);
    if (!have_runtime || !have_core)
    {
        log_line(L"ABORT: could not enumerate the payload assemblies");
        wbuf_free(&tpa);
        return FALSE;
    }

    wbuf native = {0};
    wbuf_append(&native, cfg->runtime_dir);
    wbuf_append(&native, L";");
    wbuf_append(&native, cfg->game_dir);

    char *tpa_utf8 = to_utf8(tpa.data);
    char *app_paths_utf8 = to_utf8(cfg->core_dir);
    char *native_utf8 = to_utf8(native.data);
    char *exe_utf8 = to_utf8(cfg->game_exe);
    wbuf_free(&tpa);
    wbuf_free(&native);

    BOOL ok = FALSE;
    void *host = NULL;
    unsigned int domain = 0;

    if (tpa_utf8 && app_paths_utf8 && native_utf8 && exe_utf8)
    {
        const char *keys[] = {
            "TRUSTED_PLATFORM_ASSEMBLIES",
            "APP_PATHS",
            "NATIVE_DLL_SEARCH_DIRECTORIES",
        };
        const char *values[] = { tpa_utf8, app_paths_utf8, native_utf8 };

        int hr = initialize(exe_utf8, "bugtopia", 3, keys, values, &host, &domain);
        if (hr < 0)
        {
            log_line(L"coreclr_initialize failed: 0x%08X", (unsigned)hr);
        }
        else
        {
            entrypoint_fn start = NULL;
            hr = create_delegate(host, domain, "BepInEx.Unity.IL2CPP",
                                 "Doorstop.Entrypoint", "Start", (void **)&start);
            if (hr < 0 || !start)
            {
                log_line(L"coreclr_create_delegate(Doorstop.Entrypoint.Start) failed: 0x%08X",
                         (unsigned)hr);
            }
            else if (is_selftest())
            {
                log_line(L"SELFTEST: Doorstop.Entrypoint.Start resolved at %p - stopping here",
                         (void *)start);
                ok = TRUE;
            }
            else
            {
                log_line(L"calling Doorstop.Entrypoint.Start()");
                start();
                log_line(L"Start() returned - BepInEx preloader has run");
                ok = TRUE;
            }
        }
    }
    else
    {
        log_line(L"out of memory building the hosting properties");
    }

    free(tpa_utf8);
    free(app_paths_utf8);
    free(native_utf8);
    free(exe_utf8);
    return ok;
}

/* ------------------------------------------------- main-thread rendezvous */

static config     g_config;
static WNDPROC    g_original_wndproc;
static HWND       g_window;
static volatile LONG g_bootstrap_done;

static LRESULT CALLBACK bugtopia_wndproc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    if (message == BUGTOPIA_WM_BOOTSTRAP)
    {
        /* Unsubclass first: whatever happens next, the window must be left as we found it. */
        SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)g_original_wndproc);
        log_line(L"on the Unity main thread (thread %lu)", GetCurrentThreadId());

        BOOL ok = start_bepinex(&g_config);
        log_line(ok ? L"bootstrap complete" : L"bootstrap FAILED");
        InterlockedExchange(&g_bootstrap_done, 1);
        return 0;
    }
    return CallWindowProcW(g_original_wndproc, window, message, wparam, lparam);
}

static BOOL CALLBACK find_window(HWND window, LPARAM param)
{
    DWORD pid = 0;
    GetWindowThreadProcessId(window, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(window))
        return TRUE;

    wchar_t class_name[128] = {0};
    GetClassNameW(window, class_name, 128);

    /* Unity's own window is the one whose thread runs the player loop. */
    if (_wcsicmp(class_name, L"UnityWndClass") == 0)
    {
        *(HWND *)param = window;
        return FALSE;
    }

    if (*(HWND *)param == NULL)
    {
        log_line(L"candidate window class '%s' (kept as fallback)", class_name);
        *(HWND *)param = window;
    }
    return TRUE;
}

static BOOL rendezvous(void)
{
    DWORD deadline = GetTickCount() + RENDEZVOUS_WAIT_MS;
    HWND window = NULL;

    while (!window)
    {
        EnumWindows(find_window, (LPARAM)&window);
        if (window)
            break;
        if (GetTickCount() > deadline)
        {
            log_line(L"ABORT: no game window appeared within %d ms", RENDEZVOUS_WAIT_MS);
            return FALSE;
        }
        Sleep(100);
    }

    g_window = window;
    log_line(L"game window %p, owned by thread %lu", (void *)window,
             GetWindowThreadProcessId(window, NULL));

    g_original_wndproc = (WNDPROC)SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)bugtopia_wndproc);
    if (!g_original_wndproc)
    {
        log_line(L"ABORT: SetWindowLongPtrW failed: %lu", GetLastError());
        return FALSE;
    }

    if (!PostMessageW(window, BUGTOPIA_WM_BOOTSTRAP, 0, 0))
    {
        log_line(L"ABORT: PostMessageW failed: %lu", GetLastError());
        SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)g_original_wndproc);
        return FALSE;
    }

    log_line(L"waiting for the main thread to pump our message");
    deadline = GetTickCount() + RENDEZVOUS_WAIT_MS;
    while (!InterlockedCompareExchange(&g_bootstrap_done, 0, 0))
    {
        if (GetTickCount() > deadline)
        {
            log_line(L"WARNING: the main thread has not pumped our message yet; leaving it queued");
            return FALSE;
        }
        Sleep(50);
    }
    return TRUE;
}

/* ------------------------------------------------------------------ entry */

static DWORD WINAPI bootstrap_thread(LPVOID unused)
{
    (void)unused;

    log_line(L"---- bugtopia_inject attached to pid %lu ----", GetCurrentProcessId());

    if (!build_config(&g_config))
        return 1;
    if (!check_guards(&g_config))
        return 1;

    /* Self-test never touches a window: there is no game and no main thread to hop onto. */
    if (is_selftest())
        return start_bepinex(&g_config) ? 0 : 1;

    if (!rendezvous())
        return 1;

    return 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;

    if (reason != DLL_PROCESS_ATTACH)
        return TRUE;

    DisableThreadLibraryCalls(instance);

    InitializeCriticalSection(&g_log_lock);
    g_log_lock_ready = TRUE;

    GetModuleFileNameW(instance, g_module_dir, MAX_PATH);
    wchar_t *slash = wcsrchr(g_module_dir, L'\\');
    if (slash)
        *slash = L'\0';
    join_path(g_log_path, MAX_PATH, g_module_dir, L"bugtopia_inject.log");

    /* Nothing heavy under the loader lock - the real work runs on its own thread. */
    HANDLE thread = CreateThread(NULL, 0, bootstrap_thread, NULL, 0, NULL);
    if (thread)
        CloseHandle(thread);

    return TRUE;
}
