using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Invalidation
{
    /// <summary>Low-frequency compatibility audit for external writers that bypass known hooks.</summary>
    internal static class WorkGridInvalidationAudit
    {
        private const int AuditIntervalTicks = 60;
        private static int _nextAuditTick;
        private static int _lastSignature;
        private static bool _hasSignature;

        internal static void Poll(PawnTable table, IWorkTabLayoutController layout)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (ticks < _nextAuditTick)
            {
                return;
            }

            _nextAuditTick = ticks + AuditIntervalTicks;
            int signature = ComputeSignature(table, layout);
            if (_hasSignature && signature != _lastSignature)
            {
                WorkTabInvalidationHub.InvalidateCategory(WorkGridInvalidationCategory.All);
            }
            _lastSignature = signature;
            _hasSignature = true;
        }

        internal static void Reset()
        {
            _nextAuditTick = 0;
            _lastSignature = 0;
            _hasSignature = false;
        }

        private static int ComputeSignature(PawnTable table, IWorkTabLayoutController layout)
        {
            unchecked
            {
                int hash = layout?.LayoutRevision ?? 0;
                hash = (hash * 397) ^ Prefs.UIScale.GetHashCode();
                hash = (hash * 397) ^
                    (LanguageDatabase.activeLanguage?.folderName?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ TimePriorityService.CurrentVersion;
                hash = (hash * 397) ^ WorkGiverReassignmentManager.CurrentSyncVersion;
                hash = (hash * 397) ^ TimePriorityService.ComputePresentationAuditSignature();
                hash = (hash * 397) ^ WorkGiverReassignmentManager.ComputePresentationAuditSignature();
                if (table?.cachedPawns == null || table.Columns == null)
                {
                    return hash;
                }

                for (int pawnIndex = 0; pawnIndex < table.cachedPawns.Count; pawnIndex++)
                {
                    Pawn pawn = table.cachedPawns[pawnIndex];
                    hash = (hash * 397) ^ (pawn?.thingIDNumber ?? 0);
                    if (pawn?.workSettings?.priorities == null)
                    {
                        continue;
                    }

                    for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                    {
                        WorkTypeDef workType = table.Columns[columnIndex]?.workType;
                        if (workType != null)
                        {
                            int priority = pawn.workSettings.priorities[workType];
                            hash = (hash * 397) ^ workType.shortHash;
                            hash = (hash * 397) ^ priority;
                            if (pawn.skills != null)
                            {
                                hash = (hash * 397) ^
                                    (int)(pawn.skills.AverageOfRelevantSkillsFor(workType) * 10f);
                                hash = (hash * 397) ^
                                    (int)pawn.skills.MaxPassionOfRelevantSkillsFor(workType);
                            }
                        }
                    }
                }
                return hash;
            }
        }
    }
}
