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
        public Dictionary<int, Dictionary<string, int>> PawnWorkGiverPriorityOverrides = new Dictionary<int, Dictionary<string, int>>();
        public Dictionary<int, Dictionary<string, List<string>>> PawnWorkGiverOrdering = new Dictionary<int, Dictionary<string, List<string>>>();
        public int SyncVersion = 0;

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

                WorkGiverToWorkTypeMap ??= new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private class WorkTypeOrderRecord : IExposable
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

        private class PawnWorkGiverPriorityRecord : IExposable
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

        private class PawnWorkGiverOrderRecord : IExposable
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
