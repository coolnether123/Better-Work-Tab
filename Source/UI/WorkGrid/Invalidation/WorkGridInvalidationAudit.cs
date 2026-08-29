using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using RimWorld;
using Spine.Profiling;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Invalidation
{
    /// <summary>Low-frequency compatibility audit for external writers that bypass known hooks.</summary>
    internal static class WorkGridInvalidationAudit
    {
        private const int AuditIntervalTicks = 60;
        private const int PawnLabelAuditIntervalFrames = 60;
        private static int _nextAuditTick;
        private static int _nextRosterAuditTick;
        private static int _nextPawnLabelAuditFrame;
        private static int _lastSignature;
        private static int _lastPawnLabelSignature;
        private static bool _hasSignature;
        private static bool _hasPawnLabelSignature;
        private static WorkGridRevisionSet _lastTrackedRevisions;

        internal static void PollRoster(PawnTable table)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (ticks < _nextRosterAuditTick)
            {
                return;
            }

            _nextRosterAuditTick = ticks + AuditIntervalTicks;
            bool rosterMatches = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.InvalidationAudit.RosterSignature",
                    () => RosterSignature(PawnsFinder.AllMaps_FreeColonists) == RosterSignature(table?.cachedPawns))
                : RosterSignature(PawnsFinder.AllMaps_FreeColonists) == RosterSignature(table?.cachedPawns);
            if (rosterMatches)
            {
                return;
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.PawnListOrder);
        }

        internal static void Poll(PawnTable table)
        {
            PollPawnLabelSignature(table);

            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (ticks < _nextAuditTick)
            {
                return;
            }

            _nextAuditTick = ticks + AuditIntervalTicks;
            bool directMutationsFound = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.InvalidationAudit.Reconcile",
                    TimePriorityService.ReconcileDirectMutationsFromAudit)
                : TimePriorityService.ReconcileDirectMutationsFromAudit();
            if (directMutationsFound)
            {
                WorkTabApplication.Current?.ReportObservedScheduleChange();
            }
            int signature = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.InvalidationAudit.ComputeSignature",
                    () => ComputeSignature(table))
                : ComputeSignature(table);
            WorkGridRevisionSet revisions = WorkTabInvalidationHub.Current.CategoryRevisions;
            bool knownTrackedChange =
                _lastTrackedRevisions.GameState != revisions.GameState ||
                _lastTrackedRevisions.PawnListOrder != revisions.PawnListOrder ||
                _lastTrackedRevisions.ColumnLayout != revisions.ColumnLayout ||
                _lastTrackedRevisions.Priority != revisions.Priority ||
                _lastTrackedRevisions.PawnLabel != revisions.PawnLabel ||
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
            _nextPawnLabelAuditFrame = 0;
            _lastSignature = 0;
            _lastPawnLabelSignature = 0;
            _hasSignature = false;
            _hasPawnLabelSignature = false;
            _lastTrackedRevisions = default;
        }

        private static void PollPawnLabelSignature(PawnTable table)
        {
            // Game ticks stop while the player pauses, but labels can still
            // change through UI or compatibility code. Keep this backstop
            // frame-cadenced and well away from the snapshot admission path.
            int frame = UnityEngine.Time.frameCount;
            if (frame < _nextPawnLabelAuditFrame)
            {
                return;
            }

            _nextPawnLabelAuditFrame = frame + PawnLabelAuditIntervalFrames;
            int signature = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.InvalidationAudit.PawnLabelSignature",
                    () => ComputePawnLabelSignature(table))
                : ComputePawnLabelSignature(table);
            WorkGridRevisionSet revisions = WorkTabInvalidationHub.Current.CategoryRevisions;
            bool knownTrackedLabelChange =
                _lastTrackedRevisions.PawnLabel != revisions.PawnLabel;
            if (_hasPawnLabelSignature &&
                signature != _lastPawnLabelSignature &&
                !knownTrackedLabelChange)
            {
                WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.PawnLabel);
                revisions = WorkTabInvalidationHub.Current.CategoryRevisions;
            }

            _lastPawnLabelSignature = signature;
            _lastTrackedRevisions = revisions;
            _hasPawnLabelSignature = true;
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

        private static int ComputePawnLabelSignature(PawnTable table)
        {
            unchecked
            {
                // These are the inputs used by the native label worker and
                // BWT's contrast/name-color adapter. This full scan belongs
                // only to the slow external-writer audit.
                int hash = (17 * 397) ^ PawnColorDatabase.Version;
                if (table?.Columns != null)
                {
                    for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                    {
                        PawnColumnWorker_Label labelWorker =
                            table.Columns[columnIndex]?.Worker as PawnColumnWorker_Label;
                        if (labelWorker != null)
                        {
                            hash = (hash * 397) ^
                                (labelWorker.def?.useLabelShort == true ? 1 : 0);
                            break;
                        }
                    }
                }

                if (table?.cachedPawns == null)
                {
                    return hash;
                }

                for (int index = 0; index < table.cachedPawns.Count; index++)
                {
                    Pawn pawn = table.cachedPawns[index];
                    hash = (hash * 397) ^ (pawn?.thingIDNumber ?? 0);
                    if (pawn == null)
                    {
                        continue;
                    }

                    hash = (hash * 397) ^ (pawn.Name?.ToStringShort?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.story?.Title?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.KindLabel?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (pawn.IsSlave ? 1 : 0);
                    hash = (hash * 397) ^ (pawn.IsColonyMech ? 1 : 0);
                    if (pawn.IsSlave || pawn.IsColonyMech)
                    {
                        hash = (hash * 397) ^
                            PawnNameColorUtility.PawnNameColorOf(pawn).GetHashCode();
                    }

                    hash = (hash * 397) ^ (pawn.IsSubhuman ? 1 : 0);
                    hash = (hash * 397) ^ (pawn.mutant?.HasTurned == true ? 1 : 0);
                }

                return hash;
            }
        }
    }
}
