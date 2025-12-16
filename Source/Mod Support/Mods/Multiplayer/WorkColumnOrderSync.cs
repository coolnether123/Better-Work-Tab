using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    internal static class WorkColumnOrderSync
    {
        /// <summary>
        /// Synced method that applies a column move and updates the shared game component.
        /// This ensures all players have the same column order for deterministic work execution.
        /// </summary>
        [SyncMethod]
        internal static void ApplyWorkColumnMove(string columnDefName, int targetWorkIndex)
        {
            // Find the Work tab PawnTableDef
            var tableDef = DefDatabase<PawnTableDef>.GetNamed("Work");
            if (tableDef == null)
                return;

            var colToMove = tableDef.columns.FirstOrDefault(c => c.defName == columnDefName);
            if (colToMove == null)
                return;

            tableDef.columns.Remove(colToMove);

            // Rebuild list of work columns (those with PawnColumnWorker_WorkPriority)
            var workColumns = tableDef.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority)
                .ToList();

            int insertIndex;
            if (targetWorkIndex >= workColumns.Count)
            {
                var lastWork = tableDef.columns
                    .LastOrDefault(c => c.Worker is PawnColumnWorker_WorkPriority);

                insertIndex = (lastWork != null)
                    ? tableDef.columns.IndexOf(lastWork) + 1
                    : tableDef.columns.Count;
            }
            else
            {
                var anchor = workColumns[targetWorkIndex];
                insertIndex = tableDef.columns.IndexOf(anchor);
            }

            tableDef.columns.Insert(insertIndex, colToMove);

            // Update shared game component with new column order
            // This ensures deterministic work execution across all players
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (comp != null)
            {
                comp.ColumnCurrentOrder = tableDef.columns
                    .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                    .Select(c => c.workType.defName)
                    .ToList();
            }

            // Rebuild work giver order so pawns use new column priority tiebreaker
            Features.WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        /// <summary>
        /// Syncs the entire column order to all players (called on host at game start).
        /// </summary>
        [SyncMethod]
        internal static void SyncEntireColumnOrder(List<string> columnOrder)
        {
            if (columnOrder == null || columnOrder.Count == 0)
                return;

            var tableDef = DefDatabase<PawnTableDef>.GetNamed("Work");
            if (tableDef?.columns == null)
                return;

            // Build new order from the provided list
            var newOrder = new List<PawnColumnDef>();

            foreach (var defName in columnOrder)
            {
                var col = tableDef.columns.FirstOrDefault(c =>
                    c.Worker is PawnColumnWorker_WorkPriority &&
                    c.workType?.defName == defName);

                if (col != null)
                    newOrder.Add(col);
            }

            // Add any missing columns at the end (shouldn't happen, but safe)
            foreach (var col in tableDef.columns)
            {
                if (!newOrder.Contains(col))
                    newOrder.Add(col);
            }

            tableDef.columns = newOrder;

            Features.WorkColumnOrderManager.CaptureCurrent(tableDef);
            Features.WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        /// <summary>
        /// One-time registration for multiplayer sync.
        /// </summary>
        [StaticConstructorOnStartup]
        private static class MPRegistration
        {
            static MPRegistration()
            {
                if (!MP.enabled)
                    return;

                MP.RegisterSyncMethod(typeof(WorkColumnOrderSync),
                              nameof(ApplyWorkColumnMove));

                // Register full order sync
                MP.RegisterSyncMethod(typeof(WorkColumnOrderSync),
                                      nameof(SyncEntireColumnOrder));
            }
        }
    }
}