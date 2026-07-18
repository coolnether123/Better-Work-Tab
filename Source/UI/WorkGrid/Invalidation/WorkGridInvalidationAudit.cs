using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Invalidation
{
    /// <summary>Low-frequency compatibility audit for external writers that bypass known hooks.</summary>
    internal static class WorkGridInvalidationAudit
    {
        private const int AuditIntervalTicks = 60;
        private static int _nextAuditTick;
        private static int _nextRosterAuditTick;
        private static int _lastSignature;
        private static bool _hasSignature;

        internal static void PollRoster(PawnTable table)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (ticks < _nextRosterAuditTick)
            {
                return;
            }

            _nextRosterAuditTick = ticks + AuditIntervalTicks;
            if (RosterSignature(PawnsFinder.AllMaps_FreeColonists) == RosterSignature(table?.cachedPawns))
            {
                return;
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.PawnListOrder);
        }

        internal static void Poll(PawnTable table)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (ticks < _nextAuditTick)
            {
                return;
            }

            _nextAuditTick = ticks + AuditIntervalTicks;
            int signature = ComputeSignature(table);
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
            _nextRosterAuditTick = 0;
            _lastSignature = 0;
            _hasSignature = false;
        }

        private static int RosterSignature(System.Collections.Generic.IEnumerable<Pawn> pawns)
        {
            unchecked
            {
                int count = 0;
                int sum = 0;
                int xor = 0;
                if (pawns != null)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        int id = pawn?.thingIDNumber ?? 0;
                        count++;
                        sum += id;
                        xor ^= id;
                    }
                }

                return ((count * 397) ^ sum) * 397 ^ xor;
            }
        }

        private static int ComputeSignature(PawnTable table)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ Prefs.UIScale.GetHashCode();
                hash = (hash * 397) ^
                    (LanguageDatabase.activeLanguage?.folderName?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ TimePriorityService.CurrentVersion;
                hash = (hash * 397) ^ WorkGiverReassignmentManager.CurrentSyncVersion;
                hash = (hash * 397) ^ TimePriorityService.ComputePresentationAuditSignature();
                hash = (hash * 397) ^ WorkGiverReassignmentManager.ComputePresentationAuditSignature();
                var columns = HeaderUtility.GetTableColumns(table);
                if (table?.cachedPawns == null || columns == null)
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

                    for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                    {
                        WorkTypeDef workType = columns[columnIndex]?.workType;
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
