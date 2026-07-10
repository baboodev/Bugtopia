// Which loader bootstrapped this DLL. Set by the entry point that actually ran
// (HeartopiaMelonPlugin.OnInitializeMelon / HeartopiaBepInPlugin.Load) before any
// feature code executes; features branch on this at runtime instead of compile-time #if,
// so a single binary serves both loaders.
public static class ModLoaderInfo
{
    public static bool IsMelonLoader;
}
