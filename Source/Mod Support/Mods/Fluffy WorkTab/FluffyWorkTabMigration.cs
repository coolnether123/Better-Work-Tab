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
using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using Verse;
using Current = Verse.Current;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// One-way compatibility bridge for saves that previously used Fluffy's Work Tab.
    /// Fluffy stores per-pawn, per-workgiver 24-hour priorities; BWT maps those into
    /// parent worktype schedules plus sub-work schedules.
    /// </summary>
    internal static class FluffyWorkTabMigration
    {
        private const string FluffyPriorityManagerClass = "WorkTab.PriorityManager";
        private const int MigrationVersion = 1;

        private static string _lastLoadingSaveName;

        internal static void RecordLoadingSave(string saveFileName)
        {
            _lastLoadingSaveName = saveFileName;
        }

        internal static void MigrateIfNeeded(GameComponent_BWTWorldSettings component)
        {
            if (component == null || component.ExternalWorkTabPriorityMigrationVersion >= MigrationVersion)
            {
                return;
            }

            List<ExternalPawnWorkGiverPriorityRecord> records = ReadLiveFluffyPriorities();
            if (records.Count == 0)
            {
                records = ReadSavedFluffyPriorities();
            }

            if (records.Count == 0)
            {
                return;
            }

            component.EnsureWorkGiverReassignmentData();
            int changed = ExternalWorkTabPriorityImportService.Import(component, records);
            component.ExternalWorkTabPriorityMigrationVersion = MigrationVersion;

            if (changed > 0)
            {
                Log.Message("[Better Work Tab] Imported " + changed + " Fluffy Work Tab priority entries.");
            }
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
                    AccessTools.TypeByName(FluffyPriorityManagerClass) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void ExposeMigrationVersion(ref int version)
        {
            ScribeCompat.LookValue(ref version, "fluffyWorkTabPriorityMigrationVersion", 0);
        }

        internal static bool TryReadLivePriorityRecords(out IList<ExternalPawnWorkGiverPriorityRecord> records)
        {
            records = ReadLiveFluffyPriorities();
            return records.Count > 0;
        }

        private static List<ExternalPawnWorkGiverPriorityRecord> ReadLiveFluffyPriorities()
        {
            var records = new List<ExternalPawnWorkGiverPriorityRecord>();
            try
            {
                Type managerType = AccessTools.TypeByName(FluffyPriorityManagerClass);
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

        private static List<ExternalPawnWorkGiverPriorityRecord> ReadSavedFluffyPriorities()
        {
            var records = new List<ExternalPawnWorkGiverPriorityRecord>();
#if v0_15
            string saveName = _lastLoadingSaveName;
#else
            string saveName = _lastLoadingSaveName ?? Current.Game?.InitData?.gameToLoad;
#endif
            if (saveName.NullOrEmpty())
            {
                return records;
            }

            string path;
            try
            {
                path = GenFilePaths.FilePathForSavedGame(saveName);
            }
            catch
            {
                return records;
            }

            if (path.NullOrEmpty() || !File.Exists(path))
            {
                return records;
            }

            try
            {
                var document = new XmlDocument();
                document.Load(path);
                XmlNode managerNode = document.SelectSingleNode("//components/li[@Class='WorkTab.PriorityManager']");
                if (managerNode == null)
                {
                    return records;
                }

                Dictionary<string, Pawn> pawnsByLoadId = BuildPawnLoadIdMap();
                XmlNodeList valueNodes = managerNode.SelectNodes("./Priorities/values/li");
                XmlNodeList keyNodes = managerNode.SelectNodes("./Priorities/keys/li");
                if (valueNodes == null)
                {
                    return records;
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
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("Fluffy save priority import failed: " + ex.Message, DebugFeature.ModSupport);
            }

            return records;
        }

        private static int[] ReadPriorityArray(object workPriority)
        {
            PropertyInfo prioritiesProperty = AccessTools.Property(workPriority.GetType(), "Priorities");
            return NormalizePriorities(prioritiesProperty?.GetValue(workPriority, null) as int[], WorkPrioritySystem.DisabledPriority);
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

            priorities = NormalizePriorities(parsed.Take(TimePriorityService.HoursPerDay).ToArray(), WorkPrioritySystem.DisabledPriority);
            return true;
        }

        private static int[] NormalizePriorities(int[] priorities, int fallbackPriority)
        {
            var normalized = new int[TimePriorityService.HoursPerDay];
            fallbackPriority = ClampImportedPriority(fallbackPriority);
            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i] = ClampImportedPriority(
                    priorities != null && i < priorities.Length
                        ? priorities[i]
                        : fallbackPriority);
            }

            return normalized;
        }

        private static int ClampImportedPriority(int priority)
        {
            return Math.Max(
                WorkPrioritySystem.DisabledPriority,
                Math.Min(PriorityConstants.ExtendedHardMax, priority));
        }

        private static Dictionary<string, Pawn> BuildPawnLoadIdMap()
        {
            var map = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            foreach (Pawn pawn in PawnsFinderCompat.AllAliveOrDead)
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

#if v0_13
#elif v0_18
        [HarmonyPatch(typeof(SavedGameLoader), nameof(SavedGameLoader.LoadGameFromSaveFile), new[] { typeof(string) })]
#else
        [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), new[] { typeof(string) })]
#endif
#if !v0_13
        private static class Patch_GameDataSaveLoader_LoadGame_RecordFluffyWorkTabSave
        {
            private static void Prefix(string saveFileName)
            {
                RecordLoadingSave(saveFileName);
            }
        }
#endif
    }
}
