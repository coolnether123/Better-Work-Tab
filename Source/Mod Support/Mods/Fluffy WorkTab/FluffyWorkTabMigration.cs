using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using Verse;

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
            if (component == null || component.FluffyWorkTabPriorityMigrationVersion >= MigrationVersion)
            {
                return;
            }

            List<FluffyPawnPriorityRecord> records = ReadLiveFluffyPriorities();
            if (records.Count == 0)
            {
                records = ReadSavedFluffyPriorities();
            }

            if (records.Count == 0)
            {
                return;
            }

            component.EnsureWorkGiverReassignmentData();
            int changed = Apply(component, records);
            component.FluffyWorkTabPriorityMigrationVersion = MigrationVersion;

            if (changed > 0)
            {
                TimePriorityService.NotifyLoaded();
                WorkGiverReassignmentManager.InvalidateCaches();
                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                Log.Message("[Better Work Tab] Imported " + changed + " Fluffy Work Tab priority entries.");
            }
        }

        private static List<FluffyPawnPriorityRecord> ReadLiveFluffyPriorities()
        {
            var records = new List<FluffyPawnPriorityRecord>();
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

                    var workGivers = new List<FluffyWorkGiverPriorityRecord>();
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

                        workGivers.Add(new FluffyWorkGiverPriorityRecord(workGiver, priorities));
                    }

                    if (workGivers.Count > 0)
                    {
                        records.Add(new FluffyPawnPriorityRecord(pawn, workGivers));
                    }
                }
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("Fluffy live priority import failed: " + ex.Message, DebugFeature.ModSupport);
            }

            return records;
        }

        private static List<FluffyPawnPriorityRecord> ReadSavedFluffyPriorities()
        {
            var records = new List<FluffyPawnPriorityRecord>();
            string saveName = _lastLoadingSaveName ?? Current.Game?.InitData?.gameToLoad;
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

                    var workGivers = new List<FluffyWorkGiverPriorityRecord>();
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
                            workGivers.Add(new FluffyWorkGiverPriorityRecord(workGiver, priorities));
                        }
                    }

                    if (workGivers.Count > 0)
                    {
                        records.Add(new FluffyPawnPriorityRecord(pawn, workGivers));
                    }
                }
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("Fluffy save priority import failed: " + ex.Message, DebugFeature.ModSupport);
            }

            return records;
        }

        private static int Apply(GameComponent_BWTWorldSettings component, List<FluffyPawnPriorityRecord> records)
        {
            int changed = 0;
            int importedMax = records
                .SelectMany(record => record.WorkGivers)
                .SelectMany(record => record.Priorities)
                .DefaultIfEmpty(PriorityConstants.VanillaMax)
                .Max();
            EnsurePriorityRangeForImport(importedMax);

            foreach (FluffyPawnPriorityRecord pawnRecord in records)
            {
                Pawn pawn = pawnRecord.Pawn;
                if (pawn?.workSettings == null)
                {
                    continue;
                }

                foreach (IGrouping<WorkTypeDef, FluffyWorkGiverPriorityRecord> group in pawnRecord.WorkGivers
                             .Where(record => record.WorkGiver?.workType != null)
                             .GroupBy(record => record.WorkGiver.workType))
                {
                    WorkTypeDef workType = group.Key;
                    if (workType == null || pawn.WorkTypeIsDisabled(workType))
                    {
                        continue;
                    }

                    List<FluffyWorkGiverPriorityRecord> workGiverPriorities = group.ToList();
                    int[] parentPriorities = BuildWorkTypePriorities(workGiverPriorities);
                    int parentFallback = ChooseFallbackPriority(parentPriorities);

                    WorkPrioritySystem.SetPriority(pawn.workSettings, workType, parentFallback);
                    changed++;

                    TimePriorityTarget workTypeTarget = TimePriorityTarget.ForWorkType(pawn, workType);
                    changed += SetSchedule(component, workTypeTarget, parentPriorities, parentFallback);

                    foreach (FluffyWorkGiverPriorityRecord workGiverPriority in workGiverPriorities)
                    {
                        WorkGiverDef workGiver = workGiverPriority.WorkGiver;
                        if (workGiver == null || ArraysEqual(workGiverPriority.Priorities, parentPriorities))
                        {
                            continue;
                        }

                        int workGiverFallback = ChooseFallbackPriority(workGiverPriority.Priorities);
                        WorkGiverReassignmentManager.SetPawnOverrideSynced(
                            pawn.thingIDNumber,
                            workGiver.defName,
                            workGiverFallback);
                        changed++;

                        TimePriorityTarget workGiverTarget = TimePriorityTarget.ForWorkGiver(
                            pawn,
                            workType,
                            workGiver,
                            WorkGiverDisplayNameService.HeaderLabel(workGiver));
                        changed += SetSchedule(component, workGiverTarget, workGiverPriority.Priorities, workGiverFallback);
                    }
                }
            }

            return changed;
        }

        private static void EnsurePriorityRangeForImport(int importedMax)
        {
            importedMax = PriorityAuthorityBroker.ClampMaxPriority(importedMax);
            if (importedMax <= PriorityConstants.VanillaMax)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (settings.maxPriorityInt < importedMax)
            {
                settings.maxPriorityInt = importedMax;
            }

            if (settings.priorityMode == PriorityMode.Vanilla)
            {
                settings.SetPriorityMode(PriorityMode.Auto);
            }

            settings.NormalizePrioritySettings();
            PriorityAuthorityBroker.InvalidateCaches();
        }

        private static int SetSchedule(
            GameComponent_BWTWorldSettings component,
            TimePriorityTarget target,
            int[] priorities,
            int fallbackPriority)
        {
            int[] normalized = NormalizePriorities(priorities, fallbackPriority);
            if (normalized.All(priority => priority == WorkPrioritySystem.ClampPriority(fallbackPriority)))
            {
                TimePriorityService.ClearSchedule(target);
                return 0;
            }

            TimePriorityService.SetPrioritiesSynced(target, normalized, fallbackPriority);
            return 1;
        }

        private static int[] BuildWorkTypePriorities(List<FluffyWorkGiverPriorityRecord> workGiverPriorities)
        {
            var result = new int[TimePriorityService.HoursPerDay];
            for (int hour = 0; hour < result.Length; hour++)
            {
                int best = 0;
                for (int i = 0; i < workGiverPriorities.Count; i++)
                {
                    int priority = workGiverPriorities[i].Priorities[hour];
                    if (priority <= WorkPrioritySystem.DisabledPriority)
                    {
                        continue;
                    }

                    if (best == 0 || priority < best)
                    {
                        best = priority;
                    }
                }

                result[hour] = best;
            }

            return result;
        }

        private static int ChooseFallbackPriority(int[] priorities)
        {
            return NormalizePriorities(priorities, WorkPrioritySystem.DisabledPriority)
                .Where(priority => priority > WorkPrioritySystem.DisabledPriority)
                .GroupBy(priority => priority)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .DefaultIfEmpty(WorkPrioritySystem.DisabledPriority)
                .First();
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
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i] = WorkPrioritySystem.ClampPriority(
                    priorities != null && i < priorities.Length
                        ? priorities[i]
                        : fallbackPriority);
            }

            return normalized;
        }

        private static bool ArraysEqual(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
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

        private sealed class FluffyPawnPriorityRecord
        {
            internal FluffyPawnPriorityRecord(Pawn pawn, List<FluffyWorkGiverPriorityRecord> workGivers)
            {
                Pawn = pawn;
                WorkGivers = workGivers ?? new List<FluffyWorkGiverPriorityRecord>();
            }

            internal Pawn Pawn { get; }
            internal List<FluffyWorkGiverPriorityRecord> WorkGivers { get; }
        }

        private sealed class FluffyWorkGiverPriorityRecord
        {
            internal FluffyWorkGiverPriorityRecord(WorkGiverDef workGiver, int[] priorities)
            {
                WorkGiver = workGiver;
                Priorities = NormalizePriorities(priorities, WorkPrioritySystem.DisabledPriority);
            }

            internal WorkGiverDef WorkGiver { get; }
            internal int[] Priorities { get; }
        }

#if (v1_0 || v0_19) && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
        [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), new[] { typeof(string) })]
        private static class Patch_GameDataSaveLoader_LoadGame_RecordFluffyWorkTabSave
        {
            private static void Prefix(string saveFileName)
            {
                RecordLoadingSave(saveFileName);
            }
        }
#else
        [HarmonyPatch(typeof(SavedGameLoader), nameof(SavedGameLoader.LoadGameFromSaveFile), new[] { typeof(string) })]
        private static class Patch_SavedGameLoader_LoadGameFromSaveFile_RecordFluffyWorkTabSave
        {
            private static void Prefix(string fileName)
            {
                RecordLoadingSave(fileName);
            }
        }
#endif
    }
}
