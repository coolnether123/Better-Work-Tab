using System;
using System.Collections.Generic;
using System.Globalization;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads.V2;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Workloads
{
    // The one Workloads-to-neutral translation, refreshed only at a draft/pass boundary.
    internal sealed class WorkloadParentPriorityProjection : IParentPriorityProjection
    {
        private readonly WorkloadDraft _draft;
        private readonly WorkloadOwnershipDimensions _ownership;
        private readonly HashSet<int> _editablePawnIds;
        private readonly Func<long> _sourceRevision;
        private ParentPriorityProjectionSnapshot _snapshot;
        private long _observedSourceRevision = long.MinValue, _revision;

        internal WorkloadParentPriorityProjection(WorkloadDraft draft, WorkloadOwnershipDimensions ownership,
            IEnumerable<PawnKey> editablePawns, Func<long> sourceRevision)
        {
            _draft = draft ?? new WorkloadDraft(WorkloadProjectedState.Empty);
            _ownership = ownership;
            _editablePawnIds = BuildEditablePawnIds(editablePawns);
            _sourceRevision = sourceRevision;
        }

        public long Revision { get { EnsureFresh(); return _revision; } }
        public ParentPriorityProjectionSnapshot Capture()
        {
            EnsureFresh();
            return _snapshot ?? ParentPriorityProjectionSnapshot.Empty;
        }

        internal bool TrySet(Pawn pawn, WorkTypeDef workType, int priority, out string reason)
        {
            ParentPriorityTarget target = ParentPriorityRead.TargetFor(pawn, workType);
            if (!_ownership.Owns(WorkloadStateDimension.ParentPriorities))
                reason = "The active workload preview does not own parent priorities.";
            else if (!target.IsValid || !_editablePawnIds.Contains(target.PawnThingId))
                reason = "The pawn is outside the active workload scope or is excluded for this preview.";
            else
            {
                _draft.SetParentPriority(new WorkloadParentPriorityKey(
                    new PawnKey(target.PawnThingId.ToString(CultureInfo.InvariantCulture)),
                    new WorkTypeKey(target.WorkTypeDefName)), priority);
                _observedSourceRevision = long.MinValue;
                reason = null;
                return true;
            }
            return false;
        }

        private void EnsureFresh()
        {
            long revision = _sourceRevision?.Invoke() ?? 0L;
            if (_snapshot != null && revision == _observedSourceRevision) return;
            _observedSourceRevision = revision;
            Rebuild();
        }

        private void Rebuild()
        {
            WorkloadProjectedState state = _draft.ProjectedState ?? WorkloadProjectedState.Empty;
            var priorities = new Dictionary<ParentPriorityTarget, ParentProjectionValue<int>>();
            var schedules = new Dictionary<ParentPriorityTarget, ParentPriorityScheduleOverlay>();
            var manualModes = new Dictionary<ParentPriorityTarget, ParentProjectionValue<bool>>();
            if (_ownership.Owns(WorkloadStateDimension.ParentPriorities)) AddPriorities(state, priorities);
            if (_ownership.Owns(WorkloadStateDimension.Schedules)) AddSchedules(state, schedules);
            ParentProjectionValue<bool> display = default(ParentProjectionValue<bool>);
            bool conflict = false;
            if (_ownership.Owns(WorkloadStateDimension.ManualModes))
            {
                Dictionary<WorkloadParentPriorityKey, WorkloadIntent<bool>> effective =
                    WorkloadManualModeSemantics.GetEffectiveEntries(state);
                AddManualModes(effective, manualModes);
                if (WorkloadManualModeSemantics.TryGetGlobalMode(
                        effective.Values, out bool mode, out _, out conflict))
                    display = ParentProjectionValue<bool>.Set(mode);
            }
            _snapshot = new ParentPriorityProjectionSnapshot(unchecked(++_revision), priorities, schedules,
                manualModes, display, conflict);
        }

        private void AddPriorities(WorkloadProjectedState state,
            IDictionary<ParentPriorityTarget, ParentProjectionValue<int>> values)
        {
            for (int i = 0; i < state.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = state.ParentPriorities[i];
                if (TryTarget(entry?.Key, out ParentPriorityTarget target))
                    values[target] = ParentProjectionValue<int>.Set(entry.Priority);
            }
            for (int i = 0; i < state.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry entry = state.ParentPriorityIntents[i];
                if (!TryTarget(entry?.Key, out ParentPriorityTarget target)) continue;
                WorkloadIntent<WorkloadSpecificPriorityPayload> intent = entry.Intent;
                Set(values, target, intent.HasValue, intent.HasValue ? intent.Value.Priority : 0, intent.IsClear);
            }
        }

        private void AddManualModes(
            IDictionary<WorkloadParentPriorityKey, WorkloadIntent<bool>> effective,
            IDictionary<ParentPriorityTarget, ParentProjectionValue<bool>> values)
        {
            foreach (KeyValuePair<WorkloadParentPriorityKey, WorkloadIntent<bool>> entry in effective)
            {
                if (!TryTarget(entry.Key, out ParentPriorityTarget target)) continue;
                if (entry.Value.HasValue) values[target] = ParentProjectionValue<bool>.Set(entry.Value.Value);
                else if (entry.Value.IsClear) values[target] = ParentProjectionValue<bool>.Clear;
            }
        }

        private void AddSchedules(WorkloadProjectedState state,
            IDictionary<ParentPriorityTarget, ParentPriorityScheduleOverlay> values)
        {
            for (int i = 0; i < state.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                if (!TryTarget(entry?.Key, out ParentPriorityTarget target)) continue;
                WorkloadIntent<WorkloadSchedulePayload> intent = entry.Intent;
                if (intent.IsClear) values[target] = ParentPriorityScheduleOverlay.Clear;
                else if (!intent.HasValue || intent.Value == null || !intent.Value.IsValid) values.Remove(target);
                else
                {
                    var priorities = new int[WorkloadSchedulePayload.HourCount];
                    for (int hour = 0; hour < priorities.Length; hour++) priorities[hour] = intent.Value.PriorityAt(hour);
                    values[target] = new ParentPriorityScheduleOverlay(
                        ParentPriorityOverlayState.Set, intent.Value.PinnedHourMask, priorities);
                }
            }
        }

        private static void Set<T>(IDictionary<ParentPriorityTarget, ParentProjectionValue<T>> values,
            ParentPriorityTarget target, bool hasValue, T value, bool clear)
        {
            if (hasValue) values[target] = ParentProjectionValue<T>.Set(value);
            else if (clear) values[target] = ParentProjectionValue<T>.Clear;
            else values.Remove(target);
        }

        private bool TryTarget(WorkloadParentPriorityKey key, out ParentPriorityTarget target) =>
            TryTarget(key?.Pawn, key?.WorkType, out target) && key.IsValid;

        private bool TryTarget(WorkloadScheduleTargetKey key, out ParentPriorityTarget target) =>
            TryTarget(key?.Pawn, key?.WorkType, out target) && key.IsValid && !key.IsGlobal &&
            key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType;
        private bool TryTarget(PawnKey pawn, WorkTypeKey workType, out ParentPriorityTarget target)
        {
            target = default(ParentPriorityTarget);
            return pawn != null && workType != null && int.TryParse(pawn.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int pawnId) && pawnId > 0 && _editablePawnIds.Contains(pawnId) &&
                (target = new ParentPriorityTarget(pawnId, workType.Value)).IsValid;
        }

        private static HashSet<int> BuildEditablePawnIds(IEnumerable<PawnKey> source)
        {
            var result = new HashSet<int>();
            if (source != null) foreach (PawnKey key in source)
                if (key != null && int.TryParse(key.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int pawnId) && pawnId > 0) result.Add(pawnId);
            return result;
        }
    }
}
