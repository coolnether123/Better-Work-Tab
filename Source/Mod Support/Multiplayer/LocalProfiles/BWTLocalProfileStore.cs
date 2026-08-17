using System;
using System.IO;
using System.Xml;
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
        private const int ProfileSwitchDeferredWarningKey = 154927304;

        private static BWTLocalProfile _current;
        private static bool _dirty;
        private static bool _suspendSaving; // Prevents saves while following
        private static string _pendingSaveKey;
        private static string _pendingPlayerKey;
        private static bool _quitHookRegistered;

        public static BWTLocalProfile Current => _current;
        public static bool SuspendSaving { get => _suspendSaving; set => _suspendSaving = value; }
        public static string LastDiagnostic { get; private set; }
        public static string LastQuarantinedPath { get; private set; }
        public static bool LastLoadRecoveredFromCorruption { get; private set; }

        public static bool IsLoadedForCurrentSession
        {
            get
            {
                if (!MultiplayerBridge.Active || _current == null || _pendingSaveKey != null)
                    return false;

                return string.Equals(_current.SaveKey, GetSaveKey(), StringComparison.Ordinal) &&
                       string.Equals(_current.PlayerKey, MultiplayerBridge.LocalPlayerName, StringComparison.Ordinal);
            }
        }

        public static void MarkDirty() => _dirty = true;

        public static bool LoadOrCreateForCurrentSession()
        {
            EnsureQuitHook();

            if (!MultiplayerBridge.Active)
            {
                if (!SaveIfDirty())
                {
                    return false;
                }

                _current = null;
                _dirty = false;
                _pendingSaveKey = null;
                _pendingPlayerKey = null;
                return true;
            }

            // Scribe is a process-wide singleton. Starting this private document while a game,
            // replay, or another mod is using Scribe can clear their cross-reference and
            // PostLoadIniter state. FinalizeInit is the expected caller and normally reaches
            // this method with Scribe inactive; the guard is the invariant that protects future
            // callers and lifecycle refactors.
            if (!ScribeIsolationGuard.CanStart("BWT", "local profile load", ProfileLoadDeferredWarningKey))
            {
                // A deferred load must not discard the profile currently in memory. It may be
                // dirty and may still be the only copy of edits made since the last successful
                // profile write. Keep it live and let the next lifecycle pass retry the switch.
                LastDiagnostic = "Local profile load was deferred because Scribe is active.";
                if (_current != null)
                {
                    _pendingSaveKey = GetSaveKey();
                    _pendingPlayerKey = MultiplayerBridge.LocalPlayerName;
                }
                return false;
            }

            var saveKey = GetSaveKey();
            var playerKey = MultiplayerBridge.LocalPlayerName;

            if (_current != null)
            {
                bool sameSession = string.Equals(_current.SaveKey, saveKey, StringComparison.Ordinal) &&
                                   string.Equals(_current.PlayerKey, playerKey, StringComparison.Ordinal);
                if (_dirty && !SaveIfDirty())
                {
                    _pendingSaveKey = saveKey;
                    _pendingPlayerKey = playerKey;
                    Log.WarningOnce(
                        "[BWT] Deferred local profile switch until the dirty profile can be saved.",
                        ProfileSwitchDeferredWarningKey);
                    return false;
                }

                if (sameSession && _pendingSaveKey == null)
                {
                    return true;
                }

            }

            if (!ScribeIsolationGuard.CanStart("BWT", "local profile load", ProfileLoadDeferredWarningKey))
            {
                _pendingSaveKey = saveKey;
                _pendingPlayerKey = playerKey;
                return false;
            }

            LastDiagnostic = null;
            LastQuarantinedPath = null;
            LastLoadRecoveredFromCorruption = false;

            string path;
            try
            {
                path = GetProfilePath(saveKey, playerKey);
            }
            catch (Exception e)
            {
                LastDiagnostic = "Local profile load failed before opening the profile path: " + e.Message;
                _pendingSaveKey = saveKey;
                _pendingPlayerKey = playerKey;
                Log.Warning("[BWT] Local profile path could not be opened; no profile data was replaced: " + e);
                return false;
            }

            bool wasCorrupt;
            BWTLocalProfile loaded = TryLoad(path, out wasCorrupt);
            if (wasCorrupt && LastQuarantinedPath == null)
            {
                // Never replace a corrupt profile when its original bytes could not be
                // quarantined. Keeping the previous in-memory profile (if any) also makes a
                // failed session switch recoverable instead of silently losing its context.
                _pendingSaveKey = saveKey;
                _pendingPlayerKey = playerKey;
                return false;
            }

            LastLoadRecoveredFromCorruption = wasCorrupt;

            BWTLocalProfile next = loaded ?? new BWTLocalProfile
            {
                SaveKey = saveKey,
                PlayerKey = playerKey
            };

            if (loaded != null)
            {
                next.SaveKey = string.IsNullOrEmpty(next.SaveKey) ? saveKey : next.SaveKey;
                next.PlayerKey = string.IsNullOrEmpty(next.PlayerKey) ? playerKey : next.PlayerKey;
            }

            _current = next;
            _dirty = false;
            _pendingSaveKey = null;
            _pendingPlayerKey = null;
            return true;
        }

        public static bool SaveIfDirty()
        {
            return SaveIfDirtyCore(ignoreSuspension: false);
        }

        public static bool FlushIfDirty()
        {
            return SaveIfDirtyCore(ignoreSuspension: true);
        }

        private static bool SaveIfDirtyCore(bool ignoreSuspension)
        {
            if (_current == null || !_dirty)
                return true;

            if (_suspendSaving && !ignoreSuspension)
                return false;

            // Keep the dirty flag set when Scribe is busy. The component timer will retry after
            // the enclosing save/load operation finishes, without disturbing its global state.
            if (!ScribeIsolationGuard.CanStart("BWT", "local profile save", ProfileSaveDeferredWarningKey))
            {
                LastDiagnostic = "The local profile save was deferred because Scribe is active.";
                return false;
            }

            string tempPath = null;
            bool saverStarted = false;

            try
            {
                string path = GetProfilePath(_current.SaveKey, _current.PlayerKey);
                tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
                Scribe.saver.InitSaving(tempPath, "BWTLocalProfile");
                saverStarted = true;
                var tmp = _current;
                Scribe_Deep.Look(ref tmp, "Profile");
                Scribe.saver.FinalizeSaving();
                saverStarted = false;
                ValidateProfileDocument(tempPath);
                ReplaceProfileAtomically(tempPath, path);
                _dirty = false;
                LastDiagnostic = null;
                return true;
            }
            catch (Exception e)
            {
                LastDiagnostic = "Local profile save failed and the existing profile was retained: " + e.Message;
                Log.Error($"[BWT] Local profile save failed: {e}");
                // FinalizeSaving is not a recovery API and may itself continue a damaged write.
                // This operation started only after verifying Scribe was inactive, so stopping
                // the saver here cannot interrupt another owner.
                if (saverStarted)
                {
                    try { Scribe.saver.ForceStop(); } catch { /* preserve the original failure */ }
                }
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath))
                {
                    TryDeleteTemporaryFile(tempPath);
                }
            }
        }

        private static BWTLocalProfile TryLoad(string path, out bool wasCorrupt)
        {
            wasCorrupt = false;
            if (!File.Exists(path))
                return null;

            bool loaderStarted = false;
            try
            {
                Scribe.loader.InitLoading(path);
                loaderStarted = true;
                BWTLocalProfile loaded = null;
                Scribe_Deep.Look(ref loaded, "Profile");
                Scribe.loader.FinalizeLoading();
                loaderStarted = false;
                if (loaded == null)
                    throw new InvalidDataException("The local profile document has no Profile root.");

                if (loaded.IsPersistenceReadOnly)
                {
                    throw new InvalidDataException(
                        string.IsNullOrEmpty(loaded.PersistenceDiagnostic)
                            ? "The local profile document is read-only for diagnostics."
                            : loaded.PersistenceDiagnostic);
                }

                return loaded;
            }
            catch (Exception e)
            {
                wasCorrupt = true;
                // FinalizeLoading can run cross-reference and post-load callbacks. It must not be
                // used as cleanup after a partial profile read; discard only this failed loader.
                if (loaderStarted)
                {
                    try { Scribe.loader.ForceStop(); } catch { /* preserve the original failure */ }
                }
                string quarantinePath = QuarantineCorruptProfile(path);
                LastQuarantinedPath = quarantinePath;
                LastDiagnostic = quarantinePath == null
                    ? "Local profile load failed and the corrupt profile could not be quarantined."
                    : "Local profile load failed; the corrupt profile was quarantined at " + quarantinePath;
                Log.Warning($"[BWT] Local profile load failed; no profile data was accepted. {e}. " +
                            (quarantinePath == null
                                ? "The corrupt profile was retained because it could not be quarantined."
                                : "The corrupt profile was quarantined at " + quarantinePath + "; a fresh profile may be created."));
                return null;
            }
        }

        private static void ValidateProfileDocument(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new InvalidDataException("The temporary local profile document is empty.");
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreWhitespace = false
            };

            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                if (!reader.Read() || reader.NodeType != XmlNodeType.Element)
                {
                    throw new InvalidDataException("The temporary local profile document has no XML root.");
                }

                while (reader.Read())
                {
                    // Reading to EOF validates the complete document, including all nested
                    // elements, before the atomic replacement is allowed.
                }
            }
        }

        private static void ReplaceProfileAtomically(string tempPath, string path)
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static string QuarantineCorruptProfile(string path)
        {
            if (!File.Exists(path)) return null;

            string quarantinePath = path + ".corrupt-" + Guid.NewGuid().ToString("N") + ".xml";
            try
            {
                File.Move(path, quarantinePath);
                return quarantinePath;
            }
            catch (Exception moveError)
            {
                try
                {
                    File.Copy(path, quarantinePath, overwrite: false);
                    return quarantinePath;
                }
                catch (Exception copyError)
                {
                    Log.Warning(
                        $"[BWT] Failed to quarantine corrupt local profile. Move: {moveError}; Copy: {copyError}");
                    return null;
                }
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warning($"[BWT] Failed to remove temporary local profile file '{path}': {e}");
            }
        }

        private static void EnsureQuitHook()
        {
            if (_quitHookRegistered) return;
            Application.quitting += OnApplicationQuitting;
            _quitHookRegistered = true;
        }

        private static void OnApplicationQuitting()
        {
            FlushIfDirty();
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
