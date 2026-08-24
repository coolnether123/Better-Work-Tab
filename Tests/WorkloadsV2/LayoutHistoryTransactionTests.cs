using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Source contracts for the Unity-bound specific-job layout transaction.
    /// These pin the all-or-nothing boundary used by move, undo, redo, and
    /// synchronized replay without requiring a RimWorld world fixture.
    /// </summary>
    internal static class LayoutHistoryTransactionTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine(
                    "Source",
                    "Features",
                    "WorkGiverReassignments",
                    "WorkGiverLayoutHistory.cs"),
                "specific-job layout transaction contracts");
            string layout = Read(root,
                "Source",
                "Features",
                "WorkGiverReassignments",
                "WorkGiverLayoutHistory.cs");
            string schedules = Read(root,
                "Source",
                "Features",
                "TimePriority",
                "TimePriorityService.cs");
            string application = Read(root,
                "Source",
                "Features",
                "Application",
                "WorkTabApplication.cs");

            AssertLayoutAndScheduleCommitOrder(layout);
            AssertExactRollbackContract(layout, schedules);
            AssertMoveUndoRedoAndReplayContract(layout, application);
        }

        private static void AssertLayoutAndScheduleCommitOrder(string layout)
        {
            string apply = Slice(
                layout,
                "internal static bool ApplySpecificLayout(",
                "private static void RestoreScheduleRetargetAfterFailedLayout(");
            int staged = apply.IndexOf(
                "WorkGiverReassignmentData stagedData = previousData.Clone();",
                StringComparison.Ordinal);
            int retarget = apply.IndexOf(
                "if (!TimePriorityService.CommitWorkGiverScheduleRetarget(schedulePlan))",
                StringComparison.Ordinal);
            int retargetFailure = apply.IndexOf("return false;", retarget, StringComparison.Ordinal);
            int liveWrite = apply.IndexOf(
                "data.CopyFrom(stagedData, stagedData.SyncVersion);",
                StringComparison.Ordinal);
            int scheduleCommit = apply.IndexOf(
                "schedulesChanged = TimePriorityService.CommitMutationBatch();",
                StringComparison.Ordinal);

            TestAssert.True(
                staged >= 0 && retarget > staged && retargetFailure > retarget &&
                liveWrite > retargetFailure && scheduleCommit > liveWrite,
                "a layout move must stage mapping/order state, reject a failed retarget before its live write, and commit the schedule batch only after the staged layout is installed");
            TestAssert.Contains(
                apply,
                "if (TimePriorityService.HasActiveMutationBatch)",
                "layout retargets must reject nested schedule batches they cannot own exactly");
        }

        private static void AssertExactRollbackContract(string layout, string schedules)
        {
            TestAssert.Contains(
                layout,
                "RestoreScheduleRetargetAfterFailedLayout(",
                "every failed layout transaction must enter the exact schedule compensation seam");
            TestAssert.Contains(
                layout,
                "data.CopyFrom(previousData, previousData.SyncVersion);",
                "a failed layout transaction must restore its original mapping and ordering data");
            TestAssert.Contains(
                layout,
                "TimePriorityService.DiscardMutationBatch();",
                "an unpublished retarget rollback must discard its schedule batch without a revision or publication");
            TestAssert.Contains(
                schedules,
                "PreviousRuntime = previousRuntime;",
                "a retarget plan must retain its exact runtime schedule baseline");
            TestAssert.Contains(
                schedules,
                "PreviousLegacy = previousLegacy;",
                "a retarget plan must retain its exact compatibility schedule baseline");
            TestAssert.Contains(
                schedules,
                "RestoreUnpublishedWorkGiverScheduleRetarget(",
                "the schedule owner must expose one retarget-specific unpublished restore seam");
            TestAssert.Contains(
                schedules,
                "ReplaceSchedules(RuntimeSchedules, plan.PreviousRuntime);",
                "retarget rollback must restore the captured runtime map rather than infer an inverse move");
            TestAssert.Contains(
                schedules,
                "ReplaceSchedules(LegacySchedules, plan.PreviousLegacy);",
                "retarget rollback must restore the captured compatibility map rather than infer an inverse move");
        }

        private static void AssertMoveUndoRedoAndReplayContract(string layout, string application)
        {
            int scheduleCommit = layout.IndexOf(
                "schedulesChanged = TimePriorityService.CommitMutationBatch();",
                StringComparison.Ordinal);
            int record = layout.IndexOf(
                "WorkGiverLayoutHistory.RecordForward(command);",
                StringComparison.Ordinal);
            int undo = layout.IndexOf(
                "WorkGiverLayoutHistory.CompleteUndo(command);",
                StringComparison.Ordinal);
            int redo = layout.IndexOf(
                "WorkGiverLayoutHistory.CompleteRedo(command);",
                StringComparison.Ordinal);
            TestAssert.True(
                scheduleCommit >= 0 && record > scheduleCommit && undo > scheduleCommit && redo > scheduleCommit,
                "move, undo, and redo history must advance only after the schedule transaction commits");
            TestAssert.Contains(
                layout,
                "WorkGiverLayoutCommand command = localCommand ??",
                "the same transaction must construct a command for synchronized replay when no local command exists");
            TestAssert.Contains(
                application,
                "internal static void SyncApplySpecificLayout(",
                "specific-job layout replay must retain its synchronized application entry point");
            TestAssert.Contains(
                application,
                "targetWorkTypeDefName, encodedOrders, historyAction, null);",
                "multiplayer layout replay must route through the transaction with no local-only command state");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int index = 0; index < parts.Length; index++)
            {
                path = Path.Combine(path, parts[index]);
            }

            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string Slice(string value, string startMarker, string endMarker)
        {
            int start = value.IndexOf(startMarker, StringComparison.Ordinal);
            int end = start < 0
                ? -1
                : value.IndexOf(endMarker, start, StringComparison.Ordinal);
            return start >= 0 && end > start ? value.Substring(start, end - start) : string.Empty;
        }
    }
}
