using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ShiftWheelViewportInputTests
    {
        internal static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Interaction", "WorkTabPriorityInputHandler.cs"),
                "Shift-wheel viewport input contracts");
            string priorityInput = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Interaction",
                "WorkTabPriorityInputHandler.cs");
            string parentPriority = Read(
                root,
                "Source",
                "Features",
                "Patches",
                "Patch_WorkPriority_DoCell_Unified.cs");
            string specificPriority = Read(
                root,
                "Source",
                "UI",
                "WorkGiverReassignments",
                "WorkGiverPriorityBoxRenderer.cs");
            string body = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Rendering",
                "WorkTabBodyRenderer.cs");

            RootPriorityInputLeavesShiftWheelForTheViewport(priorityInput, body);
            ParentPriorityFallbackLeavesShiftWheelForTheViewport(parentPriority);
            SpecificJobPriorityInputLeavesShiftWheelForTheViewport(specificPriority);
        }

        private static void RootPriorityInputLeavesShiftWheelForTheViewport(
            string priorityInput,
            string body)
        {
            string input = MemberBody(priorityInput, "internal bool TryHandlePriorityCellInput(");
            int shiftWheel = input.IndexOf(
                "evt.type == EventType.ScrollWheel && evt.shift",
                StringComparison.Ordinal);
            int rowHit = input.IndexOf("_bodyRenderer.TryGetRowAt", StringComparison.Ordinal);
            TestAssert.True(
                shiftWheel >= 0 && shiftWheel < rowHit,
                "shift-wheel must leave root priority input before row/cell hit testing or preview membership work");
            TestAssert.Contains(
                body,
                "Widgets.BeginScrollView(viewport.OutRect, ref table.scrollPosition, viewport.ViewRect);",
                "the existing PawnTable scroll view must remain the Shift-wheel owner");
        }

        private static void ParentPriorityFallbackLeavesShiftWheelForTheViewport(string parentPriority)
        {
            string scroll = MemberBody(parentPriority, "private static bool TryHandleWorkPriorityScroll(");
            int shiftWheel = scroll.IndexOf("evt.shift", StringComparison.Ordinal);
            int readPriority = scroll.IndexOf("ParentPriorityRead.GetObserved", StringComparison.Ordinal);
            int consume = scroll.IndexOf("evt.Use();", StringComparison.Ordinal);
            TestAssert.True(
                shiftWheel >= 0 && shiftWheel < readPriority && shiftWheel < consume,
                "the direct parent-priority fallback must reject Shift-wheel before reading, writing, or consuming it");
            TestAssert.Contains(
                scroll,
                "evt.type != EventType.ScrollWheel",
                "ordinary unmodified priority-wheel input must remain supported");

            string prefix = MemberBody(parentPriority, "private static bool PrefixProfiled(");
            int blockedShiftWheel = prefix.IndexOf("priorityInput.shift", StringComparison.Ordinal);
            int blockedInputConsume = prefix.IndexOf("priorityInput.Use();", StringComparison.Ordinal);
            TestAssert.True(
                blockedShiftWheel >= 0 && blockedShiftWheel < blockedInputConsume,
                "a read-only parent cell must leave Shift-wheel unconsumed for the PawnTable viewport");
        }

        private static void SpecificJobPriorityInputLeavesShiftWheelForTheViewport(string specificPriority)
        {
            string rootInput = MemberBody(specificPriority, "internal static bool TryHandleRootInput(");
            int shiftWheel = rootInput.IndexOf(
                "evt.type == EventType.ScrollWheel && evt.shift",
                StringComparison.Ordinal);
            int draw = rootInput.IndexOf("DrawPriorityBox(", StringComparison.Ordinal);
            TestAssert.True(
                shiftWheel >= 0 && shiftWheel < draw,
                "the root-routed sub-work priority box must reject Shift-wheel before drawing handles input");

            string inputGate = MemberBody(specificPriority, "private static bool ShouldHandleInput");
            TestAssert.Contains(
                inputGate,
                "evt.type == EventType.ScrollWheel && !evt.shift",
                "sub-work priority boxes must reserve Shift-wheel for the table viewport while retaining normal priority-wheel input");
            TestAssert.Contains(
                inputGate,
                "evt.type == EventType.MouseDown",
                "the Shift-wheel change must not alter sub-work click input");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int index = 0; index < parts.Length; index++)
            {
                path = Path.Combine(path, parts[index]);
            }

            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MemberBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "could not locate source member " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "source member has no opening brace");
            int depth = 0;
            for (int index = open; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }

            throw new InvalidOperationException("source member has no closing brace");
        }
    }
}
