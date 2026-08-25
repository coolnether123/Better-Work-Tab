using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.WorkGiverReassignments;
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
        private static WorkGridRevisionSet _lastTrackedRevisions;

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
            if (TimePriorityService.ReconcileDirectMutationsFromAudit())
            {
                WorkTabApplication.Current?.ReportObservedScheduleChange();
            }
            int signature = ComputeSignature(table);
            WorkGridRevisionSet revisions = WorkTabInvalidationHub.Current.CategoryRevisions;
            bool knownTrackedChange =
                _lastTrackedRevisions.GameState != revisions.GameState ||
                _lastTrackedRevisions.PawnListOrder != revisions.PawnListOrder ||
                _lastTrackedRevisions.ColumnLayout != revisions.ColumnLayout ||
                _lastTrackedRevisions.Priority != revisions.Priority ||
                _lastTrackedRevisions.CapabilitySkill != revisions.CapabilitySkill ||
                _lastTrackedRevisions.ScheduleHour != revisions.ScheduleHour ||
                _lastTrackedRevisions.SubWorkOverride != revisions.SubWorkOverride ||
                _lastTrackedRevisions.SettingsThemeLanguageScale != revisions.SettingsThemeLanguageScale;
            if (_hasSignature &&
                signature != _lastSignature &&
                !knownTrackedChange)
            {
                WorkTabInvalidationHub.InvalidateCategory(WorkGridInvalidationCategory.All);
                revisions = WorkTabInvalidationHub.Current.CategoryRevisions;
            }
            _lastSignature = signature;
            _lastTrackedRevisions = revisions;
            _hasSignature = true;
        }

        internal static void Reset()
        {
            _nextAuditTick = 0;
            _nextRosterAuditTick = 0;
            _lastSignature = 0;
            _hasSignature = false;
            _lastTrackedRevisions = default;
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
                hash = (hash * 397) ^ WorkGiverReassignmentManager.ComputePresentationAuditSignature();
                if (table?.cachedPawns == null || table.Columns == null)
                {
                    return hash;
                }

                var relevantWorkTypes = new System.Collections.Generic.List<WorkTypeDef>(
                    table.Columns.Count);
                var seenWorkTypes = new System.Collections.Generic.HashSet<WorkTypeDef>();
                for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                {
                    WorkTypeDef workType = table.Columns[columnIndex]?.workType;
                    if (workType != null && seenWorkTypes.Add(workType))
                    {
                        relevantWorkTypes.Add(workType);
                    }
                }

                for (int pawnIndex = 0; pawnIndex < table.cachedPawns.Count; pawnIndex++)
                {
                    Pawn pawn = table.cachedPawns[pawnIndex];
                    hash = (hash * 397) ^ (pawn?.thingIDNumber ?? 0);
                    if (pawn == null)
                    {
                        continue;
                    }

                    if (pawn.workSettings?.priorities != null)
                    {
                        for (int workTypeIndex = 0;
                             workTypeIndex < relevantWorkTypes.Count;
                             workTypeIndex++)
                        {
                            WorkTypeDef workType = relevantWorkTypes[workTypeIndex];
                            int priority = pawn.workSettings.priorities[workType];
                            hash = (hash * 397) ^ workType.shortHash;
                            hash = (hash * 397) ^ priority;
                        }
                    }

                    if (pawn.skills?.skills == null)
                    {
                        continue;
                    }

                    for (int skillIndex = 0;
                         skillIndex < pawn.skills.skills.Count;
                         skillIndex++)
                    {
                        SkillRecord skill = pawn.skills.skills[skillIndex];
                        if (skill?.def == null)
                        {
                            continue;
                        }

                        hash = (hash * 397) ^ skill.def.shortHash;
                        hash = (hash * 397) ^ skill.levelInt;
                        hash = (hash * 397) ^ (int)skill.passion;
                    }
                }
                return hash;
            }
        }
    }
}
