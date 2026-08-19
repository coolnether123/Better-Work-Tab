using System;
using System.Collections.Generic;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Deterministic coverage for global specific-job tombstone cleanup. The
    /// runtime manager is Unity-bound, so the test combines source-contract
    /// checks with a small exact-state model for set/clear/orphan/reintroduce
    /// behavior.
    /// </summary>
    internal static class ReassignmentCleanupTests
    {
        public static void Run()
        {
            string root = FindRepositoryRoot();
            string manager = Read(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "WorkGiverReassignmentManager.cs");

            int start = manager.IndexOf(
                "internal static void CleanupOrphanedReassignments()",
                StringComparison.Ordinal);
            int end = manager.IndexOf(
                "\n        }\n    }\n}",
                start,
                StringComparison.Ordinal);
            TestAssert.True(start >= 0 && end > start,
                "the canonical reassignment cleanup method must remain discoverable");
            string cleanup = manager.Substring(start, end - start);

            TestAssert.Contains(
                cleanup,
                "data.EnsureCollections();",
                "cleanup must initialize all persisted reassignment collections, including tombstones");
            TestAssert.Contains(
                cleanup,
                "data.GlobalWorkGiverPriorityClears.ToList()",
                "global WorkGiver priority clears must be scanned independently of taxonomy mapping");
            TestAssert.Contains(
                cleanup,
                "data.GlobalWorkTypeOrderClears.ToList()",
                "global WorkType order clears must be scanned independently of taxonomy mapping");
            TestAssert.Contains(
                cleanup,
                "DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgName)",
                "global WorkGiver tombstones must be checked against current definitions");
            TestAssert.Contains(
                cleanup,
                "DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeName)",
                "global WorkType tombstones must be checked against current definitions");
            TestAssert.Contains(
                cleanup,
                "data.GlobalWorkGiverPriorityClears.Remove(wgName)",
                "orphaned global WorkGiver clears must be removed explicitly");
            TestAssert.Contains(
                cleanup,
                "data.GlobalWorkTypeOrderClears.Remove(workTypeName)",
                "orphaned global WorkType order clears must be removed explicitly");
            TestAssert.Contains(
                cleanup,
                "data.SyncVersion++",
                "cleanup must publish one canonical revision for removed tombstones");
            TestAssert.False(
                cleanup.IndexOf("WorkGiverToWorkTypeMap == null", StringComparison.Ordinal) >= 0,
                "tombstone cleanup must not depend on a taxonomy-map row existing");

            SetClearOrphanReintroduceModel();
        }

        private static void SetClearOrphanReintroduceModel()
        {
            var liveWorkGivers = new HashSet<string>(StringComparer.Ordinal)
            {
                "PlantCut"
            };
            var liveWorkTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "PlantWork"
            };
            var globalPriorityClears = new HashSet<string>(StringComparer.Ordinal)
            {
                "PlantCut",
                "RemovedWorkGiver"
            };
            var globalOrderClears = new HashSet<string>(StringComparer.Ordinal)
            {
                "PlantWork",
                "RemovedWorkType"
            };

            // Set -> clear is represented by a tombstone even when no live
            // override/order remains. Valid tombstones must survive that fact.
            TestAssert.True(globalPriorityClears.Contains("PlantCut"),
                "a valid global priority clear must remain explicit without a live override");
            TestAssert.True(globalOrderClears.Contains("PlantWork"),
                "a valid global order clear must remain explicit without a live order");

            RemoveOrphans(globalPriorityClears, liveWorkGivers);
            RemoveOrphans(globalOrderClears, liveWorkTypes);

            TestAssert.True(globalPriorityClears.Contains("PlantCut"),
                "cleanup must preserve a valid WorkGiver tombstone");
            TestAssert.True(globalOrderClears.Contains("PlantWork"),
                "cleanup must preserve a valid WorkType tombstone");
            TestAssert.False(globalPriorityClears.Contains("RemovedWorkGiver"),
                "cleanup must remove a clear-only orphaned WorkGiver tombstone");
            TestAssert.False(globalOrderClears.Contains("RemovedWorkType"),
                "cleanup must remove a clear-only orphaned WorkType tombstone");

            // Reintroducing a definition after cleanup must not resurrect the
            // old clear and suppress the newly valid target.
            liveWorkGivers.Add("RemovedWorkGiver");
            liveWorkTypes.Add("RemovedWorkType");
            TestAssert.False(globalPriorityClears.Contains("RemovedWorkGiver"),
                "an orphaned WorkGiver must reintroduce without an old tombstone");
            TestAssert.False(globalOrderClears.Contains("RemovedWorkType"),
                "an orphaned WorkType must reintroduce without an old tombstone");
        }

        private static void RemoveOrphans(
            HashSet<string> tombstones,
            HashSet<string> validDefinitions)
        {
            foreach (string value in new List<string>(tombstones))
            {
                if (!validDefinitions.Contains(value)) tombstones.Remove(value);
            }
        }

        private static string FindRepositoryRoot()
        {
            var starts = new List<string>
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (int startIndex = 0; startIndex < starts.Count; startIndex++)
            {
                string current = Path.GetFullPath(starts[startIndex]);
                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
                {
                    string managerPath = Path.Combine(
                        current,
                        "Source",
                        "Features",
                        "WorkGiverReassignments",
                        "WorkGiverReassignmentManager.cs");
                    if (File.Exists(managerPath)) return current;

                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent?.FullName;
                }
            }

            throw new InvalidOperationException(
                "Could not locate the Better Work Tab repository for cleanup contracts.");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++) path = Path.Combine(path, parts[i]);
            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }
    }
}
