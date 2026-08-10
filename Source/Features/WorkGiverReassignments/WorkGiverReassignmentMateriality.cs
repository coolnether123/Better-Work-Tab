using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Determines whether saved reassignment records are valid enough to make the
    /// dynamic gameplay patches useful. The policy intentionally does not inspect
    /// pawns, vanilla baselines, priorities, or live gameplay state.
    /// </summary>
    internal static class WorkGiverReassignmentMateriality
    {
        internal static bool HasMaterialData(WorkGiverReassignmentData data)
        {
            return data != null &&
                   (HasValidMappings(data.WorkGiverToWorkTypeMap) ||
                    HasValidOrders(data.WorkTypeWorkGiverOrder) ||
                    HasValidOrders(data.PlayerMovedWorkGiversByWorkType) ||
                    HasValidPriorityOverrides(data.PawnWorkGiverPriorityOverrides) ||
                    HasValidPawnOrders(data.PawnWorkGiverOrdering));
        }

        private static bool HasValidMappings(Dictionary<string, string> mappings)
        {
            if (mappings == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> entry in mappings)
            {
                if (!entry.Key.NullOrEmpty() &&
                    !entry.Value.NullOrEmpty() &&
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail(entry.Key) != null &&
                    DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Value) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValidOrders(Dictionary<string, List<string>> orders)
        {
            if (orders == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, List<string>> entry in orders)
            {
                if (!entry.Key.NullOrEmpty() &&
                    DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Key) != null &&
                    HasValidWorkGiver(entry.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValidPriorityOverrides(
            Dictionary<int, Dictionary<string, int>> overrides)
        {
            if (overrides == null)
            {
                return false;
            }

            foreach (KeyValuePair<int, Dictionary<string, int>> pawnEntry in overrides)
            {
                if (pawnEntry.Key < -1 || pawnEntry.Value == null)
                {
                    continue;
                }

                foreach (string workGiverDefName in pawnEntry.Value.Keys)
                {
                    if (!workGiverDefName.NullOrEmpty() &&
                        DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName) != null)
                    {
                        // A non-negative pawn ID is intentionally accepted without a live-pawn
                        // lookup. Save records can arrive before the map or pawn is available.
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasValidPawnOrders(
            Dictionary<int, Dictionary<string, List<string>>> orders)
        {
            if (orders == null)
            {
                return false;
            }

            foreach (KeyValuePair<int, Dictionary<string, List<string>>> pawnEntry in orders)
            {
                if (pawnEntry.Key < -1 || pawnEntry.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, List<string>> order in pawnEntry.Value)
                {
                    if (!order.Key.NullOrEmpty() &&
                        DefDatabase<WorkTypeDef>.GetNamedSilentFail(order.Key) != null &&
                        HasValidWorkGiver(order.Value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasValidWorkGiver(List<string> workGiverDefNames)
        {
            if (workGiverDefNames == null)
            {
                return false;
            }

            for (int i = 0; i < workGiverDefNames.Count; i++)
            {
                string defName = workGiverDefNames[i];
                if (!defName.NullOrEmpty() &&
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail(defName) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
