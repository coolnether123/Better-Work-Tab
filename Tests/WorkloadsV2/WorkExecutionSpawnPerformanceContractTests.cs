using System;
using System.Collections.Generic;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkExecutionSpawnPerformanceContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "WorkExecutionOrder.cs"),
                "work execution spawn performance contracts");
            string manager = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "WorkGiverReassignmentManager.cs"));
            string execution = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "WorkExecutionOrder.cs"));
            string priorityRange = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "RaisedPriorityMaximum",
                "PriorityRangePolicy.cs"));
            string pawnTablePatch = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "Patches",
                "Patch_PawnTable_RecacheIfDirty.cs"));

            TopologyIsRevisionOwned(manager);
            PawnCompositionDoesNotScanAllDefs(manager);
            PawnDisplayCompositionReusesGlobalCache(manager);
            EffectivePrioritiesAreEvaluatedOnce(manager);
            ColumnIndexesFollowTheExistingGeneration(execution);
            HighestPriorityScanUsesMutationInvalidation(priorityRange, pawnTablePatch);
            IndexedCompositionMatchesTheLegacyOrder();
        }

        private static void HighestPriorityScanUsesMutationInvalidation(
            string priorityRange,
            string pawnTablePatch)
        {
            string ensure = MemberBody(
                priorityRange,
                "private static void EnsureCachedPriorityScan()");
            TestAssert.Contains(
                ensure,
                "cacheAge < HighestPrioritySafetyAuditFrames",
                "the live-priority compatibility scan must not run on every repaint frame");
            TestAssert.Contains(
                ensure,
                "ReferenceEquals(cachedGame, game)",
                "the live-priority cache must not cross game ownership");
            TestAssert.Contains(
                priorityRange,
                "private const int HighestPrioritySafetyAuditFrames",
                "direct external priority writes need a bounded safety audit");

            string roster = MemberBody(
                pawnTablePatch,
                "public static void Postfix(MainTabWindow_PawnTable __instance)");
            TestAssert.Contains(
                roster,
                "PriorityRangePolicy.InvalidateCache();",
                "pawn roster changes must invalidate the cached live-priority maximum");
        }

        private static void TopologyIsRevisionOwned(string manager)
        {
            TestAssert.Contains(
                manager,
                "private static readonly Dictionary<string, List<WorkGiverDef>> WorkGiverTopologyCache",
                "the reassignment owner must retain one target-work-type topology");

            string invalidation = MemberBody(manager, "internal static void InvalidateCaches()");
            TestAssert.Contains(
                invalidation,
                "WorkGiverTopologyCache.Clear();",
                "a reassignment sync-version change must discard the topology");
            TestAssert.Contains(
                invalidation,
                "_workGiverTopologyBuilt = false;",
                "topology invalidation must force one later rebuild");

            string ensureVersion = MemberBody(manager, "private static void EnsureVersion()");
            TestAssert.Contains(
                ensureVersion,
                "InvalidateCaches();",
                "the existing sync-version boundary must own topology invalidation");
        }

        private static void PawnCompositionDoesNotScanAllDefs(string manager)
        {
            string compose = MemberBody(
                manager,
                "private static IReadOnlyList<WorkGiver> GetWorkGiversForWorkType");
            TestAssert.False(
                compose.IndexOf("AllDefsListForReading", StringComparison.Ordinal) >= 0,
                "a pawn-specific work-type composition must not scan every WorkGiverDef");
            TestAssert.Contains(
                compose,
                "IReadOnlyList<WorkGiverDef> topology = GetWorkGiverTopology(workType);",
                "pawn composition must consume the revision-owned topology");

            string build = MemberBody(manager, "private static void EnsureWorkGiverTopology()");
            TestAssert.Contains(
                build,
                "DefDatabase<WorkGiverDef>.AllDefsListForReading",
                "the topology must still include the complete WorkGiverDef universe");
            TestAssert.Contains(
                build,
                "WorkTypeDef target = GetTargetWorkType(def);",
                "the topology must use reassigned target work types");
            TestAssert.Contains(
                build,
                "b.priorityInType.CompareTo(a.priorityInType)",
                "baseline ordering must retain descending vanilla priority-in-type order");
        }

        private static void EffectivePrioritiesAreEvaluatedOnce(string manager)
        {
            string compose = MemberBody(
                manager,
                "private static IReadOnlyList<WorkGiver> GetWorkGiversForWorkType");
            int sortStart = compose.IndexOf("indexed.Sort", StringComparison.Ordinal);
            TestAssert.True(sortStart >= 0, "could not locate the work-giver priority sort");
            string comparator = compose.Substring(sortStart);
            TestAssert.False(
                comparator.IndexOf("GetWorkGiverPriority", StringComparison.Ordinal) >= 0,
                "the comparer must not re-evaluate pawn overrides for every comparison");
            TestAssert.False(
                comparator.IndexOf("GetEffectiveWorkGiverPriority", StringComparison.Ordinal) >= 0,
                "the comparer must not re-evaluate schedules for every comparison");
            TestAssert.Contains(
                compose.Substring(0, sortStart),
                "new WorkGiverPriorityRecord(",
                "each work giver must receive one immutable sort record before sorting");
        }

        private static void PawnDisplayCompositionReusesGlobalCache(string manager)
        {
            string compose = MemberBody(
                manager,
                "private static IReadOnlyList<WorkGiver> GetWorkGiversForWorkType");
            TestAssert.Contains(
                compose,
                "!applyPrioritySort && pawn != null && !HasPawnOrdering(pawn, workType)",
                "roster-wide workload capture must detect display lists without pawn-local ordering");
            TestAssert.Contains(
                compose,
                "pawn = null;",
                "display composition without pawn-local ordering must flow through the shared work-type cache");
        }

        private static void ColumnIndexesFollowTheExistingGeneration(string execution)
        {
            string cache = MemberBody(execution, "private static void EnsureColumnOrderCache");
            TestAssert.Contains(
                cache,
                "cachedCustomOrderGeneration == state.Generation",
                "column index reuse must follow the column-order persistence port generation");
            TestAssert.Contains(
                cache,
                "CachedColumnOrderIndexes.Clear();",
                "a new game or column generation must rebuild the index");

            string rebuild = MemberBody(
                execution,
                "internal static void RebuildUsingSavedColumnOrder");
            TestAssert.Contains(
                rebuild,
                "IReadOnlyDictionary<string, int> indexMap = GetColumnOrderIndexes(state);",
                "pawn work-cache rebuilds must reuse the generation-owned column index");
            TestAssert.False(
                rebuild.IndexOf("new Dictionary<string, int>", StringComparison.Ordinal) >= 0,
                "pawn work-cache rebuilds must not allocate a column index map");
        }

        private static void IndexedCompositionMatchesTheLegacyOrder()
        {
            var defs = new List<Def>
            {
                new Def("haul-low", "Hauling", 10, true),
                new Def("clean", "Cleaning", 30, true),
                new Def("haul-high", "Hauling", 50, true),
                new Def("moved", "Hauling", 40, true),
                new Def("workerless", "Hauling", 60, false),
                new Def("cook", "Cooking", 20, true)
            };
            var saved = new List<string>
            {
                "haul-low",
                "missing",
                "cook",
                "haul-low",
                "workerless"
            };

            List<string> legacy = ComposeLegacy(defs, "Hauling", saved);
            List<string> indexed = ComposeIndexed(defs, "Hauling", saved);
            AssertSequence(legacy, indexed, "indexed topology composition changed manual-order semantics");
            AssertSequence(
                new List<string> { "haul-low", "haul-high", "moved" },
                indexed,
                "manual, reassigned, and fallback work givers were not composed as expected");

            var priorities = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "haul-low", 3 },
                { "moved", 0 },
                { "haul-high", 1 }
            };
            AssertSequence(
                SortLegacy(indexed, priorities),
                SortWithRecords(indexed, priorities),
                "cached priority records changed priority or disabled-work ordering");
        }

        private static List<string> ComposeLegacy(
            List<Def> defs,
            string target,
            List<string> saved)
        {
            var result = new List<string>();
            var handled = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < saved.Count; i++)
            {
                Def def = Find(defs, saved[i]);
                if (def != null && def.Target == target && def.HasWorker && handled.Add(def.Name))
                {
                    result.Add(def.Name);
                }
            }

            var remaining = new List<Def>();
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].Target == target && !handled.Contains(defs[i].Name))
                {
                    remaining.Add(defs[i]);
                }
            }
            remaining.Sort((a, b) => b.PriorityInType.CompareTo(a.PriorityInType));
            for (int i = 0; i < remaining.Count; i++)
            {
                if (remaining[i].HasWorker)
                {
                    result.Add(remaining[i].Name);
                }
            }
            return result;
        }

        private static List<string> ComposeIndexed(
            List<Def> defs,
            string target,
            List<string> saved)
        {
            var topology = new List<Def>();
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].Target == target)
                {
                    topology.Add(defs[i]);
                }
            }
            topology.Sort((a, b) => b.PriorityInType.CompareTo(a.PriorityInType));

            var result = new List<string>();
            var handled = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < saved.Count; i++)
            {
                Def def = Find(defs, saved[i]);
                if (def != null && def.Target == target && def.HasWorker && handled.Add(def.Name))
                {
                    result.Add(def.Name);
                }
            }
            for (int i = 0; i < topology.Count; i++)
            {
                if (topology[i].HasWorker && !handled.Contains(topology[i].Name))
                {
                    result.Add(topology[i].Name);
                }
            }
            return result;
        }

        private static List<string> SortLegacy(
            List<string> source,
            Dictionary<string, int> priorities)
        {
            var indexed = new List<IndexedName>();
            for (int i = 0; i < source.Count; i++)
            {
                indexed.Add(new IndexedName(source[i], i));
            }
            indexed.Sort((a, b) =>
            {
                int left = priorities[a.Name];
                int right = priorities[b.Name];
                int result = (left == 0 ? 999 : left).CompareTo(right == 0 ? 999 : right);
                return result != 0 ? result : a.Index.CompareTo(b.Index);
            });

            var names = new List<string>();
            for (int i = 0; i < indexed.Count; i++)
            {
                names.Add(indexed[i].Name);
            }
            return names;
        }

        private static List<string> SortWithRecords(
            List<string> source,
            Dictionary<string, int> priorities)
        {
            var records = ToRecords(source, priorities);
            records.Sort(CompareRecords);
            return Names(records);
        }

        private static List<Record> ToRecords(
            List<string> source,
            Dictionary<string, int> priorities)
        {
            var records = new List<Record>();
            for (int i = 0; i < source.Count; i++)
            {
                int priority = priorities[source[i]];
                records.Add(new Record(source[i], i, priority == 0 ? 999 : priority));
            }
            return records;
        }

        private static int CompareRecords(Record a, Record b)
        {
            int result = a.Priority.CompareTo(b.Priority);
            return result != 0 ? result : a.Index.CompareTo(b.Index);
        }

        private static List<string> Names(List<Record> records)
        {
            var names = new List<string>();
            for (int i = 0; i < records.Count; i++)
            {
                names.Add(records[i].Name);
            }
            return names;
        }

        private static Def Find(List<Def> defs, string name)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(defs[i].Name, name))
                {
                    return defs[i];
                }
            }
            return null;
        }

        private static void AssertSequence(
            IList<string> expected,
            IList<string> actual,
            string message)
        {
            TestAssert.True(expected.Count == actual.Count, message + " count");
            for (int i = 0; i < expected.Count; i++)
            {
                TestAssert.True(
                    StringComparer.Ordinal.Equals(expected[i], actual[i]),
                    message + " at index " + i);
            }
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source member " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "source member has no opening brace");
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(start, i - start + 1);
            }
            throw new InvalidOperationException("source member has no closing brace");
        }

        private sealed class Def
        {
            internal Def(string name, string target, int priorityInType, bool hasWorker)
            {
                Name = name;
                Target = target;
                PriorityInType = priorityInType;
                HasWorker = hasWorker;
            }

            internal string Name { get; }
            internal string Target { get; }
            internal int PriorityInType { get; }
            internal bool HasWorker { get; }
        }

        private readonly struct Record
        {
            internal Record(string name, int index, int priority)
            {
                Name = name;
                Index = index;
                Priority = priority;
            }

            internal string Name { get; }
            internal int Index { get; }
            internal int Priority { get; }
        }

        private readonly struct IndexedName
        {
            internal IndexedName(string name, int index)
            {
                Name = name;
                Index = index;
            }

            internal string Name { get; }
            internal int Index { get; }
        }
    }
}
