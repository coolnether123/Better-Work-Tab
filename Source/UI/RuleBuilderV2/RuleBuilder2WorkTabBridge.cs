using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal readonly struct RuleBuilder2WorkTabSelection
    {
        public RuleBuilder2WorkTabSelection(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds,
            RuleBuilder2TargetSource source)
        {
            WorkType = workType;
            WorkGiver = workGiver;
            Pawn = pawn;
            Priority = priority;
            Bounds = bounds;
            Source = source;
        }

        public WorkTypeDef WorkType { get; }
        public WorkGiverDef WorkGiver { get; }
        public Pawn Pawn { get; }
        public int Priority { get; }
        public Rect Bounds { get; }
        public RuleBuilder2TargetSource Source { get; }
    }

    internal static class RuleBuilder2WorkTabBridge
    {
        private static Window_RuleBuilder2 activeWindow;
        private static RuleBuilder2WorkTabSelection? lastSelection;
        private static RuleBuilder2WorkTabSelection? hoverSelection;
        private static Rect highlightedBounds;
        private static float highlightedAt;

        internal static bool IsOpen => activeWindow != null;

        internal static void Register(Window_RuleBuilder2 window)
        {
            activeWindow = window;
        }

        internal static void Unregister(Window_RuleBuilder2 window)
        {
            if (activeWindow == window)
            {
                activeWindow = null;
                lastSelection = null;
                hoverSelection = null;
            }
        }

        internal static void SelectTarget(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds,
            RuleBuilder2TargetSource source)
        {
            if (activeWindow == null || workType == null)
            {
                return;
            }

            var selection = new RuleBuilder2WorkTabSelection(workType, workGiver, pawn, priority, bounds, source);
            lastSelection = selection;
            highlightedBounds = bounds;
            highlightedAt = Time.realtimeSinceStartup;
            hoverSelection = null;
            activeWindow.AcceptWorkTabSelection(selection);
        }

        internal static void PreviewTarget(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds,
            RuleBuilder2TargetSource source)
        {
            if (activeWindow == null || workType == null)
            {
                return;
            }

            var selection = new RuleBuilder2WorkTabSelection(workType, workGiver, pawn, priority, bounds, source);
            hoverSelection = selection;
            activeWindow.PreviewWorkTabSelection(selection);
        }

        internal static void ClearPreview()
        {
            if (!hoverSelection.HasValue)
            {
                return;
            }

            hoverSelection = null;
            activeWindow?.ClearWorkTabPreview();
        }

        internal static bool FlashTarget(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            if (activeWindow == null || workType == null)
            {
                return false;
            }

            if (!MainTabCompat.TryGetOpenBetterWorkTab(out Better_Work_Tab.UI.MainTabWindow_BetterWork workTab) ||
                !workTab.TryGetRuleBuilder2TargetHeaderBounds(workType, workGiver, out Rect bounds))
            {
                return false;
            }

            highlightedBounds = bounds;
            highlightedAt = Time.realtimeSinceStartup;
            return true;
        }

        internal static bool BlocksWorkTabHover()
        {
            return activeWindow?.windowRect.Contains(Verse.UI.MousePositionOnUIInverted) == true;
        }

        internal static bool TryGetSelection(out RuleBuilder2WorkTabSelection selection)
        {
            if (lastSelection.HasValue)
            {
                selection = lastSelection.Value;
                return true;
            }

            selection = default;
            return false;
        }

        internal static bool ShouldHighlight(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            if (activeWindow == null || !(BetterWorkTabMod.Settings?.ruleBuilder2ShowWorkTabHighlights ?? true))
            {
                return false;
            }

            if (SubWorkDrilldownState.IsActive)
            {
                return ShouldHighlightSubWorkTarget(workType, workGiver);
            }

            if (hoverSelection.HasValue && Matches(hoverSelection.Value, workType, workGiver))
            {
                return true;
            }

            return activeWindow.IsTargetSelected(workType, workGiver);
        }

        private static bool ShouldHighlightSubWorkTarget(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            WorkTypeDef activeWorkType = SubWorkDrilldownState.ActiveWorkType;
            if (workType == null || workType != activeWorkType || workGiver == null)
            {
                return false;
            }

            if (hoverSelection.HasValue && MatchesSubWorkDrilldown(hoverSelection.Value, activeWorkType, workGiver))
            {
                return true;
            }

            return activeWindow.IsTargetSelected(activeWorkType, workGiver) ||
                   activeWindow.IsTargetSelected(activeWorkType, null);
        }

        private static bool MatchesSubWorkDrilldown(
            RuleBuilder2WorkTabSelection selection,
            WorkTypeDef activeWorkType,
            WorkGiverDef workGiver)
        {
            if (selection.WorkType != activeWorkType)
            {
                return false;
            }

            return selection.WorkGiver == null ||
                   (workGiver != null && selection.WorkGiver.defName == workGiver.defName);
        }

        internal static bool ShouldHighlightPawn(Pawn pawn)
        {
            if (activeWindow == null ||
                pawn == null ||
                !(BetterWorkTabMod.Settings?.ruleBuilder2ShowWorkTabHighlights ?? true))
            {
                return false;
            }

            return (hoverSelection.HasValue && hoverSelection.Value.Pawn == pawn) ||
                   (lastSelection.HasValue && lastSelection.Value.Pawn == pawn);
        }

        private static bool Matches(RuleBuilder2WorkTabSelection selection, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return selection.WorkType == workType &&
                   (selection.WorkGiver?.defName ?? "") == (workGiver?.defName ?? "");
        }

        internal static void DrawRecentSelectionPulse()
        {
            if (activeWindow == null ||
                highlightedBounds.width <= 0f ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            float age = Time.realtimeSinceStartup - highlightedAt;
            if (age > 0.55f)
            {
                return;
            }

            float alpha = Mathf.SmoothStep(0.35f, 0f, age / 0.55f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 0.86f, 0.25f, alpha);
            Widgets.DrawBox(highlightedBounds.ExpandedBy(3f), 2);
            GUI.color = old;
        }
    }
}
