using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Persisted reassignment data: global mapping, per-worktype ordering, and optional per-pawn overrides.
    /// </summary>
    public class WorkGiverReassignmentData : IExposable
    {
        public Dictionary<string, string> WorkGiverToWorkTypeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> WorkTypeWorkGiverOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> PlayerMovedWorkGiversByWorkType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<int, Dictionary<string, int>> PawnWorkGiverPriorityOverrides = new Dictionary<int, Dictionary<string, int>>();
        public Dictionary<int, Dictionary<string, List<string>>> PawnWorkGiverOrdering = new Dictionary<int, Dictionary<string, List<string>>>();
        public int SyncVersion = 0;

        public bool HasAnyData()
        {
            return HasEntries(WorkGiverToWorkTypeMap) ||
                   HasEntries(WorkTypeWorkGiverOrder) ||
                   HasEntries(PlayerMovedWorkGiversByWorkType) ||
                   HasNestedEntries(PawnWorkGiverPriorityOverrides) ||
                   HasNestedEntries(PawnWorkGiverOrdering);
        }

        public WorkGiverReassignmentData Clone()
        {
            EnsureCollections();

            return new WorkGiverReassignmentData
            {
                WorkGiverToWorkTypeMap = new Dictionary<string, string>(WorkGiverToWorkTypeMap, StringComparer.Ordinal),
                WorkTypeWorkGiverOrder = WorkTypeWorkGiverOrder.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value != null ? new List<string>(kv.Value) : new List<string>(),
                    StringComparer.Ordinal),
                PlayerMovedWorkGiversByWorkType = PlayerMovedWorkGiversByWorkType.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value != null ? new List<string>(kv.Value) : new List<string>(),
                    StringComparer.Ordinal),
                PawnWorkGiverPriorityOverrides = PawnWorkGiverPriorityOverrides.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value != null
                        ? new Dictionary<string, int>(kv.Value, StringComparer.Ordinal)
                        : new Dictionary<string, int>(StringComparer.Ordinal)),
                PawnWorkGiverOrdering = PawnWorkGiverOrdering.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value != null
                        ? kv.Value.ToDictionary(
                            inner => inner.Key,
                            inner => inner.Value != null ? new List<string>(inner.Value) : new List<string>(),
                            StringComparer.Ordinal)
                        : new Dictionary<string, List<string>>(StringComparer.Ordinal)),
                SyncVersion = SyncVersion
            };
        }

        public void Clear()
        {
            EnsureCollections();
            WorkGiverToWorkTypeMap.Clear();
            WorkTypeWorkGiverOrder.Clear();
            PlayerMovedWorkGiversByWorkType.Clear();
            PawnWorkGiverPriorityOverrides.Clear();
            PawnWorkGiverOrdering.Clear();
            SyncVersion++;
        }

        public void EnsureCollections()
        {
            WorkGiverToWorkTypeMap ??= new Dictionary<string, string>(StringComparer.Ordinal);
            WorkTypeWorkGiverOrder ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            PlayerMovedWorkGiversByWorkType ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            PawnWorkGiverPriorityOverrides ??= new Dictionary<int, Dictionary<string, int>>();
            PawnWorkGiverOrdering ??= new Dictionary<int, Dictionary<string, List<string>>>();
        }

        public void ExposeData()
        {
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            List<WorkGiverTargetRecord> targetRecords = null;
            if (Scribe.mode == LoadSaveMode.Saving && WorkGiverToWorkTypeMap != null)
            {
                targetRecords = WorkGiverToWorkTypeMap
                    .Select(kv => new WorkGiverTargetRecord
                    {
                        WorkGiverDefName = kv.Key,
                        WorkTypeDefName = kv.Value
                    })
                    .ToList();
            }

            Better_Work_Tab.ScribeCompat.LookCollection(ref targetRecords, "workGiverToWorkTypeMap", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorkGiverToWorkTypeMap = new Dictionary<string, string>(StringComparer.Ordinal);
                if (targetRecords != null)
                {
                    foreach (var record in targetRecords)
                    {
                        if (record == null ||
                            string.IsNullOrEmpty(record.WorkGiverDefName) ||
                            string.IsNullOrEmpty(record.WorkTypeDefName))
                        {
                            continue;
                        }

                        WorkGiverToWorkTypeMap[record.WorkGiverDefName] = record.WorkTypeDefName;
                    }
                }
            }
#else
            Scribe_Collections.Look(ref WorkGiverToWorkTypeMap, "workGiverToWorkTypeMap", LookMode.Value, LookMode.Value);
#endif

            List<WorkTypeOrderRecord> orderRecords = null;
            if (Scribe.mode == LoadSaveMode.Saving && WorkTypeWorkGiverOrder != null)
            {
                orderRecords = WorkTypeWorkGiverOrder
                    .Select(kv => new WorkTypeOrderRecord
                    {
                        WorkTypeDefName = kv.Key,
                        OrderedWorkGivers = kv.Value ?? new List<string>()
                    })
                    .ToList();
            }

            Better_Work_Tab.ScribeCompat.LookCollection(ref orderRecords, "workTypeWorkGiverOrder", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorkTypeWorkGiverOrder = orderRecords?.ToDictionary(
                    r => r.WorkTypeDefName,
                    r => r.OrderedWorkGivers ?? new List<string>(),
                    StringComparer.Ordinal) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            List<WorkTypeOrderRecord> movedWorkGiverRecords = null;
            if (Scribe.mode == LoadSaveMode.Saving && PlayerMovedWorkGiversByWorkType != null)
            {
                movedWorkGiverRecords = PlayerMovedWorkGiversByWorkType
                    .Select(kv => new WorkTypeOrderRecord
                    {
                        WorkTypeDefName = kv.Key,
                        OrderedWorkGivers = kv.Value ?? new List<string>()
                    })
                    .ToList();
            }

            Better_Work_Tab.ScribeCompat.LookCollection(ref movedWorkGiverRecords, "playerMovedWorkGiversByWorkType", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PlayerMovedWorkGiversByWorkType = movedWorkGiverRecords?.ToDictionary(
                    r => r.WorkTypeDefName,
                    r => r.OrderedWorkGivers ?? new List<string>(),
                    StringComparer.Ordinal) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            List<PawnWorkGiverPriorityRecord> pawnPriorities = null;
            if (Scribe.mode == LoadSaveMode.Saving && PawnWorkGiverPriorityOverrides != null)
            {
                pawnPriorities = PawnWorkGiverPriorityOverrides
                    .Select(kv => new PawnWorkGiverPriorityRecord
                    {
                        PawnId = kv.Key,
                        Priorities = kv.Value ?? new Dictionary<string, int>(StringComparer.Ordinal)
                    })
                    .ToList();
            }

            Better_Work_Tab.ScribeCompat.LookCollection(ref pawnPriorities, "pawnWorkGiverPriorityOverrides", LookMode.Deep);

            List<PawnWorkGiverOrderRecord> pawnOrders = null;
            if (Scribe.mode == LoadSaveMode.Saving && PawnWorkGiverOrdering != null)
            {
                pawnOrders = PawnWorkGiverOrdering
                    .Select(kv => new PawnWorkGiverOrderRecord
                    {
                        PawnId = kv.Key,
                        Orders = kv.Value ?? new Dictionary<string, List<string>>(StringComparer.Ordinal)
                    })
                    .ToList();
            }

            Better_Work_Tab.ScribeCompat.LookCollection(ref pawnOrders, "pawnWorkGiverOrdering", LookMode.Deep);

            Better_Work_Tab.ScribeCompat.LookValue(ref SyncVersion, "workGiverReassignmentSyncVersion", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PawnWorkGiverPriorityOverrides = pawnPriorities?.ToDictionary(
                    r => r.PawnId,
                    r => r.Priorities ?? new Dictionary<string, int>(StringComparer.Ordinal)) ?? new Dictionary<int, Dictionary<string, int>>();

                PawnWorkGiverOrdering = pawnOrders?.ToDictionary(
                    r => r.PawnId,
                    r => r.Orders ?? new Dictionary<string, List<string>>(StringComparer.Ordinal)) ?? new Dictionary<int, Dictionary<string, List<string>>>();

                EnsureCollections();
            }
        }

        private static bool HasEntries<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        {
            return dictionary != null && dictionary.Count > 0;
        }

        private static bool HasNestedEntries<TKey, TNestedKey, TNestedValue>(
            Dictionary<TKey, Dictionary<TNestedKey, TNestedValue>> dictionary)
        {
            if (dictionary == null)
            {
                return false;
            }

            foreach (var value in dictionary.Values)
            {
                if (value != null && value.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public class WorkTypeOrderRecord : IExposable
        {
            public string WorkTypeDefName;
            public List<string> OrderedWorkGivers = new List<string>();

            public void ExposeData()
            {
                Better_Work_Tab.ScribeCompat.LookValue(ref WorkTypeDefName, "workType");
                Better_Work_Tab.ScribeCompat.LookCollection(ref OrderedWorkGivers, "orderedWorkGivers", LookMode.Value);
                OrderedWorkGivers ??= new List<string>();
            }
        }

        public class WorkGiverTargetRecord : IExposable
        {
            public string WorkGiverDefName;
            public string WorkTypeDefName;

            public void ExposeData()
            {
                Better_Work_Tab.ScribeCompat.LookValue(ref WorkGiverDefName, "workGiver");
                Better_Work_Tab.ScribeCompat.LookValue(ref WorkTypeDefName, "workType");
            }
        }

        public class PawnWorkGiverPriorityRecord : IExposable
        {
            public int PawnId;
            public Dictionary<string, int> Priorities = new Dictionary<string, int>(StringComparer.Ordinal);

            public void ExposeData()
            {
                Better_Work_Tab.ScribeCompat.LookValue(ref PawnId, "pawnId");
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                List<WorkGiverPriorityRecord> priorityRecords = null;
                if (Scribe.mode == LoadSaveMode.Saving && Priorities != null)
                {
                    priorityRecords = Priorities
                        .Select(kv => new WorkGiverPriorityRecord
                        {
                            WorkGiverDefName = kv.Key,
                            Priority = kv.Value
                        })
                        .ToList();
                }

                Better_Work_Tab.ScribeCompat.LookCollection(ref priorityRecords, "priorities", LookMode.Deep);

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    Priorities = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (priorityRecords != null)
                    {
                        foreach (var record in priorityRecords)
                        {
                            if (record == null || string.IsNullOrEmpty(record.WorkGiverDefName))
                            {
                                continue;
                            }

                            Priorities[record.WorkGiverDefName] = record.Priority;
                        }
                    }
                }
#else
                Better_Work_Tab.ScribeCompat.LookCollection(ref Priorities, "priorities", LookMode.Value, LookMode.Value);
#endif
                Priorities ??= new Dictionary<string, int>(StringComparer.Ordinal);
            }
        }

        public class WorkGiverPriorityRecord : IExposable
        {
            public string WorkGiverDefName;
            public int Priority;

            public void ExposeData()
            {
                Better_Work_Tab.ScribeCompat.LookValue(ref WorkGiverDefName, "workGiver");
                Better_Work_Tab.ScribeCompat.LookValue(ref Priority, "priority");
            }
        }

        public class PawnWorkGiverOrderRecord : IExposable
        {
            public int PawnId;
            public Dictionary<string, List<string>> Orders = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            public void ExposeData()
            {
                Better_Work_Tab.ScribeCompat.LookValue(ref PawnId, "pawnId");

                List<WorkTypeOrderRecord> list = null;
                if (Scribe.mode == LoadSaveMode.Saving && Orders != null)
                {
                    list = Orders.Select(kv => new WorkTypeOrderRecord { WorkTypeDefName = kv.Key, OrderedWorkGivers = kv.Value }).ToList();
                }

                Better_Work_Tab.ScribeCompat.LookCollection(ref list, "orders", LookMode.Deep);

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    Orders = list?.ToDictionary(r => r.WorkTypeDefName, r => r.OrderedWorkGivers) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
                }
            }
        }
    }
}
