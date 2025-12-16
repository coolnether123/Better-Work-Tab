using System;
using System.IO;
using UnityEngine;
using Verse;
using Better_Work_Tab.Mod_Support.Multiplayer;

namespace Better_Work_Tab.Mod_Support.LocalProfiles
{
    internal static class BWTLocalProfileStore
    {
        private static BWTLocalProfile _current;
        private static bool _dirty;

        public static BWTLocalProfile Current => _current;

        public static void MarkDirty() => _dirty = true;

        public static void LoadOrCreateForCurrentSession()
        {
            if (!MultiplayerBridge.Active)
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
            if (!MultiplayerBridge.Active || _current == null || !_dirty)
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
                try { Scribe.saver.FinalizeSaving(); } catch { /* ignore */ }
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
                try { Scribe.loader.FinalizeLoading(); } catch { /* ignore */ }
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

            var folder = Path.Combine(baseFolder, "BetterWorkTab", "LocalProfiles");
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
