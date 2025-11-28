using System.Linq;
using Better_Work_Tab.UI;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    internal static class WorkColumnOrderSync
    {
        // This method is what MP will replicate.
        [SyncMethod]   // marked so MP can replicate calls :contentReference[oaicite:3]{index=3}
        internal static void ApplyWorkColumnMove(string columnDefName, int targetWorkIndex)
        {
            // Find the Work tab PawnTableDef (adjust if you use a custom one)
            var tableDef = DefDatabase<PawnTableDef>.GetNamed("Work");

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

            // Persist + mark dirty, same as your existing code
            Features.WorkColumnOrderManager.CaptureCurrent(tableDef);
            Features.WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        // One-time registration (can be used if you prefer explicit registration)
        [StaticConstructorOnStartup]
        private static class MPRegistration
        {
            static MPRegistration()
            {
                if (!MP.enabled) return;
                // Not strictly needed if [SyncMethod] is auto-scanned, but safe:
                MP.RegisterSyncMethod(typeof(WorkColumnOrderSync),
                                      nameof(ApplyWorkColumnMove));
            }
        }
    }
}
