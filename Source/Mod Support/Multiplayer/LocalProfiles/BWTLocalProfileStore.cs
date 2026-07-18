using System;
using System.IO;
using UnityEngine;
using Verse;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Spine.RimWorld.Serialization;

namespace Better_Work_Tab.Mod_Support.LocalProfiles
{
    internal static class BWTLocalProfileStore
    {
        private const int ProfileLoadDeferredWarningKey = 154927301;
        private const int ProfileSaveDeferredWarningKey = 154927302;

        private static BWTLocalProfile _current;
        private static bool _dirty;
        private static bool _suspendSaving; // Prevents saves while following

        public static BWTLocalProfile Current => _current;
        public static bool SuspendSaving { get => _suspendSaving; set => _suspendSaving = value; }

        public static void MarkDirty() => _dirty = true;

        public static void LoadOrCreateForCurrentSession()
        {
            if (!MultiplayerBridge.Active)
            {
                _current = null;
                _dirty = false;
                return;
            }

            // Scribe is a process-wide singleton. Starting this private document while a game,
            // replay, or another mod is using Scribe can clear their cross-reference and
            // PostLoadIniter state. FinalizeInit is the expected caller and normally reaches
            // this method with Scribe inactive; the guard is the invariant that protects future
            // callers and lifecycle refactors.
            if (!ScribeIsolationGuard.CanStart("BWT", "local profile load", ProfileLoadDeferredWarningKey))
            {
                _current = null;
                _dirty = false;
                return;
            }

            var saveKey = GetSaveKey();
            var playerKey = MultiplayerBridge.LocalPlayerName;

            var path = GetProfilePath(saveKey, playerKey);

            _current = TryLoad(path) ?? new BWTLocalProfile
            {
                SaveKey = saveKey,
                PlayerKey = playerKey
            };

            _dirty = false;
        }

        public static void SaveIfDirty()
        {
            if (!MultiplayerBridge.Active || _current == null || !_dirty || _suspendSaving)
                return;

            // Keep the dirty flag set when Scribe is busy. The component timer will retry after
            // the enclosing save/load operation finishes, without disturbing its global state.
            if (!ScribeIsolationGuard.CanStart("BWT", "local profile save", ProfileSaveDeferredWarningKey))
                return;

            var path = GetProfilePath(_current.SaveKey, _current.PlayerKey);

            try
            {
                Scribe.saver.InitSaving(path, "BWTLocalProfile");
                var tmp = _current;
                Scribe_Deep.Look(ref tmp, "Profile");
                Scribe.saver.FinalizeSaving();
                _dirty = false;
            }
            catch (Exception e)
            {
                Log.Error($"[BWT] Local profile save failed: {e}");
                // FinalizeSaving is not a recovery API and may itself continue a damaged write.
                // This operation started only after verifying Scribe was inactive, so stopping
                // the saver here cannot interrupt another owner.
                try { Scribe.saver.ForceStop(); } catch { /* preserve the original failure */ }
            }
        }

        private static BWTLocalProfile TryLoad(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                Scribe.loader.InitLoading(path);
                BWTLocalProfile loaded = null;
                Scribe_Deep.Look(ref loaded, "Profile");
                Scribe.loader.FinalizeLoading();
                return loaded;
            }
            catch (Exception e)
            {
                Log.Warning($"[BWT] Local profile load failed, starting fresh. {e}");
                // FinalizeLoading can run cross-reference and post-load callbacks. It must not be
                // used as cleanup after a partial profile read; discard only this failed loader.
                try { Scribe.loader.ForceStop(); } catch { /* preserve the original failure */ }
                return null;
            }
        }

        private static string GetSaveKey()
        {
            var game = Verse.Current.Game;
            var worldInfo = game?.World?.info;
            var candidate = game?.Info?.permadeathModeUniqueName;

            if (string.IsNullOrEmpty(candidate))
                candidate = worldInfo?.FileNameNoExtension;

            if (string.IsNullOrEmpty(candidate))
                candidate = worldInfo?.name;

            if (string.IsNullOrEmpty(candidate))
                candidate = "UnknownSave";

            return Sanitize(candidate);
        }

        private static string GetProfilePath(string saveKey, string playerKey)
        {
            var baseFolder = GenFilePaths.SaveDataFolderPath;
            if (string.IsNullOrEmpty(baseFolder))
                baseFolder = "SaveData";

            var folder = Path.Combine(Path.Combine(baseFolder, "BetterWorkTab"), "LocalProfiles");
            Directory.CreateDirectory(folder);

            var file = $"{Sanitize(saveKey)}__{Sanitize(playerKey)}.xml";
            return Path.Combine(folder, file);
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "Unnamed";

            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s.Trim();
        }
    }
}
