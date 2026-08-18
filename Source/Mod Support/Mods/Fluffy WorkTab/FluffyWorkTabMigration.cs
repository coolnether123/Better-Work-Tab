using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Better_Work_Tab.API;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    internal readonly struct FluffyWorkTabMigrationResult
    {
        internal FluffyWorkTabMigrationResult(
            bool isCurrentlyActive,
            bool hasSaveEvidence)
        {
            IsCurrentlyActive = isCurrentlyActive;
            HasSaveEvidence = hasSaveEvidence;
        }

        internal bool IsCurrentlyActive { get; }
        internal bool HasSaveEvidence { get; }
    }

    /// <summary>
    /// One-way compatibility bridge for saves that previously used Fluffy's Work Tab.
    /// Fluffy stores per-pawn, per-workgiver 24-hour priorities; BWT maps those into
    /// parent worktype schedules plus sub-work schedules.
    /// </summary>
    internal static class FluffyWorkTabMigration
    {
        private const int MigrationVersion = 1;

        private static string _lastLoadingSaveName;
        private static bool _hasPendingSaveLoad;

        internal static void RecordLoadingSave(string saveFileName)
        {
            _lastLoadingSaveName = saveFileName;
            _hasPendingSaveLoad = true;
        }

        internal static FluffyWorkTabMigrationResult MigrateIfNeeded(
            GameComponent_BWTWorldSettings component)
        {
            if (component == null)
            {
                return default;
            }

            bool isCurrentlyActive = FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;
            string saveName = Current.Game?.InitData?.gameToLoad;
            if (saveName.NullOrEmpty() && _hasPendingSaveLoad)
            {
                saveName = _lastLoadingSaveName;
            }

            // A LoadGame prefix belongs to exactly one FinalizeInit. Consuming it
            // prevents a new colony created later in this process from being
            // mistaken for the previously loaded save.
            _hasPendingSaveLoad = false;
            _lastLoadingSaveName = null;

            bool needsSavedState =
                component.ExternalWorkTabPriorityMigrationVersion < MigrationVersion ||
                component.FluffyWorkTabCompatibilityPromptVersion <
                    FluffyWorkTabPromptPolicy.CurrentPromptVersion;
            SavedFluffyState savedState = needsSavedState
                ? ReadSavedFluffyState(saveName)
                : new SavedFluffyState(default, null);
            int importedEntryCount = 0;
            if (component.ExternalWorkTabPriorityMigrationVersion < MigrationVersion)
            {
                List<ExternalPawnWorkGiverPriorityRecord> records = ReadLiveFluffyPriorities();
                if (records.Count == 0)
                {
                    records = savedState.PriorityRecords;
                }

                if (records.Count > 0)
                {
                    component.EnsureWorkGiverReassignmentData();
                    importedEntryCount = ExternalWorkTabPriorityImportService.Import(component, records);
                    component.ExternalWorkTabPriorityMigrationVersion = MigrationVersion;

                    if (importedEntryCount > 0)
                    {
                        Log.Message("[Better Work Tab] Imported " + importedEntryCount +
                            " Fluffy Work Tab priority entries.");
                    }
                }
            }

            return new FluffyWorkTabMigrationResult(
                isCurrentlyActive,
                savedState.Evidence.Detected);
        }

        internal static int ImportLivePriorities()
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null)
            {
                return 0;
            }

            List<ExternalPawnWorkGiverPriorityRecord> records = ReadLiveFluffyPriorities();
            if (records.Count == 0)
            {
                return 0;
            }

            component.EnsureWorkGiverReassignmentData();
            return ExternalWorkTabPriorityImportService.Import(component, records);
        }

        internal static bool HasMigrationHistory(GameComponent_BWTWorldSettings component)
        {
            try
            {
                return component?.ExternalWorkTabPriorityMigrationVersion > 0 ||
                    AccessTools.TypeByName(FluffyWorkTabSaveDetector.PriorityManagerClass) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void ExposeMigrationVersion(ref int version)
        {
            Scribe_Values.Look(ref version, "fluffyWorkTabPriorityMigrationVersion", 0);
        }

        internal static bool TryReadLivePriorityRecords(out IReadOnlyList<ExternalPawnWorkGiverPriorityRecord> records)
        {
            records = ReadLiveFluffyPriorities();
            return records.Count > 0;
        }

        private static List<ExternalPawnWorkGiverPriorityRecord> ReadLiveFluffyPriorities()
        {
            var records = new List<ExternalPawnWorkGiverPriorityRecord>();
            try
            {
                Type managerType = AccessTools.TypeByName(FluffyWorkTabSaveDetector.PriorityManagerClass);
                FieldInfo prioritiesField = AccessTools.Field(managerType, "priorities");
                if (prioritiesField == null)
                {
                    return records;
                }

                if (!(prioritiesField.GetValue(null) is IEnumerable pawnEntries))
                {
                    return records;
                }

                foreach (object pawnEntry in pawnEntries)
                {
                    if (!TryReadDictionaryEntry(pawnEntry, out object pawnKey, out object tracker) ||
                        !(pawnKey is Pawn pawn) ||
                        tracker == null)
                    {
                        continue;
                    }

                    FieldInfo trackerPrioritiesField = AccessTools.Field(tracker.GetType(), "priorities");
                    if (!(trackerPrioritiesField?.GetValue(tracker) is IEnumerable workGiverEntries))
                    {
                        continue;
                    }

                    var workGivers = new List<ExternalWorkGiverPriorityRecord>();
                    foreach (object workGiverEntry in workGiverEntries)
                    {
                        if (!TryReadDictionaryEntry(workGiverEntry, out object workGiverKey, out object workPriority) ||
                            !(workGiverKey is WorkGiverDef workGiver) ||
                            workPriority == null)
                        {
                            continue;
                        }

                        int[] priorities = ReadPriorityArray(workPriority);
                        if (priorities == null)
                        {
                            continue;
                        }

                        workGivers.Add(new ExternalWorkGiverPriorityRecord(workGiver, priorities));
                    }

                    if (workGivers.Count > 0)
                    {
                        records.Add(new ExternalPawnWorkGiverPriorityRecord(pawn, workGivers));
                    }
                }
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("Fluffy live priority import failed: " + ex.Message, DebugFeature.ModSupport);
            }

            return records;
        }

        private static SavedFluffyState ReadSavedFluffyState(string saveName)
        {
            var records = new List<ExternalPawnWorkGiverPriorityRecord>();
            if (saveName.NullOrEmpty())
            {
                return new SavedFluffyState(default, records);
            }

            string path;
            try
            {
                path = GenFilePaths.FilePathForSavedGame(saveName);
            }
            catch
            {
                return new SavedFluffyState(default, records);
            }

            if (path.NullOrEmpty() || !File.Exists(path))
            {
                return new SavedFluffyState(default, records);
            }

            try
            {
                var document = new XmlDocument();
                document.Load(path);
                FluffyWorkTabSaveEvidence evidence = FluffyWorkTabSaveDetector.Detect(document);
                XmlNode managerNode = document.SelectSingleNode(
                    "//components/li[@Class='" +
                    FluffyWorkTabSaveDetector.PriorityManagerClass +
                    "']");
                if (managerNode == null)
                {
                    return new SavedFluffyState(evidence, records);
                }

                Dictionary<string, Pawn> pawnsByLoadId = BuildPawnLoadIdMap();
                XmlNodeList valueNodes = managerNode.SelectNodes("./Priorities/values/li");
                XmlNodeList keyNodes = managerNode.SelectNodes("./Priorities/keys/li");
                if (valueNodes == null)
                {
                    return new SavedFluffyState(evidence, records);
                }

                for (int i = 0; i < valueNodes.Count; i++)
                {
                    XmlNode trackerNode = valueNodes[i];
                    string pawnLoadId = trackerNode["Pawn"]?.InnerText;
                    if (pawnLoadId.NullOrEmpty() && keyNodes != null && i < keyNodes.Count)
                    {
                        pawnLoadId = keyNodes[i].InnerText;
                    }

                    if (pawnLoadId.NullOrEmpty() ||
                        !pawnsByLoadId.TryGetValue(pawnLoadId.Trim(), out Pawn pawn))
                    {
                        continue;
                    }

                    XmlNodeList priorityNodes = trackerNode.SelectNodes("./Priorities/li");
                    if (priorityNodes == null)
                    {
                        continue;
                    }

                    var workGivers = new List<ExternalWorkGiverPriorityRecord>();
                    foreach (XmlNode priorityNode in priorityNodes)
                    {
                        string workGiverDefName = priorityNode["Workgiver"]?.InnerText;
                        string prioritiesText = priorityNode["Priorities"]?.InnerText;
                        if (workGiverDefName.NullOrEmpty() ||
                            !TryParsePriorityString(prioritiesText, out int[] priorities))
                        {
                            continue;
                        }

                        WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName.Trim());
                        if (workGiver != null)
                        {
                            workGivers.Add(new ExternalWorkGiverPriorityRecord(workGiver, priorities));
                        }
                    }

                    if (workGivers.Count > 0)
                    {
                        records.Add(new ExternalPawnWorkGiverPriorityRecord(pawn, workGivers));
                    }
                }

                return new SavedFluffyState(evidence, records);
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("Fluffy save priority import failed: " + ex.Message, DebugFeature.ModSupport);
                return new SavedFluffyState(default, records);
            }
        }

        private sealed class SavedFluffyState
        {
            internal SavedFluffyState(
                FluffyWorkTabSaveEvidence evidence,
                List<ExternalPawnWorkGiverPriorityRecord> priorityRecords)
            {
                Evidence = evidence;
                PriorityRecords = priorityRecords ?? new List<ExternalPawnWorkGiverPriorityRecord>();
            }

            internal FluffyWorkTabSaveEvidence Evidence { get; }
            internal List<ExternalPawnWorkGiverPriorityRecord> PriorityRecords { get; }
        }

        private static int[] ReadPriorityArray(object workPriority)
        {
            PropertyInfo prioritiesProperty = AccessTools.Property(workPriority.GetType(), "Priorities");
            return ExternalWorkTabPriorityArrayNormalizer.Normalize(
                prioritiesProperty?.GetValue(workPriority, null) as int[],
                WorkPrioritySystem.DisabledPriority);
        }

        private static bool TryParsePriorityString(string prioritiesText, out int[] priorities)
        {
            priorities = null;
            if (prioritiesText.NullOrEmpty())
            {
                return false;
            }

            var parsed = new List<int>(TimePriorityService.HoursPerDay);
            foreach (char character in prioritiesText)
            {
                if (char.IsDigit(character))
                {
                    parsed.Add(character - '0');
                }
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            while (parsed.Count < TimePriorityService.HoursPerDay)
            {
                parsed.Add(parsed[parsed.Count - 1]);
            }

            priorities = ExternalWorkTabPriorityArrayNormalizer.Normalize(
                parsed.Take(TimePriorityService.HoursPerDay).ToArray(),
                WorkPrioritySystem.DisabledPriority);
            return true;
        }

        private static Dictionary<string, Pawn> BuildPawnLoadIdMap()
        {
            var map = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (pawn == null)
                {
                    continue;
                }

                string loadId = pawn.GetUniqueLoadID();
                if (!loadId.NullOrEmpty() && !map.ContainsKey(loadId))
                {
                    map.Add(loadId, pawn);
                }
            }

            return map;
        }

        private static bool TryReadDictionaryEntry(object entry, out object key, out object value)
        {
            key = null;
            value = null;

            if (entry is DictionaryEntry dictionaryEntry)
            {
                key = dictionaryEntry.Key;
                value = dictionaryEntry.Value;
                return true;
            }

            Type type = entry?.GetType();
            PropertyInfo keyProperty = type?.GetProperty("Key");
            PropertyInfo valueProperty = type?.GetProperty("Value");
            if (keyProperty == null || valueProperty == null)
            {
                return false;
            }

            key = keyProperty.GetValue(entry, null);
            value = valueProperty.GetValue(entry, null);
            return true;
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), new[] { typeof(string) })]
        private static class Patch_GameDataSaveLoader_LoadGame_RecordFluffyWorkTabSave
        {
            private static void Prefix(string saveFileName)
            {
                RecordLoadingSave(saveFileName);
            }
        }
    }
}
