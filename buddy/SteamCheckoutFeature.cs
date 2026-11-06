using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HeartopiaMod
{
    // Steam launch opens the store through XDSDK's Steam MicroTxn overlay. The same exe started by
    // hand already opens the in-game web checkout, because the process is missing the `from=steam`
    // argument and XDGCommon.GetPackageType() is not "PC-Steam".
    //
    // This toggle does not change the package type (that also hides the Steam sign-in button). It
    // detours the one Mono method every Windows purchase goes through, PaymentModule.WindowsPay,
    // and calls XDSDKManager.PayWithWeb with the PayInfo the server already built. The web callback
    // is the game's own OnWindowsPaymentCompleted, which the shop left unused. This mono does not
    // export mono_delegate_ctor, so the Action<int, string> is built with
    // Delegate.CreateDelegate(Type, module, MethodInfo) and left on the stock method. That method
    // is not detoured: the 08:49 crash was 11s after PayWithWeb had already returned, and the
    // callback detour was the only mod code that still ran after that. Its native ABI was never
    // verified. The merge-block flag WindowsPay would set is left unset, because the stock web
    // callback does not clear it.
    //
    // The detour is installed only while the toggle is on AND this process was launched from Steam.
    // Once installed it stays (detours are not torn down); the body calls the original when the
    // toggle is off. Manual launches never install it.
    public partial class HeartopiaComplete
    {
        private const string SteamCheckoutCallbackName = "SteamDirectCheckout";

        public bool privacySteamDirectCheckout;

        private static bool? steamCheckoutLaunchedFromSteam;
        private static bool steamCheckoutNotSteamLogged;
        private static bool steamCheckoutClassMissLogged;
        private static bool steamCheckoutHookFailed;
        private static bool steamCheckoutOffsetsReady;
        private static string steamCheckoutResolveMiss;
        private static int steamCheckoutOffOrderId = -1;
        private static int steamCheckoutOffProductId = -1;
        private static int steamCheckoutOffProductName = -1;
        private static int steamCheckoutOffAmount = -1;
        private static int steamCheckoutOffRoleId = -1;
        private static int steamCheckoutOffServerId = -1;
        private static int steamCheckoutOffExt = -1;

        private static NativeDetour steamCheckoutWindowsPayDetour;
        private static SteamCheckoutWindowsPayDelegate steamCheckoutWindowsPayKeepAlive;
        private static SteamCheckoutWindowsPayDelegate steamCheckoutWindowsPayTrampoline;
        private static IntPtr steamCheckoutCreateDelegateMethod;
        private static IntPtr steamCheckoutCompletedMethod;
        private static IntPtr steamCheckoutActionClass;
        private static uint steamCheckoutActionTypePin;
        private static uint steamCheckoutMethodInfoPin;
        private static IntPtr steamCheckoutPayWithWebMethod;
        private static IntPtr steamCheckoutSdkClass;
        private static uint steamCheckoutCallbackPin;
        private static long steamCheckoutBoundModule;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SteamCheckoutWindowsPayDelegate(IntPtr self, IntPtr payInfo);

        // mono_method_get_object(domain, method, refclass) -> System.Reflection.MethodInfo.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SteamCheckoutMethodGetObject(IntPtr domain, IntPtr method, IntPtr refClass);

        // The shared auraMonoCompileMethod binding is typed void. This one returns the native entry.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SteamCheckoutCompile(IntPtr method);

        // SteamManager.InitXdSteam decides "started from Steam" from the Win32 command line.
        // The 07:51 session logged `xdt.exe from=steam` and still never installed this hook:
        // an exact Environment.GetCommandLineArgs() token compare does not see that argument.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetCommandLineW();

        private static bool SteamCheckoutLaunchedFromSteam()
        {
            if (steamCheckoutLaunchedFromSteam.HasValue)
            {
                return steamCheckoutLaunchedFromSteam.Value;
            }

            bool found = false;
            try
            {
                string native = Marshal.PtrToStringUni(GetCommandLineW());
                if (!string.IsNullOrEmpty(native)
                    && native.IndexOf("from=steam", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                }
            }
            catch
            {
                found = false;
            }

            if (!found)
            {
                try
                {
                    string[] args = Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] != null
                            && args[i].IndexOf("from=steam", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (!found)
            {
                string clientLaunch = Environment.GetEnvironmentVariable("SteamClientLaunch");
                string steamEnv = Environment.GetEnvironmentVariable("SteamEnv");
                found = clientLaunch == "1" || steamEnv == "1";
            }

            steamCheckoutLaunchedFromSteam = found;
            return found;
        }

        private string GetSteamDirectCheckoutStatus()
        {
            if (!SteamCheckoutLaunchedFromSteam())
            {
                return this.L("Not a Steam launch - checkout is already direct.");
            }

            if (!this.privacySteamDirectCheckout)
            {
                return this.L("Steam launch - direct checkout off.");
            }

            if (steamCheckoutHookFailed)
            {
                return this.L("Steam launch - direct checkout failed to install.");
            }

            if (steamCheckoutWindowsPayTrampoline == null)
            {
                return this.L("Steam launch - direct checkout waiting.");
            }

            return this.L("Steam launch - direct checkout on.");
        }

        private void RegisterSteamDirectCheckout()
        {
            this.RegisterWorldReadyCallback(SteamCheckoutCallbackName, this.TryInstallSteamDirectCheckout);
        }

        // True = done for this world. False = the shop image is not loaded yet, try again.
        private bool TryInstallSteamDirectCheckout()
        {
            if (steamCheckoutWindowsPayTrampoline != null || steamCheckoutHookFailed)
            {
                return true;
            }

            if (!this.privacySteamDirectCheckout || !SteamCheckoutLaunchedFromSteam())
            {
                if (this.privacySteamDirectCheckout && !steamCheckoutNotSteamLogged)
                {
                    steamCheckoutNotSteamLogged = true;
                    ModLogger.Msg("[SteamCheckout] toggle on, process was not launched from Steam — hook left off.");
                }

                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                // The namespace says XDTLevelAndEntity, but this module compiles into the XDTGameUI
                // image. FindAuraMonoClassByFullName never looks there for that prefix, so the
                // install retried until the world-ready gate gave up and WindowsPay stayed stock.
                IntPtr paymentClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Game.Module.Payment",
                    "PaymentModule",
                    new[] { "XDTGameUI", "XDTGameUI.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
                if (paymentClass == IntPtr.Zero)
                {
                    if (!steamCheckoutClassMissLogged)
                    {
                        steamCheckoutClassMissLogged = true;
                        ModLogger.Msg("[SteamCheckout] PaymentModule is not loaded yet.");
                    }

                    return false;
                }

                IntPtr windowsPay = this.FindAuraMonoMethodOnHierarchy(paymentClass, "WindowsPay", 1);
                IntPtr completed = this.FindAuraMonoMethodOnHierarchy(paymentClass, "OnWindowsPaymentCompleted", 2);
                if (windowsPay == IntPtr.Zero || completed == IntPtr.Zero
                    || !AuraMonoMethodParamCountIs(windowsPay, 1)
                    || !AuraMonoMethodParamCountIs(completed, 2))
                {
                    steamCheckoutHookFailed = true;
                    ModLogger.Msg("[SteamCheckout] PaymentModule pay methods missing — left on Steam checkout.");
                    return true;
                }

                // A miss here used to latch hookFailed and return true, so the gate never
                // retried. The 08:18 session found PaymentModule, failed this step once, and
                // left WindowsPay on the Steam overlay for the rest of the process.
                if (!this.TryResolveSteamCheckoutPayTargets(out string miss))
                {
                    if (!string.Equals(steamCheckoutResolveMiss, miss, StringComparison.Ordinal))
                    {
                        steamCheckoutResolveMiss = miss;
                        ModLogger.Msg("[SteamCheckout] " + miss);
                    }

                    return false;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                steamCheckoutCompletedMethod = completed;
                // 08:32 session: PayWithWeb resolved, then this bailed. mono_delegate_ctor is not
                // in the export table of mono-2.0-sgen.dll. CreateDelegate is.
                if (!this.TryBindSteamCheckoutCallback(monoModule, out string bindMiss))
                {
                    steamCheckoutHookFailed = true;
                    ModLogger.Msg("[SteamCheckout] " + bindMiss);
                    return true;
                }

                SteamCheckoutCompile compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<SteamCheckoutCompile>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    steamCheckoutHookFailed = true;
                    ModLogger.Msg("[SteamCheckout] mono_compile_method unavailable — left on Steam checkout.");
                    return true;
                }

                IntPtr windowsNative = compile(windowsPay);
                if (windowsNative == IntPtr.Zero)
                {
                    return false;
                }

                steamCheckoutWindowsPayKeepAlive = SteamCheckoutWindowsPayDetour;
                steamCheckoutWindowsPayDetour = new NativeDetour(windowsNative, steamCheckoutWindowsPayKeepAlive);
                steamCheckoutWindowsPayTrampoline = steamCheckoutWindowsPayDetour.GenerateTrampoline<SteamCheckoutWindowsPayDelegate>();
                if (steamCheckoutWindowsPayTrampoline == null)
                {
                    this.RevertSteamCheckoutDetours();
                    steamCheckoutHookFailed = true;
                    ModLogger.Msg("[SteamCheckout] trampoline unavailable — detour reverted.");
                    return true;
                }

                ModLogger.Msg("[SteamCheckout] hooked PaymentModule.WindowsPay -> PayWithWeb.");
                return true;
            }
            catch (Exception ex)
            {
                this.RevertSteamCheckoutDetours();
                steamCheckoutHookFailed = true;
                ModLogger.Msg("[SteamCheckout] install failed: " + ex.Message);
                return true;
            }
        }

        private void RevertSteamCheckoutDetours()
        {
            try { steamCheckoutWindowsPayDetour?.Undo(); } catch { }
            steamCheckoutWindowsPayDetour = null;
            steamCheckoutWindowsPayKeepAlive = null;
            steamCheckoutWindowsPayTrampoline = null;
        }

        private bool TryResolveSteamCheckoutPayTargets(out string miss)
        {
            miss = null;
            // mono_image_loaded("EngineWrapper") does not see this image. The live runtime
            // resolves the same name only by walking every loaded assembly.
            if (steamCheckoutSdkClass == IntPtr.Zero)
            {
                steamCheckoutSdkClass = this.FindAuraMonoClassAnySpelling(
                    "Client.SDKManager.XDSDKManager.XDSDKManager");
            }

            if (steamCheckoutSdkClass == IntPtr.Zero)
            {
                miss = "XDSDKManager is not loaded yet.";
                return false;
            }

            if (steamCheckoutPayWithWebMethod == IntPtr.Zero && auraMonoClassGetMethodFromName != null)
            {
                // Do not use the hierarchy helper: it caches a miss for the rest of the process.
                IntPtr method = auraMonoClassGetMethodFromName(steamCheckoutSdkClass, "PayWithWeb", 8);
                if (method != IntPtr.Zero && AuraMonoMethodParamCountIs(method, 8))
                {
                    steamCheckoutPayWithWebMethod = method;
                }
            }

            if (steamCheckoutPayWithWebMethod == IntPtr.Zero)
            {
                miss = "PayWithWeb(8) was not found on XDSDKManager.";
                return false;
            }

            if (steamCheckoutActionClass == IntPtr.Zero)
            {
                IntPtr actionOpen = this.FindAuraMonoClassAnySpelling("System.Action`2");
                IntPtr intClass = this.FindAuraMonoClassAnySpelling("System.Int32");
                IntPtr stringClass = this.FindAuraMonoClassAnySpelling("System.String");
                if (actionOpen == IntPtr.Zero || intClass == IntPtr.Zero || stringClass == IntPtr.Zero)
                {
                    miss = "System.Action`2, Int32 or String was not found.";
                    return false;
                }

                if (!this.TryBindAuraMonoGenericClass(actionOpen, new[] { intClass, stringClass }, out IntPtr closed)
                    || closed == IntPtr.Zero)
                {
                    miss = "System.Action<int, string> did not bind.";
                    return false;
                }

                steamCheckoutActionClass = closed;
            }

            return this.TryCacheSteamCheckoutPayOffsets(out miss);
        }

        private bool TryCacheSteamCheckoutPayOffsets(out string miss)
        {
            miss = null;
            if (steamCheckoutOffsetsReady)
            {
                return true;
            }

            IntPtr payInfo = this.FindAuraMonoClassAnySpelling(
                "EcsClient.XDT.Scene.Shared.Modules.Pay.PayInfo");
            if (payInfo == IntPtr.Zero)
            {
                miss = "PayInfo is not loaded yet.";
                return false;
            }

            steamCheckoutOffOrderId = this.SteamCheckoutFieldOffset(payInfo, "orderId");
            steamCheckoutOffProductId = this.SteamCheckoutFieldOffset(payInfo, "productId");
            steamCheckoutOffProductName = this.SteamCheckoutFieldOffset(payInfo, "productName");
            steamCheckoutOffAmount = this.SteamCheckoutFieldOffset(payInfo, "amount");
            steamCheckoutOffRoleId = this.SteamCheckoutFieldOffset(payInfo, "roleId");
            steamCheckoutOffServerId = this.SteamCheckoutFieldOffset(payInfo, "serverId");
            steamCheckoutOffExt = this.SteamCheckoutFieldOffset(payInfo, "ext");
            if (steamCheckoutOffOrderId < 0 || steamCheckoutOffProductId < 0 || steamCheckoutOffAmount < 0)
            {
                miss = "PayInfo field offsets missing (orderId=" + steamCheckoutOffOrderId.ToString()
                    + " productId=" + steamCheckoutOffProductId.ToString()
                    + " amount=" + steamCheckoutOffAmount.ToString() + ").";
                return false;
            }

            steamCheckoutOffsetsReady = true;
            return true;
        }

        private static void SteamCheckoutWindowsPayDetour(IntPtr self, IntPtr payInfo)
        {
            HeartopiaComplete inst = Instance;
            if (inst == null || !inst.privacySteamDirectCheckout || !SteamCheckoutLaunchedFromSteam())
            {
                steamCheckoutWindowsPayTrampoline?.Invoke(self, payInfo);
                return;
            }

            bool redirected = false;
            try
            {
                redirected = inst.TryRedirectSteamCheckout(self, payInfo);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[SteamCheckout] redirect failed: " + ex.Message);
            }

            if (!redirected)
            {
                steamCheckoutWindowsPayTrampoline?.Invoke(self, payInfo);
            }
        }

        private bool TryRedirectSteamCheckout(IntPtr module, IntPtr payInfo)
        {
            if (module == IntPtr.Zero || payInfo == IntPtr.Zero || steamCheckoutPayWithWebMethod == IntPtr.Zero)
            {
                return false;
            }

            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            string orderId = SteamCheckoutReadString(payInfo, steamCheckoutOffOrderId);
            string productId = SteamCheckoutReadString(payInfo, steamCheckoutOffProductId);
            string productName = SteamCheckoutReadString(payInfo, steamCheckoutOffProductName);
            string roleId = SteamCheckoutReadString(payInfo, steamCheckoutOffRoleId);
            string serverId = SteamCheckoutReadString(payInfo, steamCheckoutOffServerId);
            string ext = SteamCheckoutReadString(payInfo, steamCheckoutOffExt);
            double amount = SteamCheckoutReadDouble(payInfo, steamCheckoutOffAmount);
            if (!this.TryEnsureSteamCheckoutCallback(module, out IntPtr callback))
            {
                ModLogger.Msg("[SteamCheckout] callback delegate was not created — using Steam checkout.");
                return false;
            }

            IntPtr sdk = this.TryGetAuraMonoDataModuleInstance(steamCheckoutSdkClass);
            if (sdk == IntPtr.Zero)
            {
                IntPtr getInstance = this.FindAuraMonoMethodOnHierarchy(steamCheckoutSdkClass, "get_Instance", 0);
                if (getInstance == IntPtr.Zero
                    || !TryAuraInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, out sdk, out _)
                    || sdk == IntPtr.Zero)
                {
                    ModLogger.Msg("[SteamCheckout] XDSDKManager.Instance missing — using Steam checkout.");
                    return false;
                }
            }

            if (!this.TryInvokeSteamCheckoutPayWithWeb(sdk, orderId, productId, productName, amount, roleId, serverId, ext, callback))
            {
                ModLogger.Msg("[SteamCheckout] PayWithWeb failed — using Steam checkout.");
                return false;
            }

            ModLogger.Msg("[SteamCheckout] web checkout for order " + orderId + ".");
            return true;
        }

        private unsafe bool TryInvokeSteamCheckoutPayWithWeb(
            IntPtr sdk, string orderId, string productId, string productName, double amount,
            string roleId, string serverId, string ext, IntPtr callback)
        {
            IntPtr domain = this.auraMonoRootDomain;
            IntPtr order = auraMonoStringNew(domain, orderId ?? string.Empty);
            IntPtr product = auraMonoStringNew(domain, productId ?? string.Empty);
            IntPtr name = auraMonoStringNew(domain, productName ?? string.Empty);
            IntPtr role = auraMonoStringNew(domain, roleId ?? string.Empty);
            IntPtr server = auraMonoStringNew(domain, serverId ?? string.Empty);
            IntPtr extra = auraMonoStringNew(domain, ext ?? string.Empty);
            uint p0 = AuraMonoPinNew(order);
            uint p1 = AuraMonoPinNew(product);
            uint p2 = AuraMonoPinNew(name);
            uint p3 = AuraMonoPinNew(role);
            uint p4 = AuraMonoPinNew(server);
            uint p5 = AuraMonoPinNew(extra);
            try
            {
                double payAmount = amount;
                IntPtr* args = stackalloc IntPtr[8];
                args[0] = order;
                args[1] = product;
                args[2] = name;
                args[3] = (IntPtr)(&payAmount);
                args[4] = role;
                args[5] = server;
                args[6] = extra;
                args[7] = callback;
                return TryAuraInvoke(steamCheckoutPayWithWebMethod, sdk, (IntPtr)args, out _, out string error)
                    && string.IsNullOrEmpty(error);
            }
            finally
            {
                AuraMonoPinFree(p0);
                AuraMonoPinFree(p1);
                AuraMonoPinFree(p2);
                AuraMonoPinFree(p3);
                AuraMonoPinFree(p4);
                AuraMonoPinFree(p5);
            }
        }

        private bool TryEnsureSteamCheckoutCallback(IntPtr module, out IntPtr callback)
        {
            callback = IntPtr.Zero;
            long moduleBits = module.ToInt64();
            if (steamCheckoutCallbackPin != 0 && steamCheckoutBoundModule == moduleBits)
            {
                callback = AuraMonoPinTarget(steamCheckoutCallbackPin);
                return callback != IntPtr.Zero;
            }

            if (steamCheckoutCallbackPin != 0)
            {
                AuraMonoPinFree(steamCheckoutCallbackPin);
                steamCheckoutCallbackPin = 0;
                steamCheckoutBoundModule = 0;
            }

            IntPtr actionType = steamCheckoutActionTypePin != 0
                ? AuraMonoPinTarget(steamCheckoutActionTypePin)
                : IntPtr.Zero;
            IntPtr methodInfo = steamCheckoutMethodInfoPin != 0
                ? AuraMonoPinTarget(steamCheckoutMethodInfoPin)
                : IntPtr.Zero;
            if (actionType == IntPtr.Zero || methodInfo == IntPtr.Zero || steamCheckoutCreateDelegateMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr created;
            string error;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = actionType;
                args[1] = module;
                args[2] = methodInfo;
                if (!TryAuraInvoke(steamCheckoutCreateDelegateMethod, IntPtr.Zero, (IntPtr)args, out created, out error)
                    || created == IntPtr.Zero)
                {
                    ModLogger.Msg("[SteamCheckout] CreateDelegate failed"
                        + (string.IsNullOrEmpty(error) ? "." : ": " + error));
                    return false;
                }
            }

            uint pin = AuraMonoPinNew(created);
            if (pin == 0)
            {
                return false;
            }

            steamCheckoutCallbackPin = pin;
            steamCheckoutBoundModule = moduleBits;
            callback = created;
            return true;
        }

        // Pins the closed Action<int, string> Type and the MethodInfo for OnWindowsPaymentCompleted.
        // Both are corlib objects, so the pins outlive the world that created them.
        private bool TryBindSteamCheckoutCallback(IntPtr monoModule, out string miss)
        {
            miss = null;
            if (steamCheckoutCreateDelegateMethod != IntPtr.Zero
                && steamCheckoutActionTypePin != 0
                && steamCheckoutMethodInfoPin != 0)
            {
                return true;
            }

            IntPtr create = this.FindSteamCheckoutCreateDelegate();
            if (create == IntPtr.Zero)
            {
                miss = "Delegate.CreateDelegate(Type, object, MethodInfo) was not found.";
                return false;
            }

            IntPtr domain = this.auraMonoRootDomain;
            if (domain == IntPtr.Zero || auraMonoClassGetType == null || auraMonoTypeGetObject == null)
            {
                miss = "Action<int, string> Type object was not created.";
                return false;
            }

            IntPtr actionTypeObj = auraMonoTypeGetObject(domain, auraMonoClassGetType(steamCheckoutActionClass));
            if (actionTypeObj == IntPtr.Zero)
            {
                miss = "Action<int, string> Type object was not created.";
                return false;
            }

            uint typePin = AuraMonoPinNew(actionTypeObj);
            if (typePin == 0)
            {
                miss = "Action<int, string> Type object could not be pinned.";
                return false;
            }

            SteamCheckoutMethodGetObject getObject = monoModule != IntPtr.Zero
                ? this.GetAuraMonoExport<SteamCheckoutMethodGetObject>(monoModule, "mono_method_get_object")
                : null;
            if (getObject == null || steamCheckoutCompletedMethod == IntPtr.Zero)
            {
                AuraMonoPinFree(typePin);
                miss = "mono_method_get_object unavailable.";
                return false;
            }

            IntPtr methodInfo = getObject(domain, steamCheckoutCompletedMethod, IntPtr.Zero);
            if (methodInfo == IntPtr.Zero)
            {
                AuraMonoPinFree(typePin);
                miss = "OnWindowsPaymentCompleted MethodInfo was not created.";
                return false;
            }

            uint infoPin = AuraMonoPinNew(methodInfo);
            if (infoPin == 0)
            {
                AuraMonoPinFree(typePin);
                miss = "OnWindowsPaymentCompleted MethodInfo could not be pinned.";
                return false;
            }

            steamCheckoutActionTypePin = typePin;
            steamCheckoutMethodInfoPin = infoPin;
            steamCheckoutCreateDelegateMethod = create;
            return true;
        }

        private IntPtr FindSteamCheckoutCreateDelegate()
        {
            if (auraMonoClassGetMethods == null || auraMonoMethodGetName == null
                || auraMonoMethodSignature == null || auraMonoSignatureGetParamCount == null
                || auraMonoSignatureGetParams == null)
            {
                return IntPtr.Zero;
            }

            IntPtr delegateClass = this.FindAuraMonoClassAnySpelling("System.Delegate");
            IntPtr multicastClass = this.FindAuraMonoClassAnySpelling("System.MulticastDelegate");
            IntPtr[] classes = new IntPtr[] { delegateClass, multicastClass };
            for (int c = 0; c < classes.Length; c++)
            {
                IntPtr klass = classes[c];
                if (klass == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr iter = IntPtr.Zero;
                while (true)
                {
                    IntPtr method = auraMonoClassGetMethods(klass, ref iter);
                    if (method == IntPtr.Zero)
                    {
                        break;
                    }

                    string name = Marshal.PtrToStringAnsi(auraMonoMethodGetName(method));
                    if (!string.Equals(name, "CreateDelegate", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    IntPtr sig = auraMonoMethodSignature(method);
                    if (sig == IntPtr.Zero || auraMonoSignatureGetParamCount(sig) != 3)
                    {
                        continue;
                    }

                    IntPtr paramIter = IntPtr.Zero;
                    string first = SteamCheckoutMonoTypeName(auraMonoSignatureGetParams(sig, ref paramIter));
                    string second = SteamCheckoutMonoTypeName(auraMonoSignatureGetParams(sig, ref paramIter));
                    string third = SteamCheckoutMonoTypeName(auraMonoSignatureGetParams(sig, ref paramIter));
                    if (first == "System.Type"
                        && second == "System.Object"
                        && third == "System.Reflection.MethodInfo")
                    {
                        return method;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static string SteamCheckoutMonoTypeName(IntPtr type)
        {
            if (type == IntPtr.Zero || auraMonoTypeGetName == null)
            {
                return string.Empty;
            }

            IntPtr raw = auraMonoTypeGetName(type);
            if (raw == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringAnsi(raw) ?? string.Empty;
            }
            finally
            {
                auraMonoFree?.Invoke(raw);
            }
        }

        // Missing fields stay -1. Offset 0 is legal for the first struct field, so a failed lookup
        // must not be stored as 0.
        private int SteamCheckoutFieldOffset(IntPtr klass, string fieldName)
        {
            return this.TryGetAuraMonoFieldRawOffset(klass, fieldName, out int raw) ? raw : -1;
        }

        private static string SteamCheckoutReadString(IntPtr payInfo, int offset)
        {
            if (payInfo == IntPtr.Zero || offset < 0)
            {
                return string.Empty;
            }

            try
            {
                IntPtr monoString = Marshal.ReadIntPtr(payInfo, offset);
                return McpMonoGetString(monoString) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static unsafe double SteamCheckoutReadDouble(IntPtr payInfo, int offset)
        {
            if (payInfo == IntPtr.Zero || offset < 0)
            {
                return 0d;
            }

            return *(double*)((byte*)payInfo + offset);
        }
    }
}
