using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Multiplayer.API;
#endif
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    internal sealed class WorkGiverLayoutSnapshot
    {
        internal WorkGiverLayoutSnapshot(string targetWorkTypeDefName, IDictionary<string, List<string>> orders)
            : this(targetWorkTypeDefName, orders.ToDictionary(
                pair => pair.Key, pair => (IEnumerable<string>)pair.Value, StringComparer.Ordinal))
        {
        }

        internal WorkGiverLayoutSnapshot(string targetWorkTypeDefName, IDictionary<string, IEnumerable<string>> orders)
        {
            TargetWorkTypeDefName = targetWorkTypeDefName;
            Orders = orders.ToDictionary(
                pair => pair.Key,
                pair => (IList<string>)pair.Value.ToList().AsReadOnly(),
                StringComparer.Ordinal);
        }

        internal string TargetWorkTypeDefName { get; }
        internal IDictionary<string, IList<string>> Orders { get; }
    }

    internal sealed class WorkGiverLayoutCommand
    {
        internal WorkGiverLayoutCommand(long id, string workGiverDefName, int expectedVersion,
            WorkGiverLayoutSnapshot before, WorkGiverLayoutSnapshot after)
        {
            Id = id;
            WorkGiverDefName = workGiverDefName;
            ExpectedVersion = expectedVersion;
            Before = before;
            After = after;
        }

        internal long Id { get; }
        internal string WorkGiverDefName { get; }
        internal int ExpectedVersion { get; }
        internal WorkGiverLayoutSnapshot Before { get; }
        internal WorkGiverLayoutSnapshot After { get; }
    }

    internal static class WorkGiverLayoutHistory
    {
        private const int Capacity = 64;
        private static readonly List<WorkGiverLayoutCommand> UndoCommands = new List<WorkGiverLayoutCommand>();
        private static readonly List<WorkGiverLayoutCommand> RedoCommands = new List<WorkGiverLayoutCommand>();
        private static long _nextId;

        internal static long NextId() => ++_nextId;
        internal static bool CanUndo => UndoCommands.Count > 0;
        internal static bool CanRedo => RedoCommands.Count > 0;

        internal static void RecordForward(WorkGiverLayoutCommand command)
        {
            UndoCommands.Add(command);
            if (UndoCommands.Count > Capacity) UndoCommands.RemoveAt(0);
            RedoCommands.Clear();
        }

        internal static WorkGiverLayoutCommand PeekUndo() => CanUndo ? UndoCommands[UndoCommands.Count - 1] : null;
        internal static WorkGiverLayoutCommand PeekRedo() => CanRedo ? RedoCommands[RedoCommands.Count - 1] : null;

        internal static void CompleteUndo(WorkGiverLayoutCommand command)
        {
            if (!ReferenceEquals(PeekUndo(), command)) return;
            UndoCommands.RemoveAt(UndoCommands.Count - 1);
            RedoCommands.Add(command);
        }

        internal static void CompleteRedo(WorkGiverLayoutCommand command)
        {
            if (!ReferenceEquals(PeekRedo(), command)) return;
            RedoCommands.RemoveAt(RedoCommands.Count - 1);
            UndoCommands.Add(command);
        }

        internal static void Clear()
        {
            UndoCommands.Clear();
            RedoCommands.Clear();
        }
    }

    internal static partial class WorkGiverReassignmentManager
    {
        internal static bool CanUndoWorkGiverLayout => WorkGiverLayoutHistory.CanUndo;
        internal static bool CanRedoWorkGiverLayout => WorkGiverLayoutHistory.CanRedo;

        internal static bool TryMoveWorkGiverLayout(string workGiverDefName, string targetWorkTypeDefName,
            int insertIndex, out string errorMessage)
        {
            if (!TryCreateLayoutCommand(workGiverDefName, targetWorkTypeDefName, insertIndex, out var command, out errorMessage))
                return false;

            SubmitLayoutCommand(command, command.After, 0);
            return true;
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
            SubmitLayoutCommand(command, command.Before, 1);
            return true;
        }

        internal static bool TryRedoWorkGiverLayout()
        {
            var command = WorkGiverLayoutHistory.PeekRedo();
            if (command == null) return false;
            SubmitLayoutCommand(command, command.After, 2);
            return true;
        }

        private static void SubmitLayoutCommand(WorkGiverLayoutCommand command, WorkGiverLayoutSnapshot snapshot, int historyAction)
        {
            var encoded = EncodeOrders(snapshot.Orders);
            if (MultiplayerBridge.Active)
            {
                SyncApplyWorkGiverLayout(command.Id, command.WorkGiverDefName, CurrentSyncVersion,
                    snapshot.TargetWorkTypeDefName, encoded, historyAction);
                return;
            }

            ApplySynchronizedLayout(command.Id, command.WorkGiverDefName, CurrentSyncVersion,
                snapshot.TargetWorkTypeDefName, encoded, historyAction, command);
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        public static void SyncApplyWorkGiverLayout(long commandId, string workGiverDefName, int expectedVersion,
            string targetWorkTypeDefName, List<string> encodedOrders, int historyAction)
        {
            ApplySynchronizedLayout(commandId, workGiverDefName, expectedVersion,
                targetWorkTypeDefName, encodedOrders, historyAction, null);
        }

        private static void ApplySynchronizedLayout(long commandId, string workGiverDefName, int expectedVersion,
            string targetWorkTypeDefName, List<string> encodedOrders, int historyAction, WorkGiverLayoutCommand localCommand)
        {
            var data = Data;
            var giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (data == null || giver == null) return;
            if (expectedVersion != CurrentSyncVersion)
            {
                WorkGiverLayoutHistory.Clear();
                return;
            }

            var orders = DecodeOrders(encodedOrders);
            if (orders.Count == 0) return;

            WorkGiverLayoutCommand command = localCommand;
            if (command == null)
            {
                var before = CaptureSnapshot(giver, orders.Keys);
                var after = new WorkGiverLayoutSnapshot(targetWorkTypeDefName, orders);
                command = new WorkGiverLayoutCommand(commandId, workGiverDefName, expectedVersion, before, after);
            }

            data.EnsureCollections();
            if (targetWorkTypeDefName == giver.workType?.defName)
                data.WorkGiverToWorkTypeMap.Remove(giver.defName);
            else
                data.WorkGiverToWorkTypeMap[giver.defName] = targetWorkTypeDefName;

            foreach (var pair in orders)
                data.WorkTypeWorkGiverOrder[pair.Key] = pair.Value.Distinct().ToList();

            RemoveWorkGiverFromPawnOrders(data, giver.defName);
            foreach (var moved in data.PlayerMovedWorkGiversByWorkType.Values) moved?.Remove(giver.defName);
            data.SyncVersion++;
            InvalidateCaches();

            var target = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            if (target != null && (target != giver.workType || IsWorkGiverOutOfBaselinePosition(target, giver)))
                RecordPlayerMovedWorkGiver(target, giver);

            if (historyAction == 0) WorkGiverLayoutHistory.RecordForward(command);
            else if (historyAction == 1) WorkGiverLayoutHistory.CompleteUndo(WorkGiverLayoutHistory.PeekUndo());
            else WorkGiverLayoutHistory.CompleteRedo(WorkGiverLayoutHistory.PeekRedo());

            NotifySubWorkDataChanged();
            ExternalPriorityMirror.NotifyWorkGiverChangedForAllPawns(giver);
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
            var affected = new HashSet<string>(StringComparer.Ordinal) { target.defName };
            if (source != null) affected.Add(source.defName);
            var before = CaptureSnapshot(giver, affected);
            var afterOrders = before.Orders.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
            foreach (var list in afterOrders.Values) list.Remove(giver.defName);
            var targetOrder = afterOrders[target.defName];
            targetOrder.Insert(Math.Max(0, Math.Min(insertIndex, targetOrder.Count)), giver.defName);
            var after = new WorkGiverLayoutSnapshot(target.defName, afterOrders);
            if (before.TargetWorkTypeDefName == after.TargetWorkTypeDefName && OrdersEqual(before.Orders, after.Orders))
                return false;
            command = new WorkGiverLayoutCommand(WorkGiverLayoutHistory.NextId(), giver.defName, CurrentSyncVersion, before, after);
            return true;
        }

        private static WorkGiverLayoutSnapshot CaptureSnapshot(WorkGiverDef giver, IEnumerable<string> workTypes)
        {
            var orders = new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);
            foreach (string name in workTypes.Distinct())
            {
                var type = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                orders[name] = type == null ? Enumerable.Empty<string>() :
                    GetDisplayWorkGiversForWorkType(type).Where(w => w?.def != null).Select(w => w.def.defName).ToList();
            }
            return new WorkGiverLayoutSnapshot((GetTargetWorkType(giver) ?? giver.workType)?.defName, orders);
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

        private static List<string> EncodeOrders(IDictionary<string, IList<string>> orders)
        {
            var result = new List<string>();
            foreach (var pair in orders.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                result.Add(pair.Key);
                result.Add(pair.Value.Count.ToString());
                result.AddRange(pair.Value);
            }
            return result;
        }

        private static Dictionary<string, List<string>> DecodeOrders(List<string> encoded)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; encoded != null && i + 1 < encoded.Count;)
            {
                string key = encoded[i++];
                if (!int.TryParse(encoded[i++], out int count) || count < 0 || i + count > encoded.Count) return new Dictionary<string, List<string>>();
                result[key] = encoded.GetRange(i, count);
                i += count;
            }
            return result;
        }

        private static bool OrdersEqual(IDictionary<string, IList<string>> a,
            IDictionary<string, IList<string>> b) =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && pair.Value.SequenceEqual(value));
    }
}
