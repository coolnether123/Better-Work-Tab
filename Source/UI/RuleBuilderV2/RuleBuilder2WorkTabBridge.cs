using Better_Work_Tab.Features.Rules.RuleBuilder2;
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
            activeWindow.AcceptWorkTabSelection(selection);
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

            return activeWindow.IsTargetSelected(workType, workGiver);
        }

        internal static bool ShouldHighlightPawn(Pawn pawn)
        {
            return activeWindow != null &&
                   pawn != null &&
                   lastSelection.HasValue &&
                   lastSelection.Value.Pawn == pawn &&
                   (BetterWorkTabMod.Settings?.ruleBuilder2ShowWorkTabHighlights ?? true);
        }

        internal static void DrawRecentSelectionPulse()
        {
            if (activeWindow == null || highlightedBounds.width <= 0f)
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
