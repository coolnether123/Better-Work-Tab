using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Better_Work_Tab.Features.Workloads.V2;
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
        // Global priority/order clears are explicit state.  They are kept
        // separate from the legacy -1 priority bucket and from taxonomy so a
        // caller can distinguish "no stored opinion" from "clear this exact
        // global override/order" without inspecting the backing collections.
        internal HashSet<string> GlobalWorkGiverPriorityClears = new HashSet<string>(StringComparer.Ordinal);
        internal HashSet<string> GlobalWorkTypeOrderClears = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> PlayerMovedWorkGiversByWorkType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<int, Dictionary<string, int>> PawnWorkGiverPriorityOverrides = new Dictionary<int, Dictionary<string, int>>();
        public Dictionary<int, Dictionary<string, List<string>>> PawnWorkGiverOrdering = new Dictionary<int, Dictionary<string, List<string>>>();
        public int SyncVersion = 0;

        public bool HasAnyData()
        {
            return HasEntries(WorkGiverToWorkTypeMap) ||
                   HasEntries(WorkTypeWorkGiverOrder) ||
                   HasValues(GlobalWorkGiverPriorityClears) ||
                   HasValues(GlobalWorkTypeOrderClears) ||
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
                GlobalWorkGiverPriorityClears = new HashSet<string>(
                    GlobalWorkGiverPriorityClears ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                GlobalWorkTypeOrderClears = new HashSet<string>(
                    GlobalWorkTypeOrderClears ?? new HashSet<string>(),
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

        /// <summary>
        /// Replaces only this object's persisted state from an immutable
        /// transaction snapshot.  The owner remains the live game component;
        /// callers choose the new monotonic revision explicitly.
        /// </summary>
        internal void CopyFrom(WorkGiverReassignmentData snapshot, int newSyncVersion)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            WorkGiverReassignmentData copy = snapshot.Clone();
            WorkGiverToWorkTypeMap = copy.WorkGiverToWorkTypeMap;
            WorkTypeWorkGiverOrder = copy.WorkTypeWorkGiverOrder;
            GlobalWorkGiverPriorityClears = copy.GlobalWorkGiverPriorityClears;
            GlobalWorkTypeOrderClears = copy.GlobalWorkTypeOrderClears;
            PlayerMovedWorkGiversByWorkType = copy.PlayerMovedWorkGiversByWorkType;
            PawnWorkGiverPriorityOverrides = copy.PawnWorkGiverPriorityOverrides;
            PawnWorkGiverOrdering = copy.PawnWorkGiverOrdering;
            SyncVersion = newSyncVersion;
        }

        /// <summary>
        /// Stable comparison value for a transaction rollback lease.  It
        /// includes all BWT-owned collections, including explicit clear
        /// tombstones, but deliberately excludes the mutable revision number.
        /// </summary>
        internal string ComputeStateFingerprint()
        {
            EnsureCollections();
            var builder = new StringBuilder();
            foreach (var entry in WorkGiverToWorkTypeMap.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                builder.Append("map:").Append(WorkloadCanonical.Pair(entry.Key, entry.Value)).Append('\n');
            }

            foreach (var entry in WorkTypeWorkGiverOrder.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                builder.Append("global-order:").Append(WorkloadCanonical.Encode(entry.Key));
                AppendNames(builder, entry.Value);
            }

            foreach (string name in GlobalWorkTypeOrderClears.OrderBy(value => value, StringComparer.Ordinal))
            {
                builder.Append("global-order-clear:").Append(WorkloadCanonical.Encode(name)).Append('\n');
            }

            foreach (string name in GlobalWorkGiverPriorityClears.OrderBy(value => value, StringComparer.Ordinal))
            {
                builder.Append("global-priority-clear:").Append(WorkloadCanonical.Encode(name)).Append('\n');
            }

            foreach (var entry in PlayerMovedWorkGiversByWorkType.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                builder.Append("moved:").Append(WorkloadCanonical.Encode(entry.Key));
                AppendNames(builder, entry.Value);
            }

            foreach (var pawn in PawnWorkGiverPriorityOverrides.OrderBy(value => value.Key))
            {
                builder.Append("priority-pawn:").Append(pawn.Key).Append(':');
                foreach (var entry in (pawn.Value ?? new Dictionary<string, int>()).OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    builder.Append(WorkloadCanonical.Pair(entry.Key, WorkloadCanonical.Integer(entry.Value))).Append(';');
                }

                builder.Append('\n');
            }

            foreach (var pawn in PawnWorkGiverOrdering.OrderBy(value => value.Key))
            {
                builder.Append("order-pawn:").Append(pawn.Key).Append(':');
                foreach (var entry in (pawn.Value ?? new Dictionary<string, List<string>>()).OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    builder.Append(WorkloadCanonical.Encode(entry.Key));
                    AppendNames(builder, entry.Value);
                }
            }

            return WorkloadCanonical.Fingerprint(builder.ToString());
        }

        private static void AppendNames(StringBuilder builder, IEnumerable<string> names)
        {
            builder.Append('[');
            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                builder.Append(WorkloadCanonical.Encode(name));
            }

            builder.Append("]\n");
        }

        public void Clear()
        {
            EnsureCollections();
            WorkGiverToWorkTypeMap.Clear();
            WorkTypeWorkGiverOrder.Clear();
            GlobalWorkGiverPriorityClears.Clear();
            GlobalWorkTypeOrderClears.Clear();
            PlayerMovedWorkGiversByWorkType.Clear();
            PawnWorkGiverPriorityOverrides.Clear();
            PawnWorkGiverOrdering.Clear();
            SyncVersion++;
        }

        public void EnsureCollections()
        {
            WorkGiverToWorkTypeMap ??= new Dictionary<string, string>(StringComparer.Ordinal);
            WorkTypeWorkGiverOrder ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            GlobalWorkGiverPriorityClears ??= new HashSet<string>(StringComparer.Ordinal);
            GlobalWorkTypeOrderClears ??= new HashSet<string>(StringComparer.Ordinal);
            PlayerMovedWorkGiversByWorkType ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            PawnWorkGiverPriorityOverrides ??= new Dictionary<int, Dictionary<string, int>>();
            PawnWorkGiverOrdering ??= new Dictionary<int, Dictionary<string, List<string>>>();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref WorkGiverToWorkTypeMap, "workGiverToWorkTypeMap", LookMode.Value, LookMode.Value);

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

            Scribe_Collections.Look(ref orderRecords, "workTypeWorkGiverOrder", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorkTypeWorkGiverOrder = orderRecords?.ToDictionary(
                    r => r.WorkTypeDefName,
                    r => r.OrderedWorkGivers ?? new List<string>(),
                    StringComparer.Ordinal) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            List<string> globalPriorityClearRecords = null;
            if (Scribe.mode == LoadSaveMode.Saving && GlobalWorkGiverPriorityClears != null)
            {
                globalPriorityClearRecords = GlobalWorkGiverPriorityClears
                    .Where(name => !name.NullOrEmpty())
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }

            Scribe_Collections.Look(
                ref globalPriorityClearRecords,
                "globalWorkGiverPriorityClears",
                LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GlobalWorkGiverPriorityClears = new HashSet<string>(
                    globalPriorityClearRecords?.Where(name => !name.NullOrEmpty()) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);
            }

            List<string> globalOrderClearRecords = null;
            if (Scribe.mode == LoadSaveMode.Saving && GlobalWorkTypeOrderClears != null)
            {
                globalOrderClearRecords = GlobalWorkTypeOrderClears
                    .Where(name => !name.NullOrEmpty())
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }

            Scribe_Collections.Look(
                ref globalOrderClearRecords,
                "globalWorkTypeOrderClears",
                LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GlobalWorkTypeOrderClears = new HashSet<string>(
                    globalOrderClearRecords?.Where(name => !name.NullOrEmpty()) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);
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

            Scribe_Collections.Look(ref movedWorkGiverRecords, "playerMovedWorkGiversByWorkType", LookMode.Deep);

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

            Scribe_Collections.Look(ref pawnPriorities, "pawnWorkGiverPriorityOverrides", LookMode.Deep);

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

            Scribe_Collections.Look(ref pawnOrders, "pawnWorkGiverOrdering", LookMode.Deep);

            Scribe_Values.Look(ref SyncVersion, "workGiverReassignmentSyncVersion", 0);

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

        private static bool HasValues(HashSet<string> values)
        {
            return values != null && values.Count > 0;
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
                Scribe_Values.Look(ref WorkTypeDefName, "workType");
                Scribe_Collections.Look(ref OrderedWorkGivers, "orderedWorkGivers", LookMode.Value);
                OrderedWorkGivers ??= new List<string>();
            }
        }

        public class PawnWorkGiverPriorityRecord : IExposable
        {
            public int PawnId;
            public Dictionary<string, int> Priorities = new Dictionary<string, int>(StringComparer.Ordinal);

            public void ExposeData()
            {
                Scribe_Values.Look(ref PawnId, "pawnId");
                Scribe_Collections.Look(ref Priorities, "priorities", LookMode.Value, LookMode.Value);
                Priorities ??= new Dictionary<string, int>(StringComparer.Ordinal);
            }
        }

        public class PawnWorkGiverOrderRecord : IExposable
        {
            public int PawnId;
            public Dictionary<string, List<string>> Orders = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            public void ExposeData()
            {
                Scribe_Values.Look(ref PawnId, "pawnId");

                List<WorkTypeOrderRecord> list = null;
                if (Scribe.mode == LoadSaveMode.Saving && Orders != null)
                {
                    list = Orders.Select(kv => new WorkTypeOrderRecord { WorkTypeDefName = kv.Key, OrderedWorkGivers = kv.Value }).ToList();
                }

                Scribe_Collections.Look(ref list, "orders", LookMode.Deep);

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    Orders = list?.ToDictionary(r => r.WorkTypeDefName, r => r.OrderedWorkGivers) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
                }
            }
        }
    }
}
