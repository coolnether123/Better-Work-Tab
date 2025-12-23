using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Tracks vanilla baseline order for WorkGivers and identifies which have been moved.
    /// </summary>
    internal class WorkGiverBaselineTracker
    {
        private readonly WorkTypeDef _workType;
        private readonly List<string> _baselineOrder;
        private readonly Dictionary<string, bool> _movedFromBaseline;

        public WorkGiverBaselineTracker(WorkTypeDef workType, List<WorkGiver> currentWorkGivers)
        {
            _workType = workType;
            _movedFromBaseline = new Dictionary<string, bool>();
            
            // Get vanilla order for this WorkType (sorted by priorityInType)
            var vanillaGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(wg => wg.workType == _workType)
                .OrderByDescending(wg => wg.priorityInType)
                .Select(wg => wg.defName)
                .ToList();
            
            _baselineOrder = vanillaGivers;
            UpdateMovedStatus(currentWorkGivers);
        }

        public void UpdateMovedStatus(List<WorkGiver> currentWorkGivers)
        {
            _movedFromBaseline.Clear();
            
            var currentOrder = currentWorkGivers.Select(wg => wg.def.defName).ToList();
            for (int i = 0; i < currentOrder.Count; i++)
            {
                int baselineIndex = _baselineOrder.IndexOf(currentOrder[i]);
                _movedFromBaseline[currentOrder[i]] = baselineIndex >= 0 && baselineIndex != i;
            }
        }

        public bool IsMovedFromBaseline(string workGiverDefName)
        {
            return _movedFromBaseline.ContainsKey(workGiverDefName) && _movedFromBaseline[workGiverDefName];
        }

        public int GetBaselinePosition(string workGiverDefName)
        {
            return _baselineOrder.IndexOf(workGiverDefName);
        }

        public int CalculateBaselineTargetIndex(string draggedWorkGiverDefName, List<WorkGiver> currentWorkGivers, int draggedIndex)
        {
            int baselineIndex = GetBaselinePosition(draggedWorkGiverDefName);
            if (baselineIndex < 0) return -1;
            
            int targetPos = 0;
            for (int i = 0; i < currentWorkGivers.Count; i++)
            {
                if (i == draggedIndex) continue;
                
                int otherBaselineIndex = GetBaselinePosition(currentWorkGivers[i].def.defName);
                if (otherBaselineIndex >= 0 && otherBaselineIndex < baselineIndex)
                {
                    targetPos++;
                }
            }
            
            return targetPos;
        }
    }
}
