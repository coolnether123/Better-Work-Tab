using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Mod_Support.Multiplayer.Sync
{
    internal static class WorkColumnOrderSync
    {
        [SyncMethod]
        public static void ApplyWorkColumnOrder(List<string> orderedDefNames, List<string> movedDefNames = null)
        {
            if (orderedDefNames == null || orderedDefNames.Count == 0)
                return;

            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null)
                return;

            var workColumns = tableDef.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .ToList();

            // Rebuild the list based on the incoming order
            var result = new List<PawnColumnDef>();
            foreach (var name in orderedDefNames)
            {
                var col = workColumns.FirstOrDefault(c => c.workType?.defName == name);
                if (col != null) result.Add(col);
            }

            // Add any that were missing (sanity check)
            foreach (var col in workColumns)
            {
                if (!result.Contains(col)) result.Add(col);
            }

            var original = tableDef.columns.ToList();
            var preWork = new List<PawnColumnDef>();
            var postWork = new List<PawnColumnDef>();
            bool passedFirstWork = false;

            foreach (var col in original)
            {
                bool isWork = col.Worker is PawnColumnWorker_WorkPriority && col.workType != null;
                if (isWork)
                {
                    passedFirstWork = true;
                }
                else if (!passedFirstWork)
                {
                    preWork.Add(col);
                }
                else
                {
                    postWork.Add(col);
                }
            }

            tableDef.columns.Clear();
            tableDef.columns.AddRange(preWork);
            tableDef.columns.AddRange(result);
            tableDef.columns.AddRange(postWork);

            var game = Current.Game;
            if (game != null)
            {
                var comp = game.GetComponent<GameComponent_BWTWorldSettings>();
                if (comp != null)
                {
                    comp.ColumnCurrentOrder = result
                        .Where(c => c.workType != null)
                        .Select(c => c.workType.defName)
                        .ToList();
                }
            }

            if (movedDefNames != null)
            {
                foreach (var defName in movedDefNames)
                {
                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                    if (wt != null)
                    {
                        MainTabWindow_BetterWork.MarkColumnMoved(wt);
                    }
                }
            }

            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            Better_Work_Tab.UI.Headers.HeaderDrawingCoordinator.InvalidateSolution();
        }

        [SyncMethod]
        public static void ApplyWorkColumnMove(string workTypeDefName, int targetWorkIndex)
        {
            if (string.IsNullOrEmpty(workTypeDefName))
                return;

            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null)
                return;

            var workColumns = tableDef.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .ToList();

            var dragged = workColumns.FirstOrDefault(c => c.workType?.defName == workTypeDefName);
            if (dragged == null)
                return;

            var oldIndex = workColumns.IndexOf(dragged);
            if (oldIndex < 0)
                return;

            targetWorkIndex = Mathf.Clamp(targetWorkIndex, 0, workColumns.Count - 1);
            if (oldIndex == targetWorkIndex)
                return;

            workColumns.RemoveAt(oldIndex);
            workColumns.Insert(targetWorkIndex, dragged);

            var nonWork = tableDef.columns
                .Where(c => !(c.Worker is PawnColumnWorker_WorkPriority))
                .ToList();

            tableDef.columns.Clear();
            tableDef.columns.AddRange(nonWork);
            tableDef.columns.AddRange(workColumns);

            var game = Current.Game;
            if (game != null)
            {
                var comp = game.GetComponent<GameComponent_BWTWorldSettings>();
                if (comp != null)
                {
                    comp.ColumnCurrentOrder = workColumns
                        .Where(c => c.workType != null)
                        .Select(c => c.workType.defName)
                        .ToList();
                }
            }

            MainTabWindow_BetterWork.MarkColumnMoved(dragged.workType);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
