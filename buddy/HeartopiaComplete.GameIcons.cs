using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using Il2CppInterop.Runtime;

namespace HeartopiaMod
{
    // Direct in-game item icon loading (research: docs/ITEM_ICON_PIPELINE.md).
    //
    // The only icon source for the Bag/AutoSell/PetFeed tiles: any item icon is loaded on
    // demand by staticId via the game's own asset pipeline.
    //   staticId -> RewardUtility.GetIconName(staticId, 0)            (AuraMono, EcsClient tables)
    //   iconName -> "ui_item_normal_" + iconName                      (asset key, same as BagPanel)
    //   key      -> ScriptsRefactory.ResSystem.ResManager.LoadSpriteAsync(key, cb)   (interop side)
    // The loaded Sprite is refcounted by the game, so the texture is copied
    // (CopySpriteTexture) into the shared autoSellBagItemTextures dictionary and the load
    // token is released. No disk involvement — the radar/ESP icon path consumes the same
    // in-memory dictionary (plus the dll-embedded tree/rare_tree icons).
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogGameIcons = false;

        private const float GameIconFailureRetrySeconds = 45f;
        private const float GameIconNameRetrySeconds = 5f;
        private const float GameIconLoadTimeoutSeconds = 12f;
        private const float GameIconPollIntervalSeconds = 0.6f;
        private const int GameIconMaxConcurrentLoads = 24;

        private sealed class GameIconPendingLoad
        {
            public string LoadKey = string.Empty;
            public List<string> StoreKeys = new List<string>();
            public int Token;
            public float StartedAt;
            public bool PollPickup;
            public bool CallbackFired;
            // Both delegates stay rooted here until the load completes — a collected
            // converted delegate means il2cpp calls into a freed thunk (native crash).
            public Delegate ManagedCallback;
            public object Il2CppCallback;
        }

        private MethodInfo gameIconHasAssetMethod;
        private MethodInfo gameIconLoadSpriteAsyncMethod;
        private Type gameIconSpriteActionType;
        private MethodInfo gameIconUnloadTokenMethod;
        private float gameIconResManagerRetryAt;
        private bool gameIconResManagerLoggedOnce;
        private bool gameIconDelegateModeBroken;
        private readonly Dictionary<string, GameIconPendingLoad> gameIconPendingByKey = new Dictionary<string, GameIconPendingLoad>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> gameIconFailedRetryAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<GameIconPendingLoad, Sprite>> gameIconCompleted = new List<KeyValuePair<GameIconPendingLoad, Sprite>>();
        private readonly object gameIconCompletedLock = new object();
        private readonly Dictionary<int, string> gameIconNameByStaticId = new Dictionary<int, string>();
        private readonly Dictionary<int, float> gameIconNameRetryAt = new Dictionary<int, float>();
        private int gameIconWorldEpoch;
        private float gameIconNextPollAt;

        // ---------------------------------------------------------------- ResManager (interop)

        private bool EnsureGameIconResManager()
        {
            if (this.gameIconLoadSpriteAsyncMethod != null && this.gameIconHasAssetMethod != null)
            {
                return true;
            }

            if (Time.time < this.gameIconResManagerRetryAt)
            {
                return false;
            }
            this.gameIconResManagerRetryAt = Time.time + 15f;

            try
            {
                Type type = this.FindLoadedType(
                    "ScriptsRefactory.ResSystem.ResManager",
                    "Il2CppScriptsRefactory.ResSystem.ResManager");
                if (type == null)
                {
                    return false;
                }

                MethodInfo hasAsset = type.GetMethod(
                    "HasAsset",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);

                MethodInfo loadSprite = null;
                Type actionType = null;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, "LoadSpriteAsync", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && method.ReturnType == typeof(int))
                    {
                        loadSprite = method;
                        actionType = parameters[1].ParameterType;
                        break;
                    }
                }

                MethodInfo unloadToken = type.GetMethod(
                    "UnLoadAsync",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);

                if (hasAsset == null || loadSprite == null || actionType == null)
                {
                    if (!this.gameIconResManagerLoggedOnce)
                    {
                        this.gameIconResManagerLoggedOnce = true;
                        ModLogger.Msg("[GameIcons] ResManager resolved but expected members missing (HasAsset=" + (hasAsset != null) + ", LoadSpriteAsync=" + (loadSprite != null) + ").");
                    }
                    return false;
                }

                this.gameIconHasAssetMethod = hasAsset;
                this.gameIconLoadSpriteAsyncMethod = loadSprite;
                this.gameIconSpriteActionType = actionType;
                this.gameIconUnloadTokenMethod = unloadToken;
                if (!this.gameIconResManagerLoggedOnce)
                {
                    this.gameIconResManagerLoggedOnce = true;
                    ModLogger.Msg("[GameIcons] ResManager ready (" + type.FullName + ", callback type " + actionType.Name + ").");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!this.gameIconResManagerLoggedOnce)
                {
                    this.gameIconResManagerLoggedOnce = true;
                    ModLogger.Msg("[GameIcons] ResManager resolve failed: " + ex.Message);
                }
                return false;
            }
        }

        private bool GameIconHasAsset(string key)
        {
            try
            {
                object result = this.gameIconHasAssetMethod?.Invoke(null, new object[] { key });
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private void GameIconReleaseToken(int token)
        {
            if (token <= 0)
            {
                return;
            }
            try
            {
                this.gameIconUnloadTokenMethod?.Invoke(null, new object[] { token });
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- staticId -> icon name

        // RewardUtility.GetIconName(int staticId, int step) — the exact resolver BagPanel cells use.
        // Namespace XDTGameSystem.Utilities, but the class lives in the XDTDataAndProtocol image.
        private unsafe bool TryResolveGameIconName(int staticId, out string iconName)
        {
            iconName = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.gameIconNameByStaticId.TryGetValue(staticId, out string cached))
            {
                iconName = cached;
                return !string.IsNullOrEmpty(cached);
            }

            if (this.gameIconNameRetryAt.TryGetValue(staticId, out float retryAt) && Time.time < retryAt)
            {
                return false;
            }
            this.gameIconNameRetryAt[staticId] = Time.time + GameIconNameRetrySeconds;

            if (!this.EnsureAuraMonoApiReady() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr rewardUtilityClass = this.FindAuraMonoClassByFullName("XDTGameSystem.Utilities.RewardUtility");
                if (rewardUtilityClass == IntPtr.Zero)
                {
                    rewardUtilityClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGameSystem.Utilities", "RewardUtility");
                }
                if (rewardUtilityClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getIconNameMethod = this.FindAuraMonoMethodOnHierarchy(rewardUtilityClass, "GetIconName", 2);
                if (getIconNameMethod == IntPtr.Zero)
                {
                    return false;
                }

                int step = 0;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&staticId);
                args[1] = (IntPtr)(&step);
                IntPtr nameObj = auraMonoRuntimeInvoke(getIconNameMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || nameObj == IntPtr.Zero || !this.TryReadMonoString(nameObj, out string rawName) || string.IsNullOrWhiteSpace(rawName))
                {
                    return false;
                }

                iconName = rawName.Trim();
                this.gameIconNameByStaticId[staticId] = iconName;
                this.gameIconNameRetryAt.Remove(staticId);
                // Feed the shared staticId -> icon-key index so radar/pet-feed lookups benefit too.
                try { this.RememberRadarStaticIdIconMapping(staticId, BuildGameIconLoadKey(iconName)); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                if (MasterLogGameIcons)
                {
                    ModLogger.Msg("[GameIcons] GetIconName(" + staticId + ") failed: " + ex.Message);
                }
                return false;
            }
        }

        private static string BuildGameIconLoadKey(string iconName)
        {
            string name = (iconName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return string.Empty;
            }
            return name.StartsWith("ui_item_", StringComparison.OrdinalIgnoreCase) ? name : ("ui_item_normal_" + name);
        }

        // ---------------------------------------------------------------- request entry points

        // Called from the AutoSell / Transfer grid texture-miss path every OnGUI frame, so all
        // early-outs are dictionary lookups.
        private void RequestGameItemIconForEntry(AutoSellBagItemEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            string iconName = null;
            if (entry.StaticId > 0 && this.TryResolveGameIconName(entry.StaticId, out string resolved))
            {
                iconName = resolved;
            }
            if (string.IsNullOrWhiteSpace(iconName))
            {
                iconName = !string.IsNullOrWhiteSpace(entry.SpriteName)
                    ? entry.SpriteName
                    : this.GetAutoSellSpriteNameFromMatchKey(entry.MatchKey);
            }
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return;
            }

            List<string> storeKeys = this.GetAutoSellItemTextureKeys(entry);
            this.RequestGameItemIconLoad(iconName, storeKeys, entry.StaticId);
        }

        // staticId-keyed variant for callers outside the AutoSell entry model (pet feed, radar).
        internal void RequestGameItemIconByStaticId(int staticId, string storeKey)
        {
            if (staticId <= 0)
            {
                return;
            }
            if (!this.TryResolveGameIconName(staticId, out string iconName))
            {
                return;
            }
            List<string> storeKeys = new List<string>();
            if (!string.IsNullOrWhiteSpace(storeKey))
            {
                storeKeys.Add(storeKey);
            }
            this.RequestGameItemIconLoad(iconName, storeKeys, staticId);
        }

        // Icon-name-keyed variant for callers that already hold a resolved sprite/icon key
        // (radar ESP candidates are normalized icon names without the ui_item_ prefix).
        internal void RequestGameItemIconByIconName(string iconName, string storeKey)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return;
            }
            List<string> storeKeys = new List<string>();
            if (!string.IsNullOrWhiteSpace(storeKey))
            {
                storeKeys.Add(storeKey);
            }
            this.RequestGameItemIconLoad(iconName, storeKeys, 0);
        }

        private void RequestGameItemIconLoad(string iconName, List<string> storeKeys, int staticId)
        {
            string loadKey = BuildGameIconLoadKey(iconName);
            if (loadKey.Length == 0)
            {
                return;
            }

            if (this.gameIconPendingByKey.ContainsKey(loadKey))
            {
                return;
            }
            if (this.gameIconFailedRetryAt.TryGetValue(loadKey, out float retryAt) && Time.time < retryAt)
            {
                return;
            }
            if (this.gameIconPendingByKey.Count >= GameIconMaxConcurrentLoads)
            {
                return;
            }

            if (!this.EnsureGameIconResManager())
            {
                return;
            }

            // Key-case probe: icon names from the tables are lowercase in practice, but trust the
            // table value first and fall back to the lowercased form before failing.
            string effectiveKey = loadKey;
            if (!this.GameIconHasAsset(effectiveKey))
            {
                string lower = loadKey.ToLowerInvariant();
                if (!string.Equals(lower, loadKey, StringComparison.Ordinal) && this.GameIconHasAsset(lower))
                {
                    effectiveKey = lower;
                }
                else
                {
                    this.gameIconFailedRetryAt[loadKey] = Time.time + GameIconFailureRetrySeconds;
                    if (MasterLogGameIcons)
                    {
                        ModLogger.Msg("[GameIcons] HasAsset=false for '" + loadKey + "' (staticId=" + staticId + ").");
                    }
                    return;
                }
            }

            GameIconPendingLoad pending = new GameIconPendingLoad
            {
                LoadKey = loadKey,
                StartedAt = Time.time
            };
            this.AddGameIconStoreKey(pending.StoreKeys, this.NormalizeAutoSellMatchKey(loadKey));
            this.AddGameIconStoreKey(pending.StoreKeys, loadKey.ToLowerInvariant());
            if (storeKeys != null)
            {
                foreach (string key in storeKeys)
                {
                    this.AddGameIconStoreKey(pending.StoreKeys, key);
                }
            }

            object callbackArg = null;
            if (!this.gameIconDelegateModeBroken)
            {
                try
                {
                    Action<Sprite> managed = sprite => this.OnGameIconSpriteLoaded(pending, sprite);
                    MethodInfo convert = null;
                    foreach (MethodInfo candidate in typeof(DelegateSupport).GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (string.Equals(candidate.Name, "ConvertDelegate", StringComparison.Ordinal)
                            && candidate.IsGenericMethodDefinition
                            && candidate.GetParameters().Length == 1)
                        {
                            convert = candidate.MakeGenericMethod(this.gameIconSpriteActionType);
                            break;
                        }
                    }
                    callbackArg = convert?.Invoke(null, new object[] { managed });
                    if (callbackArg != null)
                    {
                        pending.ManagedCallback = managed;
                        pending.Il2CppCallback = callbackArg;
                    }
                }
                catch (Exception ex)
                {
                    this.gameIconDelegateModeBroken = true;
                    ModLogger.Msg("[GameIcons] Delegate conversion failed, switching to poll pickup: " + ex.Message);
                    callbackArg = null;
                }
            }
            pending.PollPickup = callbackArg == null;

            int token;
            try
            {
                object result = this.gameIconLoadSpriteAsyncMethod.Invoke(null, new object[] { effectiveKey, callbackArg });
                token = result is int t ? t : 0;
            }
            catch (Exception ex)
            {
                this.gameIconFailedRetryAt[loadKey] = Time.time + GameIconFailureRetrySeconds;
                ModLogger.Msg("[GameIcons] LoadSpriteAsync('" + effectiveKey + "') threw: " + ex.Message);
                return;
            }

            if (token <= 0)
            {
                this.gameIconFailedRetryAt[loadKey] = Time.time + GameIconFailureRetrySeconds;
                if (MasterLogGameIcons)
                {
                    ModLogger.Msg("[GameIcons] LoadSpriteAsync('" + effectiveKey + "') returned token " + token + ".");
                }
                return;
            }

            pending.Token = token;
            this.gameIconWorldEpoch = HeartopiaComplete.AuraMonoWorldEpoch;
            this.gameIconPendingByKey[loadKey] = pending;
            if (MasterLogGameIcons)
            {
                ModLogger.Msg("[GameIcons] Load requested '" + effectiveKey + "' token=" + token + " poll=" + pending.PollPickup + ".");
            }
        }

        private void AddGameIconStoreKey(List<string> keys, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            string trimmed = value.Trim();
            foreach (string existing in keys)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            keys.Add(trimmed);
        }

        // Invoked by il2cpp when the async load finishes. Keep it minimal: no Unity object work
        // here, just hand the sprite to the main-loop drain.
        private void OnGameIconSpriteLoaded(GameIconPendingLoad pending, Sprite sprite)
        {
            try
            {
                pending.CallbackFired = true;
                lock (this.gameIconCompletedLock)
                {
                    this.gameIconCompleted.Add(new KeyValuePair<GameIconPendingLoad, Sprite>(pending, sprite));
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- per-frame drain

        private FeatureBreakerState gameIconTickBreaker;

        private void ProcessGameIconLoads()
        {
            float now = Time.time;
            if (!this.gameIconTickBreaker.ShouldRun(now))
            {
                return;
            }
            try
            {
                this.ProcessGameIconLoadsCore();
                this.gameIconTickBreaker.Success();
            }
            catch (Exception ex)
            {
                this.gameIconTickBreaker.Failure("GameIcons", ex, now);
            }
        }

        private void ProcessGameIconLoadsCore()
        {
            if (this.gameIconPendingByKey.Count == 0)
            {
                lock (this.gameIconCompletedLock)
                {
                    if (this.gameIconCompleted.Count == 0)
                    {
                        return;
                    }
                }
            }

            // World change: released tokens, cleared queue — netIds/assets re-resolve next request.
            if (this.gameIconWorldEpoch != HeartopiaComplete.AuraMonoWorldEpoch && this.gameIconPendingByKey.Count > 0)
            {
                foreach (GameIconPendingLoad stale in this.gameIconPendingByKey.Values)
                {
                    this.GameIconReleaseToken(stale.Token);
                }
                this.gameIconPendingByKey.Clear();
                lock (this.gameIconCompletedLock)
                {
                    this.gameIconCompleted.Clear();
                }
                this.gameIconWorldEpoch = HeartopiaComplete.AuraMonoWorldEpoch;
                return;
            }

            List<KeyValuePair<GameIconPendingLoad, Sprite>> completed = null;
            lock (this.gameIconCompletedLock)
            {
                if (this.gameIconCompleted.Count > 0)
                {
                    completed = new List<KeyValuePair<GameIconPendingLoad, Sprite>>(this.gameIconCompleted);
                    this.gameIconCompleted.Clear();
                }
            }

            int stored = 0;
            if (completed != null)
            {
                foreach (KeyValuePair<GameIconPendingLoad, Sprite> pair in completed)
                {
                    if (this.FinishGameIconLoad(pair.Key, pair.Value))
                    {
                        stored++;
                    }
                }
            }
            if (stored > 0 && MasterLogGameIcons)
            {
                ModLogger.Msg("[GameIcons] Stored " + stored + " icon texture(s) from direct load.");
            }

            if (this.gameIconPendingByKey.Count == 0)
            {
                return;
            }

            // Poll pickup for loads made without a callback (delegate conversion unavailable):
            // the loaded sprite appears under (or containing) its load key name.
            bool anyPoll = false;
            foreach (GameIconPendingLoad pending in this.gameIconPendingByKey.Values)
            {
                if (pending.PollPickup)
                {
                    anyPoll = true;
                    break;
                }
            }
            if (anyPoll && Time.time >= this.gameIconNextPollAt)
            {
                this.gameIconNextPollAt = Time.time + GameIconPollIntervalSeconds;
                this.PollGameIconSprites();
            }

            // Timeouts (covers failed loads whose callback never fires).
            List<string> timedOut = null;
            foreach (KeyValuePair<string, GameIconPendingLoad> pair in this.gameIconPendingByKey)
            {
                if (Time.time - pair.Value.StartedAt > GameIconLoadTimeoutSeconds)
                {
                    (timedOut ?? (timedOut = new List<string>())).Add(pair.Key);
                }
            }
            if (timedOut != null)
            {
                foreach (string key in timedOut)
                {
                    if (this.gameIconPendingByKey.TryGetValue(key, out GameIconPendingLoad pending))
                    {
                        this.gameIconPendingByKey.Remove(key);
                        this.GameIconReleaseToken(pending.Token);
                        this.gameIconFailedRetryAt[key] = Time.time + GameIconFailureRetrySeconds;
                        ModLogger.Msg("[GameIcons] Load timed out for '" + key + "' (callbackFired=" + pending.CallbackFired + ").");
                    }
                }
            }
        }

        private bool FinishGameIconLoad(GameIconPendingLoad pending, Sprite sprite)
        {
            this.gameIconPendingByKey.Remove(pending.LoadKey);

            bool ok = false;
            try
            {
                if (sprite != null)
                {
                    Texture2D copy = this.CopySpriteTexture(sprite, "[GameIcons]");
                    if (copy != null)
                    {
                        foreach (string key in pending.StoreKeys)
                        {
                            this.autoSellBagItemTextures[key] = copy;
                        }
                        ok = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GameIcons] Copy failed for '" + pending.LoadKey + "': " + ex.Message);
            }
            finally
            {
                this.GameIconReleaseToken(pending.Token);
            }

            if (!ok)
            {
                this.gameIconFailedRetryAt[pending.LoadKey] = Time.time + GameIconFailureRetrySeconds;
            }

            return ok;
        }

        private void PollGameIconSprites()
        {
            try
            {
                Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                if (sprites == null || sprites.Length == 0)
                {
                    return;
                }

                // Exact name match wins; a prefix match (game may suffix the loaded sprite name)
                // is only kept until an exact one shows up, so "…_apple" never steals
                // "…_apple_golden"'s pending slot.
                Dictionary<GameIconPendingLoad, Sprite> exactMatches = null;
                Dictionary<GameIconPendingLoad, Sprite> prefixMatches = null;
                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null)
                    {
                        continue;
                    }
                    string name = sprite.name;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    foreach (GameIconPendingLoad pending in this.gameIconPendingByKey.Values)
                    {
                        if (!pending.PollPickup)
                        {
                            continue;
                        }
                        if (string.Equals(name, pending.LoadKey, StringComparison.OrdinalIgnoreCase))
                        {
                            (exactMatches ?? (exactMatches = new Dictionary<GameIconPendingLoad, Sprite>()))[pending] = sprite;
                            break;
                        }
                        if (name.StartsWith(pending.LoadKey, StringComparison.OrdinalIgnoreCase))
                        {
                            Dictionary<GameIconPendingLoad, Sprite> prefixes = prefixMatches ?? (prefixMatches = new Dictionary<GameIconPendingLoad, Sprite>());
                            if (!prefixes.ContainsKey(pending))
                            {
                                prefixes[pending] = sprite;
                            }
                            break;
                        }
                    }
                }

                if (exactMatches != null)
                {
                    foreach (KeyValuePair<GameIconPendingLoad, Sprite> pair in exactMatches)
                    {
                        prefixMatches?.Remove(pair.Key);
                        if (this.gameIconPendingByKey.ContainsKey(pair.Key.LoadKey))
                        {
                            this.FinishGameIconLoad(pair.Key, pair.Value);
                        }
                    }
                }
                if (prefixMatches != null)
                {
                    foreach (KeyValuePair<GameIconPendingLoad, Sprite> pair in prefixMatches)
                    {
                        if (this.gameIconPendingByKey.ContainsKey(pair.Key.LoadKey))
                        {
                            this.FinishGameIconLoad(pair.Key, pair.Value);
                        }
                    }
                }
            }
            catch
            {
            }
        }

    }
}
