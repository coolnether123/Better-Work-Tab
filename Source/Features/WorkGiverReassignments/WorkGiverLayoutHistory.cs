using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Foundation.GameState;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    internal sealed class WorkGiverLayoutOrderSnapshot
    {
        internal WorkGiverLayoutOrderSnapshot(
            WorkGiverReassignmentManager.ExactGlobalStateKind state,
            IEnumerable<string> storedOrder,
            IEnumerable<string> effectiveOrder)
        {
            State = state;
            StoredOrder = (storedOrder ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
            EffectiveOrder = (effectiveOrder ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
        }

        internal WorkGiverReassignmentManager.ExactGlobalStateKind State { get; }
        internal IReadOnlyList<string> StoredOrder { get; }
        internal IReadOnlyList<string> EffectiveOrder { get; }
    }

    internal sealed class WorkGiverLayoutPawnOrderSnapshot
    {
        internal WorkGiverLayoutPawnOrderSnapshot(
            int pawnId,
            string workTypeDefName,
            bool hadOrder,
            IEnumerable<string> order)
        {
            PawnId = pawnId;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            HadOrder = hadOrder;
            Order = (order ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
        }

        internal int PawnId { get; }
        internal string WorkTypeDefName { get; }
        internal bool HadOrder { get; }
        internal IReadOnlyList<string> Order { get; }
    }

    internal sealed class WorkGiverLayoutSnapshot
    {
        internal WorkGiverLayoutSnapshot(
            string targetWorkTypeDefName,
            IDictionary<string, WorkGiverLayoutOrderSnapshot> orders,
            IEnumerable<WorkGiverLayoutPawnOrderSnapshot> pawnOrders)
        {
            TargetWorkTypeDefName = targetWorkTypeDefName;
            Orders = orders.ToDictionary(
                pair => pair.Key,
                pair => new WorkGiverLayoutOrderSnapshot(
                    pair.Value.State,
                    pair.Value.StoredOrder,
                    pair.Value.EffectiveOrder),
                StringComparer.Ordinal);
            PawnOrders = (pawnOrders ?? Enumerable.Empty<WorkGiverLayoutPawnOrderSnapshot>())
                .Select(order => new WorkGiverLayoutPawnOrderSnapshot(
                    order.PawnId,
                    order.WorkTypeDefName,
                    order.HadOrder,
                    order.Order))
                .OrderBy(order => order.PawnId)
                .ThenBy(order => order.WorkTypeDefName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        internal string TargetWorkTypeDefName { get; }
        internal IReadOnlyDictionary<string, WorkGiverLayoutOrderSnapshot> Orders { get; }
        internal IReadOnlyList<WorkGiverLayoutPawnOrderSnapshot> PawnOrders { get; }
    }

    internal sealed class WorkGiverLayoutCommand
    {
        internal WorkGiverLayoutCommand(long id, string workGiverDefName,
            WorkGiverLayoutSnapshot before, WorkGiverLayoutSnapshot after)
        {
            Id = id;
            WorkGiverDefName = workGiverDefName;
            Before = before;
            After = after;
        }

        internal long Id { get; }
        internal string WorkGiverDefName { get; }
        internal WorkGiverLayoutSnapshot Before { get; }
        internal WorkGiverLayoutSnapshot After { get; }
    }

    internal static class WorkGiverLayoutHistory
    {
        private static readonly BoundedUndoRedoHistory<WorkGiverLayoutCommand> Commands =
            new BoundedUndoRedoHistory<WorkGiverLayoutCommand>(64);
        private static long _nextId;

        internal static long NextId() => ++_nextId;

        internal static void RecordForward(WorkGiverLayoutCommand command)
        {
            if (command == null) return;
            _nextId = Math.Max(_nextId, command.Id);
            Commands.Record(command);
        }

        internal static WorkGiverLayoutCommand PeekUndo() => Commands.PeekUndo;
        internal static WorkGiverLayoutCommand PeekRedo() => Commands.PeekRedo;

        internal static void CompleteUndo(WorkGiverLayoutCommand command)
        {
            if (command == null) return;
            _nextId = Math.Max(_nextId, command.Id);
            if (!Commands.CompleteUndo(candidate => candidate.Id == command.Id))
            {
                Commands.Restore(null, new[] { command });
            }
        }

        internal static void CompleteRedo(WorkGiverLayoutCommand command)
        {
            if (command == null) return;
            _nextId = Math.Max(_nextId, command.Id);
            if (!Commands.CompleteRedo(candidate => candidate.Id == command.Id))
            {
                Commands.Restore(new[] { command }, null);
            }
        }

        internal static void Clear() => Commands.Clear();
    }

    internal static partial class WorkGiverReassignmentManager
    {
        internal static bool TryMoveWorkGiverLayout(string workGiverDefName, string targetWorkTypeDefName,
            int insertIndex, out string errorMessage)
        {
            if (!TryCreateLayoutCommand(workGiverDefName, targetWorkTypeDefName, insertIndex, out var command, out errorMessage))
                return false;

            return SubmitLayoutCommand(command, command.After, 0);
        }

        internal static bool TryRestoreWorkGiverToBaseline(string workGiverDefName, out string errorMessage)
        {
            var giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (giver?.workType == null)
            {
                errorMessage = "Work giver or original work type was not found.";
                return false;
            }

            int index = CalculateOriginalBaselineTargetIndex(giver);
            return TryMoveWorkGiverLayout(giver.defName, giver.workType.defName, index, out errorMessage);
        }

        internal static bool TryUndoWorkGiverLayout()
        {
            var command = WorkGiverLayoutHistory.PeekUndo();
            if (command == null) return false;
            return SubmitLayoutCommand(command, command.Before, 1);
        }

        internal static bool TryRedoWorkGiverLayout()
        {
            var command = WorkGiverLayoutHistory.PeekRedo();
            if (command == null) return false;
            return SubmitLayoutCommand(command, command.After, 2);
        }

        private static bool SubmitLayoutCommand(
            WorkGiverLayoutCommand command, WorkGiverLayoutSnapshot snapshot, int historyAction) =>
            WorkTabGameRoots.For(Current.Game)?.Application?
                .SubmitSpecificLayout(command, snapshot, historyAction).Accepted == true;

        internal static bool ApplySpecificLayout(long commandId, string workGiverDefName, int expectedVersion,
            string expectedTargetWorkTypeDefName, List<string> encodedExpectedOrders,
            string targetWorkTypeDefName, List<string> encodedOrders, int historyAction,
            WorkGiverLayoutCommand localCommand, out bool schedulesChanged)
        {
            schedulesChanged = false;
            var data = Data;
            var giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (data == null || giver == null) return false;
            if (expectedVersion != CurrentSyncVersion)
            {
                WorkGiverLayoutHistory.Clear();
                return false;
            }

            WorkGiverLayoutSnapshot expected = DecodeSnapshot(
                expectedTargetWorkTypeDefName, encodedExpectedOrders);
            WorkGiverLayoutSnapshot desired = DecodeSnapshot(
                targetWorkTypeDefName, encodedOrders);
            if (expected == null || desired == null ||
                !MatchesLayoutSnapshot(giver, expected))
            {
                WorkGiverLayoutHistory.Clear();
                return false;
            }

            WorkTypeDef targetWorkType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            WorkTypeDef sourceWorkType = GetTargetWorkType(giver);
            if (targetWorkType == null ||
                !TimePriorityService.TryPrepareWorkGiverScheduleRetarget(
                    giver, sourceWorkType, targetWorkType, out TimePriorityService.WorkGiverScheduleRetargetPlan schedulePlan, out _))
            {
                return false;
            }

            WorkGiverLayoutCommand command = localCommand ??
                new WorkGiverLayoutCommand(
                    commandId,
                    workGiverDefName,
                    historyAction == 1 ? desired : expected,
                    historyAction == 1 ? expected : desired);

            WorkGiverReassignmentData previousData = data.Clone();
            WorkGiverReassignmentData stagedData = previousData.Clone();
            bool layoutWriteStarted = false;
            bool scheduleRetargetApplied = false;
            bool scheduleBatchCommitted = false;
            bool transactionCompleted = false;
            IDisposable scheduleBatch = null;
            try
            {
                stagedData.EnsureCollections();
                if (targetWorkTypeDefName == giver.workType?.defName)
                    stagedData.WorkGiverToWorkTypeMap.Remove(giver.defName);
                else
                    stagedData.WorkGiverToWorkTypeMap[giver.defName] = targetWorkTypeDefName;

                foreach (var pair in desired.Orders)
                {
                    ApplyGlobalWorkTypeOrderState(
                        stagedData,
                        pair.Key,
                        pair.Value.State,
                        pair.Value.StoredOrder,
                        advanceRevision: false);
                }

                ApplyPawnOrderStates(stagedData, desired.PawnOrders);
                foreach (var moved in stagedData.PlayerMovedWorkGiversByWorkType.Values)
                    moved?.Remove(giver.defName);
                stagedData.SyncVersion++;

                if (schedulePlan.Changed)
                {
                    if (TimePriorityService.HasActiveMutationBatch)
                    {
                        return false;
                    }

                    scheduleBatch = TimePriorityService.BeginMutationBatch();
                    if (!TimePriorityService.CommitWorkGiverScheduleRetarget(schedulePlan))
                    {
                        return false;
                    }

                    scheduleRetargetApplied = true;
                }

                layoutWriteStarted = true;
                data.CopyFrom(stagedData, stagedData.SyncVersion);
                InvalidateCaches();

                if (targetWorkType != null &&
                    (targetWorkType != giver.workType ||
                     IsWorkGiverOutOfBaselinePosition(targetWorkType, giver)))
                {
                    RecordPlayerMovedWorkGiver(targetWorkType, giver);
                }

                if (scheduleBatch != null)
                {
                    scheduleBatch.Dispose();
                    scheduleBatch = null;
                    schedulesChanged = TimePriorityService.CommitMutationBatch();
                    if (!schedulesChanged)
                    {
                        return false;
                    }

                    scheduleBatchCommitted = true;
                }

                if (historyAction == 0) WorkGiverLayoutHistory.RecordForward(command);
                else if (historyAction == 1) WorkGiverLayoutHistory.CompleteUndo(command);
                else WorkGiverLayoutHistory.CompleteRedo(command);

                transactionCompleted = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (!transactionCompleted)
                {
                    if (scheduleRetargetApplied)
                    {
                        RestoreScheduleRetargetAfterFailedLayout(
                            schedulePlan,
                            ref scheduleBatch,
                            scheduleBatchCommitted);
                    }
                    else
                    {
                        scheduleBatch?.Dispose();
                    }

                    if (layoutWriteStarted)
                    {
                        data.CopyFrom(previousData, previousData.SyncVersion);
                        InvalidateCaches();
                    }

                    schedulesChanged = false;
                }
            }
        }

        private static void RestoreScheduleRetargetAfterFailedLayout(
            TimePriorityService.WorkGiverScheduleRetargetPlan schedulePlan,
            ref IDisposable scheduleBatch,
            bool scheduleBatchCommitted)
        {
            if (scheduleBatch != null)
            {
                try
                {
                    TimePriorityService.RestoreUnpublishedWorkGiverScheduleRetarget(schedulePlan);
                }
                finally
                {
                    scheduleBatch.Dispose();
                    scheduleBatch = null;
                    TimePriorityService.DiscardMutationBatch();
                }

                return;
            }

            try
            {
                using (IDisposable rollbackBatch = TimePriorityService.BeginMutationBatch())
                {
                    TimePriorityService.RestoreUnpublishedWorkGiverScheduleRetarget(schedulePlan);
                }
            }
            finally
            {
                if (scheduleBatchCommitted)
                {
                    TimePriorityService.CommitMutationBatch();
                }
                else
                {
                    TimePriorityService.DiscardMutationBatch();
                }
            }
        }

        private static bool TryCreateLayoutCommand(string giverName, string targetName, int insertIndex,
            out WorkGiverLayoutCommand command, out string error)
        {
            command = null;
            error = null;
            var giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverName);
            var target = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetName);
            if (giver == null || target == null)
            {
                error = "Work giver or target work type was not found.";
                return false;
            }

            var source = GetTargetWorkType(giver) ?? giver.workType;
            if (!TimePriorityService.TryPrepareWorkGiverScheduleRetarget(
                    giver, source, target, out _, out error))
            {
                return false;
            }
            var affected = new HashSet<string>(StringComparer.Ordinal) { target.defName };
            if (source != null) affected.Add(source.defName);
            var before = CaptureSnapshot(giver, affected);
            var afterOrders = before.Orders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.EffectiveOrder.ToList(),
                StringComparer.Ordinal);
            foreach (var list in afterOrders.Values) list.Remove(giver.defName);
            var targetOrder = afterOrders[target.defName];
            targetOrder.Insert(Math.Max(0, Math.Min(insertIndex, targetOrder.Count)), giver.defName);
            var after = new WorkGiverLayoutSnapshot(
                target.defName,
                afterOrders.ToDictionary(
                    pair => pair.Key,
                    pair => new WorkGiverLayoutOrderSnapshot(
                        ExactGlobalStateKind.Set,
                        pair.Value,
                        pair.Value),
                    StringComparer.Ordinal),
                before.PawnOrders.Select(order =>
                    new WorkGiverLayoutPawnOrderSnapshot(
                        order.PawnId,
                        order.WorkTypeDefName,
                        order.HadOrder,
                        order.Order.Where(name => name != giver.defName))));
            if (SnapshotsEqual(before, after))
                return false;
            command = new WorkGiverLayoutCommand(WorkGiverLayoutHistory.NextId(), giver.defName, before, after);
            return true;
        }

        private static WorkGiverLayoutSnapshot CaptureSnapshot(
            WorkGiverDef giver,
            IEnumerable<string> workTypes,
            IEnumerable<WorkGiverLayoutPawnOrderSnapshot> pawnFootprint = null)
        {
            var orders = new Dictionary<string, WorkGiverLayoutOrderSnapshot>(StringComparer.Ordinal);
            foreach (string name in workTypes.Distinct())
            {
                var type = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                GlobalWorkTypeOrderSnapshot exact = CaptureGlobalWorkTypeOrderSnapshot(name);
                List<string> effective = type == null
                    ? new List<string>()
                    : GetDisplayWorkGiversForWorkType(type)
                        .Where(worker => worker?.def != null)
                        .Select(worker => worker.def.defName)
                        .ToList();
                orders[name] = new WorkGiverLayoutOrderSnapshot(
                    exact.State,
                    exact.OrderedWorkGiverNames,
                    effective);
            }
            var pawnOrders = new List<WorkGiverLayoutPawnOrderSnapshot>();
            WorkGiverReassignmentData data = ExistingData;
            var capturedPawnOrders = new HashSet<string>(StringComparer.Ordinal);
            if (pawnFootprint != null)
            {
                foreach (WorkGiverLayoutPawnOrderSnapshot key in pawnFootprint)
                {
                    List<string> order = null;
                    bool hadOrder = data?.PawnWorkGiverOrdering != null &&
                        data.PawnWorkGiverOrdering.TryGetValue(key.PawnId, out var pawn) &&
                        pawn != null &&
                        pawn.TryGetValue(key.WorkTypeDefName, out order);
                    pawnOrders.Add(new WorkGiverLayoutPawnOrderSnapshot(
                        key.PawnId,
                        key.WorkTypeDefName,
                        hadOrder,
                        hadOrder ? order : null));
                    capturedPawnOrders.Add(key.PawnId + "\u001f" + key.WorkTypeDefName);
                }
            }

            if (data?.PawnWorkGiverOrdering != null)
            {
                foreach (var pawn in data.PawnWorkGiverOrdering)
                {
                    if (pawn.Value == null) continue;
                    foreach (var order in pawn.Value)
                    {
                        string key = pawn.Key + "\u001f" + order.Key;
                        if (order.Value?.Contains(giver.defName) == true &&
                            capturedPawnOrders.Add(key))
                        {
                            pawnOrders.Add(new WorkGiverLayoutPawnOrderSnapshot(
                                pawn.Key,
                                order.Key,
                                true,
                                order.Value));
                        }
                    }
                }
            }
            return new WorkGiverLayoutSnapshot(
                (GetTargetWorkType(giver) ?? giver.workType)?.defName,
                orders,
                pawnOrders);
        }

        private static int CalculateOriginalBaselineTargetIndex(WorkGiverDef giver)
        {
            var baseline = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(d => d.workType == giver.workType).OrderByDescending(d => d.priorityInType).Select(d => d.defName).ToList();
            int giverBaseline = baseline.IndexOf(giver.defName);
            if (giverBaseline < 0) return 0;
            int index = 0;
            foreach (var worker in GetDisplayWorkGiversForWorkType(giver.workType))
            {
                string name = worker?.def?.defName;
                int other = baseline.IndexOf(name);
                if (name != giver.defName && other >= 0 && other < giverBaseline) index++;
            }
            return index;
        }

        internal static List<string> EncodeSnapshot(WorkGiverLayoutSnapshot snapshot)
        {
            var result = new List<string> { snapshot.Orders.Count.ToString() };
            foreach (var pair in snapshot.Orders.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                result.Add(pair.Key);
                result.Add(((int)pair.Value.State).ToString());
                result.Add(pair.Value.StoredOrder.Count.ToString());
                result.AddRange(pair.Value.StoredOrder);
                result.Add(pair.Value.EffectiveOrder.Count.ToString());
                result.AddRange(pair.Value.EffectiveOrder);
            }
            result.Add(snapshot.PawnOrders.Count.ToString());
            foreach (WorkGiverLayoutPawnOrderSnapshot order in snapshot.PawnOrders)
            {
                result.Add(order.PawnId.ToString());
                result.Add(order.WorkTypeDefName);
                result.Add(order.HadOrder ? "1" : "0");
                result.Add(order.Order.Count.ToString());
                result.AddRange(order.Order);
            }
            return result;
        }

        private static WorkGiverLayoutSnapshot DecodeSnapshot(string targetWorkTypeDefName, List<string> encoded)
        {
            var result = new Dictionary<string, WorkGiverLayoutOrderSnapshot>(StringComparer.Ordinal);
            int i = 0;
            if (encoded == null || encoded.Count == 0 ||
                !int.TryParse(encoded[i++], out int globalCount) || globalCount <= 0)
            {
                return null;
            }
            for (int entry = 0; entry < globalCount; entry++)
            {
                if (i + 3 >= encoded.Count) return null;
                string key = encoded[i++];
                if (!int.TryParse(encoded[i++], out int stateValue) ||
                    !Enum.IsDefined(typeof(ExactGlobalStateKind), stateValue) ||
                    !int.TryParse(encoded[i++], out int storedCount) ||
                    storedCount < 0 || i + storedCount >= encoded.Count)
                {
                    return null;
                }
                List<string> stored = encoded.GetRange(i, storedCount);
                i += storedCount;
                if (!int.TryParse(encoded[i++], out int effectiveCount) ||
                    effectiveCount < 0 || i + effectiveCount > encoded.Count)
                {
                    return null;
                }
                List<string> effective = encoded.GetRange(i, effectiveCount);
                i += effectiveCount;
                result[key] = new WorkGiverLayoutOrderSnapshot(
                    (ExactGlobalStateKind)stateValue,
                    stored,
                    effective);
            }
            if (i >= encoded.Count ||
                !int.TryParse(encoded[i++], out int pawnCount) || pawnCount < 0)
            {
                return null;
            }
            var pawnOrders = new List<WorkGiverLayoutPawnOrderSnapshot>(pawnCount);
            for (int entry = 0; entry < pawnCount; entry++)
            {
                if (i + 3 >= encoded.Count ||
                    !int.TryParse(encoded[i++], out int pawnId)) return null;
                string workType = encoded[i++];
                string hadOrderValue = encoded[i++];
                if ((hadOrderValue != "0" && hadOrderValue != "1") ||
                    !int.TryParse(encoded[i++], out int count) ||
                    count < 0 || i + count > encoded.Count)
                {
                    return null;
                }
                List<string> order = encoded.GetRange(i, count);
                i += count;
                pawnOrders.Add(new WorkGiverLayoutPawnOrderSnapshot(
                    pawnId,
                    workType,
                    hadOrderValue == "1",
                    order));
            }
            return i == encoded.Count
                ? new WorkGiverLayoutSnapshot(targetWorkTypeDefName, result, pawnOrders)
                : null;
        }

        private static bool MatchesLayoutSnapshot(
            WorkGiverDef giver,
            WorkGiverLayoutSnapshot expected) =>
            SnapshotsEqual(
                CaptureSnapshot(giver, expected.Orders.Keys, expected.PawnOrders),
                expected);

        private static void ApplyPawnOrderStates(
            WorkGiverReassignmentData data,
            IReadOnlyList<WorkGiverLayoutPawnOrderSnapshot> states)
        {
            for (int i = 0; i < states.Count; i++)
            {
                WorkGiverLayoutPawnOrderSnapshot state = states[i];
                data.PawnWorkGiverOrdering.TryGetValue(state.PawnId, out var pawn);
                if (!state.HadOrder)
                {
                    pawn?.Remove(state.WorkTypeDefName);
                    if (pawn?.Count == 0) data.PawnWorkGiverOrdering.Remove(state.PawnId);
                    continue;
                }

                if (pawn == null)
                {
                    pawn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    data.PawnWorkGiverOrdering[state.PawnId] = pawn;
                }
                pawn[state.WorkTypeDefName] = state.Order.ToList();
            }
        }

        private static bool SnapshotsEqual(WorkGiverLayoutSnapshot a, WorkGiverLayoutSnapshot b) =>
            string.Equals(a?.TargetWorkTypeDefName, b?.TargetWorkTypeDefName, StringComparison.Ordinal) &&
            a.Orders.Count == b.Orders.Count &&
            a.Orders.All(pair =>
                b.Orders.TryGetValue(pair.Key, out WorkGiverLayoutOrderSnapshot value) &&
                pair.Value.State == value.State &&
                pair.Value.StoredOrder.SequenceEqual(value.StoredOrder) &&
                pair.Value.EffectiveOrder.SequenceEqual(value.EffectiveOrder)) &&
            a.PawnOrders.Count == b.PawnOrders.Count &&
            a.PawnOrders.All(order => b.PawnOrders.Any(value =>
                order.PawnId == value.PawnId &&
                string.Equals(order.WorkTypeDefName, value.WorkTypeDefName, StringComparison.Ordinal) &&
                order.HadOrder == value.HadOrder &&
                order.Order.SequenceEqual(value.Order)));
    }
}
