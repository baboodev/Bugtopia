using System;

namespace HeartopiaMod
{
    // Calm Creatures — client-side bird/insect alert suppression via BirdAIClientService /
    // InsectAIClientService static setters (invoked through AuraMono on the world-ready gate).
    public partial class HeartopiaComplete
    {
        private const string CalmCreaturesWorldReadyName = "CalmCreatures";

        private const string CalmCreaturesBirdServiceTypeName =
            "EcsSystem.ClientSystem.Bird.BirdAIClientService";

        private const string CalmCreaturesInsectServiceTypeName =
            "EcsSystem.ClientSystem.InsectCatching.InsectAIClientService";

        private static readonly string[] CalmCreaturesEcsSystemImageNames =
        {
            "EcsSystem", "EcsSystem.dll",
        };

        private bool insectsIgnoreAlert;

        private bool calmCreaturesPrevBirdIgnoreAlert;
        private bool calmCreaturesPrevInsectIgnoreAlert;
        private bool calmCreaturesCallbackRegistered;

        private IntPtr calmCreaturesBirdSetIgnoreAlertMethod;
        private IntPtr calmCreaturesInsectSetIgnoreAlertMethod;
        private bool calmCreaturesBirdMethodPermanentFail;
        private bool calmCreaturesInsectMethodPermanentFail;
        private bool calmCreaturesBirdMethodLogged;
        private bool calmCreaturesInsectMethodLogged;
        private bool calmCreaturesBirdClassMissLogged;
        private bool calmCreaturesInsectClassMissLogged;
        private string calmCreaturesBirdInvokeLastLoggedError;
        private string calmCreaturesInsectInvokeLastLoggedError;

        private void ProcessCalmCreaturesOnUpdate()
        {
            bool bird = BirdNetFarm.GetIgnoreAlertEnabled();
            bool insect = this.insectsIgnoreAlert;
            if (bird == this.calmCreaturesPrevBirdIgnoreAlert && insect == this.calmCreaturesPrevInsectIgnoreAlert)
            {
                return;
            }

            this.calmCreaturesPrevBirdIgnoreAlert = bird;
            this.calmCreaturesPrevInsectIgnoreAlert = insect;

            if (!this.calmCreaturesCallbackRegistered)
            {
                this.calmCreaturesCallbackRegistered = true;
                this.RegisterWorldReadyCallback(CalmCreaturesWorldReadyName, this.TryApplyCalmCreaturesOnWorldReady);
            }
            else
            {
                this.ResetWorldReadyCallback(CalmCreaturesWorldReadyName);
            }
        }

        private void RequestCalmCreaturesApply()
        {
            this.calmCreaturesPrevBirdIgnoreAlert = !BirdNetFarm.GetIgnoreAlertEnabled();
            this.calmCreaturesPrevInsectIgnoreAlert = !this.insectsIgnoreAlert;
            this.ProcessCalmCreaturesOnUpdate();
        }

        private bool TryApplyCalmCreaturesOnWorldReady()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                bool birdOk = this.ApplyBirdIgnoreAlert(BirdNetFarm.GetIgnoreAlertEnabled());
                bool insectOk = this.ApplyInsectIgnoreAlert(this.insectsIgnoreAlert);
                return birdOk && insectOk;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[CalmCreatures] apply failed: " + ex.Message);
                return true;
            }
        }

        private bool EnsureCalmCreaturesBirdMethod()
        {
            if (this.calmCreaturesBirdMethodPermanentFail)
            {
                return false;
            }

            if (this.calmCreaturesBirdSetIgnoreAlertMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr cls = this.ResolveCalmCreaturesBirdServiceClass();
            if (cls == IntPtr.Zero)
            {
                if (!this.calmCreaturesBirdClassMissLogged)
                {
                    this.calmCreaturesBirdClassMissLogged = true;
                    ModLogger.Msg("[CalmCreatures] BirdAIClientService class unresolved (retrying).");
                }

                return false;
            }

            this.calmCreaturesBirdSetIgnoreAlertMethod =
                this.FindAuraMonoMethodOnHierarchy(cls, "SetBirdIgnoreAlert", 1);
            if (this.calmCreaturesBirdSetIgnoreAlertMethod != IntPtr.Zero)
            {
                return true;
            }

            this.calmCreaturesBirdMethodPermanentFail = true;
            if (!this.calmCreaturesBirdMethodLogged)
            {
                this.calmCreaturesBirdMethodLogged = true;
                ModLogger.Msg("[CalmCreatures] BirdAIClientService.SetBirdIgnoreAlert not found — bird calm off.");
            }

            return false;
        }

        private bool EnsureCalmCreaturesInsectMethod()
        {
            if (this.calmCreaturesInsectMethodPermanentFail)
            {
                return false;
            }

            if (this.calmCreaturesInsectSetIgnoreAlertMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr cls = this.ResolveCalmCreaturesInsectServiceClass();
            if (cls == IntPtr.Zero)
            {
                if (!this.calmCreaturesInsectClassMissLogged)
                {
                    this.calmCreaturesInsectClassMissLogged = true;
                    ModLogger.Msg("[CalmCreatures] InsectAIClientService class unresolved (retrying).");
                }

                return false;
            }

            this.calmCreaturesInsectSetIgnoreAlertMethod =
                this.FindAuraMonoMethodOnHierarchy(cls, "SetInsectIgnoreAlert", 1);
            if (this.calmCreaturesInsectSetIgnoreAlertMethod != IntPtr.Zero)
            {
                return true;
            }

            this.calmCreaturesInsectMethodPermanentFail = true;
            if (!this.calmCreaturesInsectMethodLogged)
            {
                this.calmCreaturesInsectMethodLogged = true;
                ModLogger.Msg("[CalmCreatures] InsectAIClientService.SetInsectIgnoreAlert not found — insect calm off.");
            }

            return false;
        }

        private unsafe bool ApplyBirdIgnoreAlert(bool enabled)
        {
            if (!this.EnsureCalmCreaturesBirdMethod())
            {
                return this.calmCreaturesBirdMethodPermanentFail;
            }

            bool* valuePtr = stackalloc bool[1];
            valuePtr[0] = enabled;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)valuePtr;

            if (!TryAuraInvoke(this.calmCreaturesBirdSetIgnoreAlertMethod, IntPtr.Zero, (IntPtr)args, out _, out string error))
            {
                this.CalmCreaturesLogInvokeErrorOnce(
                    "SetBirdIgnoreAlert",
                    ref this.calmCreaturesBirdInvokeLastLoggedError,
                    error);
                return false;
            }

            this.calmCreaturesBirdInvokeLastLoggedError = null;
            return true;
        }

        private unsafe bool ApplyInsectIgnoreAlert(bool enabled)
        {
            if (!this.EnsureCalmCreaturesInsectMethod())
            {
                return this.calmCreaturesInsectMethodPermanentFail;
            }

            bool* valuePtr = stackalloc bool[1];
            valuePtr[0] = enabled;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)valuePtr;

            if (!TryAuraInvoke(this.calmCreaturesInsectSetIgnoreAlertMethod, IntPtr.Zero, (IntPtr)args, out _, out string error))
            {
                this.CalmCreaturesLogInvokeErrorOnce(
                    "SetInsectIgnoreAlert",
                    ref this.calmCreaturesInsectInvokeLastLoggedError,
                    error);
                return false;
            }

            this.calmCreaturesInsectInvokeLastLoggedError = null;
            return true;
        }

        private IntPtr ResolveCalmCreaturesBirdServiceClass()
        {
            // Real namespace is EcsSystem.ClientSystem.Bird (mono.find), not ClientSystem.Bird.
            IntPtr cls = this.FindAuraMonoClassInImages(
                "EcsSystem.ClientSystem.Bird",
                "BirdAIClientService",
                CalmCreaturesEcsSystemImageNames);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            cls = this.FindAuraMonoClassInAllLoadedImages(
                "BirdAIClientService",
                "EcsSystem.ClientSystem.Bird");
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassAnySpelling(CalmCreaturesBirdServiceTypeName);
        }

        private IntPtr ResolveCalmCreaturesInsectServiceClass()
        {
            IntPtr cls = this.FindAuraMonoClassInImages(
                "EcsSystem.ClientSystem.InsectCatching",
                "InsectAIClientService",
                CalmCreaturesEcsSystemImageNames);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            cls = this.FindAuraMonoClassInAllLoadedImages(
                "InsectAIClientService",
                "EcsSystem.ClientSystem.InsectCatching");
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassAnySpelling(CalmCreaturesInsectServiceTypeName);
        }

        private void CalmCreaturesLogInvokeErrorOnce(string methodName, ref string lastLogged, string error)
        {
            string message = methodName + " invoke failed: " + (error ?? "unknown");
            if (string.Equals(message, lastLogged, StringComparison.Ordinal))
            {
                return;
            }

            lastLogged = message;
            ModLogger.Msg("[CalmCreatures] " + message);
        }
    }
}