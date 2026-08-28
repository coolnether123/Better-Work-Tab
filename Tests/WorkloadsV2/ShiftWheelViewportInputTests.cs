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
            string viewport = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Layout",
                "WorkTabViewportController.cs");
            string angledHeaders = Read(
                root,
                "Source",
                "UI",
                "Headers",
                "Angled",
                "AngledHeaderInteraction.cs");
            string shiftHelper = Read(
                root,
                "Source",
                "Features",
                "Patches",
                "ShiftHelper.cs");

            RootPriorityInputLeavesShiftWheelForTheViewport(priorityInput, body);
            ParentPriorityFallbackLeavesShiftWheelForTheViewport(parentPriority);
            SpecificJobPriorityInputLeavesShiftWheelForTheViewport(specificPriority);
            ViewportOwnsOnlyBodyShiftWheel(body, viewport);
            AngledHeadersKeepTheirShiftWheelGesture(angledHeaders);
            RepaintDoesNotRetainAReleasedShiftKey(shiftHelper);
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
                "the PawnTable viewport must retain Shift-wheel ownership after priority input declines it");
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

        private static void ViewportOwnsOnlyBodyShiftWheel(
            string body,
            string viewport)
        {
            string drawRows = MemberBody(body, "internal void DrawRows(");
            int route = drawRows.IndexOf(
                "TryApplyShiftTranslatedVerticalWheel",
                StringComparison.Ordinal);
            int consume = drawRows.IndexOf("Event.current.Use();", StringComparison.Ordinal);
            int begin = drawRows.IndexOf("Widgets.BeginScrollView", StringComparison.Ordinal);
            TestAssert.True(
                route >= 0 && consume > route && begin > consume,
                "the viewport must consume Shift-translated wheel once before BeginScrollView can treat it as horizontal");
            TestAssert.Equal(
                1,
                CountOccurrences(drawRows, "Event.current.Use();"),
                "DrawRows must have one explicit owner for the translated Shift-wheel event");
            TestAssert.Contains(
                viewport,
                "_lastViewport.OutRect.Contains(currentEvent.mousePosition)",
                "the body route must not steal Shift-wheel outside the actual table viewport");
            string resolve = MemberBody(viewport, "private static bool ResolveWheelMotion(");
            TestAssert.Contains(resolve, "verticalDelta = horizontalDelta;",
                "physical Shift-wheel delta=(3,0) must remain vertical viewport motion");
            TestAssert.Contains(resolve, "horizontalDelta = 0f;",
                "physical Shift-wheel must not spuriously move the table horizontally");
        }

        private static void AngledHeadersKeepTheirShiftWheelGesture(string angledHeaders)
        {
            string interactions = MemberBody(angledHeaders, "internal static void HandleInteractions(");
            int headerHit = interactions.IndexOf("!ctx.IsMouseOver", StringComparison.Ordinal);
            int shiftGesture = interactions.IndexOf("TryHandleShiftPriorityGesture", StringComparison.Ordinal);
            TestAssert.True(
                headerHit >= 0 && shiftGesture > headerHit,
                "angled-header Shift-wheel remains a header-only column-priority gesture");

            string gesture = MemberBody(angledHeaders, "internal static bool TryHandleShiftPriorityGesture(");
            TestAssert.Contains(gesture, "Mathf.Abs(evt.delta.y) < 0.01f",
                "the header gesture must ignore the horizontal-only platform Shift-wheel encoding");
            TestAssert.Contains(gesture, "HandleShiftClick(worker, table, evt.delta.y < 0f ? 0 : 1, application);",
                "angled headers must retain their intentional vertical Shift-wheel priority gesture");
        }

        private static void RepaintDoesNotRetainAReleasedShiftKey(string shiftHelper)
        {
            string held = MemberBody(shiftHelper, "public static bool IsHeld");
            TestAssert.Contains(
                held,
                "current.type != EventType.Layout",
                "layout must use live Unity key state instead of a stale IMGUI modifier");
            TestAssert.Contains(
                held,
                "current.type != EventType.Repaint",
                "repaint must use live Unity key state instead of retaining Shift from the previous input event");
            TestAssert.Contains(
                held,
                "current.type != EventType.Used",
                "a consumed input event must not keep Shift active for later drawing in the same pass");
            TestAssert.Contains(
                held,
                "Input.GetKey(KeyCode.LeftShift)",
                "left Shift must remain live during rendering");
            TestAssert.Contains(
                held,
                "Input.GetKey(KeyCode.RightShift)",
                "right Shift must remain live during rendering");
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

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
