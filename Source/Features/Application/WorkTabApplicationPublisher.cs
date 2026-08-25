using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal sealed class WorkTabApplicationPublisher
    {
        internal WorkTabApplicationChange Publish(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions,
            bool broadScope,
            WorkTabApplicationEffects effects,
            WorkTabRevisionVector beforeRevision,
            WorkTabRevisionVector afterRevision,
            IEnumerable<WorkTabApplicationTargetChange> affectedTargets)
        {
            var change = new WorkTabApplicationChange(
                target,
                dimensions,
                broadScope,
                effects,
                beforeRevision,
                afterRevision,
                affectedTargets);

            PublishInvalidation(change);
            PublishExecutionRefresh(change.Effects);
            if ((change.Effects & WorkTabApplicationEffects.PawnTableRecache) != 0)
            {
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
            if ((change.Effects & WorkTabApplicationEffects.ExternalMirror) != 0)
            {
                PublishExternalMirror(change);
            }

            return change;
        }

        internal void InvalidateTransient(WorkTabApplicationEffects effects)
        {
            var change = new WorkTabApplicationChange(
                default,
                WorkTabApplicationDimensions.Presentation,
                true,
                effects,
                default,
                default,
                null);
            PublishInvalidation(change);
            PublishExecutionRefresh(effects);
        }

        private static void PublishInvalidation(WorkTabApplicationChange change)
        {
            if (TryPublishSparseParentInvalidation(change))
            {
                return;
            }

            WorkTabDirtyFlags flags = WorkTabDirtyFlags.None;
            WorkTabApplicationEffects effects = change.Effects;
            if ((effects & WorkTabApplicationEffects.PriorityInvalidation) != 0)
                flags |= WorkTabDirtyFlags.Priority;
            if ((effects & WorkTabApplicationEffects.ScheduleHourInvalidation) != 0)
                flags |= WorkTabDirtyFlags.ScheduleHour;
            if ((effects & WorkTabApplicationEffects.PresentationInvalidation) != 0)
                flags |= WorkTabDirtyFlags.Presentation;
            if ((effects & WorkTabApplicationEffects.SubWorkInvalidation) != 0)
                flags |= WorkTabDirtyFlags.SubWorkOverride;
            if ((effects & WorkTabApplicationEffects.ColumnLayoutInvalidation) != 0)
                flags |= WorkTabDirtyFlags.Columns;
            if ((effects & WorkTabApplicationEffects.HeaderTextInvalidation) != 0)
                flags |= WorkTabDirtyFlags.HeaderText;
            if ((effects & WorkTabApplicationEffects.HeaderGeometryInvalidation) != 0)
                flags |= WorkTabDirtyFlags.HeaderGeometry;
            if ((effects & WorkTabApplicationEffects.RenderResourceInvalidation) != 0)
                flags |= WorkTabDirtyFlags.RenderResources;
            if ((effects & WorkTabApplicationEffects.ThemeSettingsInvalidation) != 0)
                flags |= WorkTabDirtyFlags.SettingsThemeLanguageScale;
            WorkTabInvalidationHub.Invalidate(flags);
        }

        private static bool TryPublishSparseParentInvalidation(
            WorkTabApplicationChange change)
        {
            if ((change.Effects & WorkTabApplicationEffects.SparseParentPriorityInvalidation) == 0 ||
                change.BroadScope ||
                change.Dimensions != WorkTabApplicationDimensions.ParentPriority ||
                change.AffectedTargets.Count == 0)
            {
                return false;
            }

            var keys = new List<WorkGridPriorityKey>(change.AffectedTargets.Count);
            var unique = new HashSet<WorkGridPriorityKey>();
            for (int i = 0; i < change.AffectedTargets.Count; i++)
            {
                WorkTabApplicationTargetChange affected = change.AffectedTargets[i];
                TimePriorityTarget affectedTarget = affected.Target;
                if (affected.Dimensions != WorkTabApplicationDimensions.ParentPriority ||
                    affectedTarget.IsGlobal ||
                    affectedTarget.Kind != TimePriorityTargetKind.WorkType)
                {
                    return false;
                }

                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(
                    affectedTarget.WorkTypeDefName);
                if (workType == null)
                {
                    return false;
                }

                var key = new WorkGridPriorityKey(
                    affectedTarget.PawnId,
                    workType.shortHash);
                if (unique.Add(key))
                {
                    keys.Add(key);
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                WorkTabInvalidationHub.InvalidatePriority(
                    keys[i].PawnId,
                    keys[i].WorkTypeId);
            }
            return keys.Count > 0;
        }

        private static void PublishExecutionRefresh(
            WorkTabApplicationEffects effects)
        {
            if ((effects & WorkTabApplicationEffects.RowDescriptorRecache) != 0 &&
                PawnOrganizerSystem.Instance?.Layout is WorkTabLayoutController layout)
            {
                layout.InvalidateRowDescriptors();
            }
            if ((effects & WorkTabApplicationEffects.ExecutionRecache) != 0)
            {
                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            }
        }

        private static void PublishExternalMirror(WorkTabApplicationChange change)
        {
            WorkTabApplicationDimensions dimensions = change.Dimensions;
            bool scheduleOnly = dimensions == WorkTabApplicationDimensions.Schedule;
            if (change.AffectedTargets.Count == 0)
            {
                TimePriorityService.NotifyExternalMirror(
                    change.Target,
                    change.BroadScope,
                    scheduleOnly);
                return;
            }

            var mirrored = new HashSet<TimePriorityCacheKey>();
            for (int i = 0; i < change.AffectedTargets.Count; i++)
            {
                TimePriorityTarget affectedTarget = change.AffectedTargets[i].Target;
                if (mirrored.Add(affectedTarget.CacheKey))
                {
                    TimePriorityService.NotifyExternalMirror(
                        affectedTarget,
                        false,
                        scheduleOnly);
                }
            }
        }
    }
}
