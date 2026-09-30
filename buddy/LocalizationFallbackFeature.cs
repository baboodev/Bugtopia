using System;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // UNTRANSLATED GAME STRINGS — show the Chinese original instead of "LOC:<hash>"
    //
    // The game's localized text lives in designTable.db (Layer A, one encrypted column per
    // language). A few thousand rows have only zhHans filled in (4927 of 41826 lack `en` on the
    // 2026-09-30 build, in the global AND the China build alike). For such a row the native
    // XDFramework.Expansion.LocalizationManager.GetText(int) finds the row, gets an empty string
    // and returns "LOC:{0}" — which is what a task HUD showed for "Scenic Dreamweave" ("LOC:1306391685
    // 0/1"). The Mono-side Localize(string) already falls back to the source text for an EMPTY
    // result, but "LOC:…" is not empty, so it passes straight through.
    //
    // No hook: GetText is IL2CPP (no .text patches) and every Localize call in the game goes
    // through it, so a Mono detour on the wrapper would be the hottest detour in the mod. Instead
    // this uses the lookup's own cache. LocalizationDb.TryGetText checks `_cache[key]` (keyed by
    // hash only — NOT by language) before touching SQLite whenever `_cacheEnabled`, which it is for
    // the design table. So once per world load:
    //   SELECT hash, zhHans FROM DesignTable WHERE <current language> IS NULL AND zhHans IS NOT NULL
    // through the game's own XDTSqlite connection, decrypt with its own GetDecryptedData, and put
    // the Chinese text into `_cache`. GetText then returns it like any translated string. Measured
    // on Ideapad (CN build, English): 4927 rows in ~27 ms.
    //
    // Runs from the world-ready gate: a language switch re-enters the world (LanguageSwitchPanel ->
    // GameWorld.EnterLevel), and that rebuilds the localization tables and drops this cache, so the
    // next world-ready fills it again for the new language. Labels already on screen were bound
    // before the fill and are re-resolved once.
    //
    // Everything is reached by reflection over the interop assemblies: the types are not in the
    // compile-time references of either loader flavor, and MelonLoader prefixes their namespaces.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string LocalizationFallbackWorldReadyCallbackName = "LocalizationFallback";
        private const BindingFlags LocFallbackFlags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private bool locFallbackResolveTried;
        private bool locFallbackResolved;
        private PropertyInfo locFallbackManagerInstance;
        private MethodInfo locFallbackGetLanguageKey;
        private MethodInfo locFallbackGetTextById;
        private PropertyInfo locFallbackDesignDb;
        private PropertyInfo locFallbackDbSqlite;
        private PropertyInfo locFallbackDbTableName;
        private PropertyInfo locFallbackDbCacheEnabled;
        private PropertyInfo locFallbackDbCache;
        private PropertyInfo locFallbackDbLanguages;
        private MethodInfo locFallbackExecuteCommand;
        private MethodInfo locFallbackGetDecryptedData;
        private int locFallbackLastLoggedLanguage = int.MinValue;

        private static Type FindLocFallbackInteropType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type t = assemblies[i].GetType(fullName, false) ?? assemblies[i].GetType("Il2Cpp" + fullName, false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private bool TryResolveLocalizationFallback()
        {
            if (this.locFallbackResolveTried)
            {
                return this.locFallbackResolved;
            }

            this.locFallbackResolveTried = true;
            Type manager = FindLocFallbackInteropType("XDFramework.Expansion.LocalizationManager");
            Type db = FindLocFallbackInteropType("XDFramework.Expansion.LocalizationDb");
            Type sqliteBase = FindLocFallbackInteropType("ScriptsRefactory.ResSystem.XDTSqliteBase");
            Type sqlite = FindLocFallbackInteropType("ScriptsRefactory.ResSystem.XDTSqlite");
            if (manager == null || db == null || sqliteBase == null || sqlite == null)
            {
                ModLogger.Msg("[LocFallback] interop types missing (manager=" + (manager != null) + " db=" + (db != null)
                              + " sqliteBase=" + (sqliteBase != null) + " sqlite=" + (sqlite != null) + ") — feature off.");
                return false;
            }

            this.locFallbackManagerInstance = manager.GetProperty("Instance", LocFallbackFlags);
            this.locFallbackGetLanguageKey = manager.GetMethod("GetLanguageKey", LocFallbackFlags, null, Type.EmptyTypes, null);
            this.locFallbackGetTextById = manager.GetMethod("GetText", LocFallbackFlags, null, new[] { typeof(int) }, null);
            this.locFallbackDesignDb = manager.GetProperty("_designTableDb", LocFallbackFlags);
            this.locFallbackDbSqlite = db.GetProperty("mSqliteDB", LocFallbackFlags);
            this.locFallbackDbTableName = db.GetProperty("_tableName", LocFallbackFlags);
            this.locFallbackDbCacheEnabled = db.GetProperty("_cacheEnabled", LocFallbackFlags);
            this.locFallbackDbCache = db.GetProperty("_cache", LocFallbackFlags);
            this.locFallbackDbLanguages = db.GetProperty("_languages", LocFallbackFlags);
            this.locFallbackExecuteCommand = sqliteBase.GetMethod("ExecuteCommand", LocFallbackFlags);
            this.locFallbackGetDecryptedData = sqlite.GetMethod("GetDecryptedData", LocFallbackFlags);

            this.locFallbackResolved = this.locFallbackManagerInstance != null && this.locFallbackGetLanguageKey != null
                && this.locFallbackGetTextById != null && this.locFallbackDesignDb != null && this.locFallbackDbSqlite != null
                && this.locFallbackDbTableName != null && this.locFallbackDbCacheEnabled != null && this.locFallbackDbCache != null
                && this.locFallbackDbLanguages != null && this.locFallbackExecuteCommand != null
                && this.locFallbackGetDecryptedData != null && this.locFallbackGetDecryptedData.GetParameters().Length == 3;
            if (!this.locFallbackResolved)
            {
                ModLogger.Msg("[LocFallback] interop members missing on this build — feature off.");
            }
            return this.locFallbackResolved;
        }

        // World-ready callback: true = done for this world, false = retry a second later.
        private bool ApplyLocalizationFallbackOnWorldReady()
        {
            if (!this.TryResolveLocalizationFallback())
            {
                return true; // permanent: nothing to retry
            }

            try
            {
                object manager = this.locFallbackManagerInstance.GetValue(null);
                if (manager == null)
                {
                    return false;
                }

                int language = (int)this.locFallbackGetLanguageKey.Invoke(manager, null);
                if (language <= 0)
                {
                    return true; // zh-cn IS the source column: nothing can be missing
                }

                object db = this.locFallbackDesignDb.GetValue(manager);
                if (db == null)
                {
                    return false;
                }

                if (!(bool)this.locFallbackDbCacheEnabled.GetValue(db))
                {
                    // Without the cache the lookup always goes to SQLite and there is nothing to
                    // pre-fill; turning the cache on would change the game's own memory behaviour.
                    this.LogLocalizationFallbackOnce(language, "design table cache is disabled — skipped");
                    return true;
                }

                // `_languages` = { "hash", "zhHans", "zhHant", "en", ... }: TryGetText reads column
                // languageId + 1, so the current language's column is at language + 1.
                object languages = this.locFallbackDbLanguages.GetValue(null);
                string column = null;
                string source = null;
                if (languages != null)
                {
                    Type lt = languages.GetType();
                    int count = (int)lt.GetProperty("Length").GetValue(languages);
                    PropertyInfo item = lt.GetProperty("Item");
                    if (language + 1 < count && count > 1)
                    {
                        column = (string)item.GetValue(languages, new object[] { language + 1 });
                        source = (string)item.GetValue(languages, new object[] { 1 });
                    }
                }
                if (string.IsNullOrEmpty(column) || !string.Equals(source, "zhHans", StringComparison.Ordinal))
                {
                    this.LogLocalizationFallbackOnce(language, "unexpected language column layout (source=" + source + ") — skipped");
                    return true;
                }

                object sqlite = this.locFallbackDbSqlite.GetValue(db);
                object cache = this.locFallbackDbCache.GetValue(db);
                string table = (string)this.locFallbackDbTableName.GetValue(db);
                if (sqlite == null || cache == null || string.IsNullOrEmpty(table))
                {
                    return false;
                }

                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                int filled = this.FillLocalizationFallbackCache(sqlite, cache, table, column, source);
                int relabeled = filled > 0 ? this.RelabelUntranslatedGameTexts(manager) : 0;
                this.LogLocalizationFallbackOnce(language, "column " + column + ": " + filled
                    + " untranslated strings now show the Chinese original, " + relabeled + " visible labels re-resolved, "
                    + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[LocFallback] failed: " + (ex.InnerException ?? ex).Message);
            }
            return true;
        }

        private int FillLocalizationFallbackCache(object sqlite, object cache, string table, string column, string source)
        {
            string query = "SELECT hash, \"" + source + "\" FROM \"" + table + "\" WHERE \"" + column + "\" IS NULL AND \""
                           + source + "\" IS NOT NULL";
            object command = this.locFallbackExecuteCommand.Invoke(sqlite, new object[] { query });
            if (command == null)
            {
                return 0;
            }

            object reader = null;
            int filled = 0;
            try
            {
                reader = command.GetType().GetMethod("ExecuteReader", Type.EmptyTypes).Invoke(command, null);
                if (reader == null)
                {
                    return 0;
                }

                Type rt = reader.GetType();
                MethodInfo read = rt.GetMethod("Read", Type.EmptyTypes);
                MethodInfo getInt64 = rt.GetMethod("GetInt64", new[] { typeof(int) });
                PropertyInfo setItem = cache.GetType().GetProperty("Item");
                object[] keyArgs = new object[] { 0 };
                while ((bool)read.Invoke(reader, null))
                {
                    long key = (long)getInt64.Invoke(reader, keyArgs);
                    string text = (string)this.locFallbackGetDecryptedData.Invoke(sqlite, new object[] { reader, 1, key });
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }
                    // Overwrites a "" the game may already have cached for this key in this language.
                    setItem.SetValue(cache, text, new object[] { unchecked((int)key) });
                    filled++;
                }
            }
            finally
            {
                try { reader?.GetType().GetMethod("Close", Type.EmptyTypes)?.Invoke(reader, null); } catch { }
                try { command.GetType().GetMethod("Dispose", Type.EmptyTypes)?.Invoke(command, null); } catch { }
            }
            return filled;
        }

        // Labels bound before the fill still read "LOC:<id>…"; resolve the id again and keep whatever
        // followed it (a task condition carries "  0/1"). One pass per world load — not a poll.
        private int RelabelUntranslatedGameTexts(object manager)
        {
            int relabeled = 0;
            UnityEngine.UI.Text[] texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
            for (int i = 0; i < texts.Length; i++)
            {
                UnityEngine.UI.Text label = texts[i];
                string current = label != null ? label.text : null;
                string resolved = this.ResolveUntranslatedLabel(manager, current);
                if (resolved != null)
                {
                    label.text = resolved;
                    relabeled++;
                }
            }
            return relabeled;
        }

        private string ResolveUntranslatedLabel(object manager, string current)
        {
            if (current == null || !current.StartsWith("LOC:", StringComparison.Ordinal))
            {
                return null;
            }

            int end = 4;
            if (end < current.Length && current[end] == '-')
            {
                end++;
            }
            while (end < current.Length && char.IsDigit(current[end]))
            {
                end++;
            }
            if (!int.TryParse(current.Substring(4, end - 4), out int id))
            {
                return null;
            }

            string text = this.locFallbackGetTextById.Invoke(manager, new object[] { id }) as string;
            if (string.IsNullOrEmpty(text) || text.StartsWith("LOC:", StringComparison.Ordinal))
            {
                return null;
            }
            return text + current.Substring(end);
        }

        private void LogLocalizationFallbackOnce(int language, string message)
        {
            if (this.locFallbackLastLoggedLanguage == language)
            {
                return;
            }
            this.locFallbackLastLoggedLanguage = language;
            ModLogger.Msg("[LocFallback] language " + language + ": " + message);
        }
    }
}
