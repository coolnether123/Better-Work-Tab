using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;

namespace Better_Work_Tab.Features.Application
{
    [Flags]
    internal enum WorkTabApplicationEffects
    {
        None = 0,
        Persistence = 1 << 0,
        PriorityInvalidation = 1 << 1,
        ScheduleHourInvalidation = 1 << 2,
        PresentationInvalidation = 1 << 3,
        SubWorkInvalidation = 1 << 4,
        ColumnLayoutInvalidation = 1 << 5,
        HeaderTextInvalidation = 1 << 6,
        HeaderGeometryInvalidation = 1 << 7,
        RenderResourceInvalidation = 1 << 8,
        ThemeSettingsInvalidation = 1 << 9,
        ExecutionRecache = 1 << 10,
        RowDescriptorRecache = 1 << 11,
        PawnTableRecache = 1 << 12,
        ExternalMirror = 1 << 13,
        SparseParentPriorityInvalidation = 1 << 14
    }

    internal readonly struct WorkTabRevisionVector
    {
        internal WorkTabRevisionVector(
            WorkTabApplicationRevision application,
            long authority,
            long schedule,
            long specificJob,
            long settings)
        {
            Application = application;
            Authority = authority;
            Schedule = schedule;
            SpecificJob = specificJob;
            Settings = settings;
        }

        internal WorkTabApplicationRevision Application { get; }
        internal long Authority { get; }
        internal long Schedule { get; }
        internal long SpecificJob { get; }
        internal long Settings { get; }

        internal static WorkTabRevisionVector FromApplication(
            WorkTabApplicationRevision application) =>
            new WorkTabRevisionVector(application, 0L, 0L, 0L, 0L);
    }

    internal readonly struct WorkTabApplicationTargetChange
    {
        internal WorkTabApplicationTargetChange(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions)
        {
            Target = target;
            Dimensions = dimensions;
        }

        internal TimePriorityTarget Target { get; }
        internal WorkTabApplicationDimensions Dimensions { get; }
    }

    internal readonly struct WorkTabApplicationChange
    {
        private static readonly WorkTabApplicationTargetChange[] NoTargets =
            new WorkTabApplicationTargetChange[0];
        private readonly IReadOnlyList<WorkTabApplicationTargetChange> _affectedTargets;

        internal WorkTabApplicationChange(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions,
            bool broadScope)
            : this(
                target,
                dimensions,
                broadScope,
                WorkTabApplicationEffects.None,
                default(WorkTabRevisionVector),
                default(WorkTabRevisionVector),
                null)
        {
        }

        internal WorkTabApplicationChange(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions,
            bool broadScope,
            WorkTabApplicationEffects effects,
            WorkTabRevisionVector beforeRevision,
            WorkTabRevisionVector afterRevision,
            IEnumerable<WorkTabApplicationTargetChange> affectedTargets)
        {
            Target = target;
            Dimensions = dimensions;
            BroadScope = broadScope;
            Effects = effects;
            BeforeRevisionVector = beforeRevision;
            AfterRevisionVector = afterRevision;
            _affectedTargets = CopyTargets(affectedTargets);
        }

        internal TimePriorityTarget Target { get; }
        internal WorkTabApplicationDimensions Dimensions { get; }
        internal bool BroadScope { get; }
        internal WorkTabApplicationEffects Effects { get; }
        internal WorkTabRevisionVector BeforeRevisionVector { get; }
        internal WorkTabRevisionVector AfterRevisionVector { get; }
        internal WorkTabApplicationRevision BeforeRevision => BeforeRevisionVector.Application;
        internal WorkTabApplicationRevision AfterRevision => AfterRevisionVector.Application;
        internal IReadOnlyList<WorkTabApplicationTargetChange> AffectedTargets =>
            _affectedTargets ?? NoTargets;
        internal bool HasPreciseTargets => !BroadScope && AffectedTargets.Count > 0;
        internal bool IsEmpty => Dimensions == WorkTabApplicationDimensions.None;

        private static IReadOnlyList<WorkTabApplicationTargetChange> CopyTargets(
            IEnumerable<WorkTabApplicationTargetChange> targets)
        {
            if (targets == null)
            {
                return NoTargets;
            }

            var copied = new List<WorkTabApplicationTargetChange>();
            foreach (WorkTabApplicationTargetChange target in targets)
            {
                copied.Add(target);
            }

            if (copied.Count == 0)
            {
                return NoTargets;
            }

            copied.Sort(CompareTargetChanges);
            var distinct = new List<WorkTabApplicationTargetChange>(copied.Count);
            for (int i = 0; i < copied.Count; i++)
            {
                if (i == 0 || CompareTargetChanges(copied[i - 1], copied[i]) != 0)
                {
                    distinct.Add(copied[i]);
                }
            }

            return distinct.ToArray();
        }

        private static int CompareTargetChanges(
            WorkTabApplicationTargetChange left,
            WorkTabApplicationTargetChange right)
        {
            TimePriorityCacheKey leftKey = left.Target.CacheKey;
            TimePriorityCacheKey rightKey = right.Target.CacheKey;
            int comparison = leftKey.PawnId.CompareTo(rightKey.PawnId);
            if (comparison != 0) return comparison;
            comparison = leftKey.Kind.CompareTo(rightKey.Kind);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(
                leftKey.WorkTypeDefName,
                rightKey.WorkTypeDefName);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(
                leftKey.TargetDefName,
                rightKey.TargetDefName);
            return comparison != 0
                ? comparison
                : left.Dimensions.CompareTo(right.Dimensions);
        }
    }
}
