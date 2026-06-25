using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.UI.RuleBuilder.State;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RWWidgets = Better_Work_Tab.WidgetsCompat;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Renders the "Conditions for" breadcrumb-like chain showing WorkType -> Priority flow.
    /// Uses the current priority order and can collapse/expand beyond the selected priority.
    /// </summary>
    public static class PriorityChainWidget
    {
        /// <summary>
        /// Draws the chain.
        /// </summary>
        /// <param name="rect">Available area.</param>
        /// <param name="state">Rule builder state for work type/priorities.</param>
        /// <param name="expanded">Whether to show all priorities.</param>
        /// <returns>Updated expanded flag.</returns>
        public static bool Draw(Rect rect, RuleBuilderState state, bool expanded)
        {
            if (state == null || state.PriorityOrder == null)
                return expanded;
            state.EnsurePriorityOrder();
            var order = state.PriorityOrder.ToList();
            var selectedPriority = state.SelectedPriority;
            var workType = state.SelectedWorkType;
            var counts = workType != null
                ? state.GetPriorityRuleCounts(workType)
                : new System.Collections.Generic.Dictionary<int, int>();

            float curX = rect.x;
            float centerY = rect.y + rect.height / 2f;

            // Work type badge
            string workLabel = workType?.labelShort.CapitalizeFirst() ?? "BWT_SelectWorkTypeFirst".Translate();
            var wtSize = Text.CalcSize(workLabel) + new Vector2(16f, 6f);
            Rect wtRect = new Rect(curX, centerY - wtSize.y / 2f, wtSize.x, wtSize.y);
            DrawBadge(wtRect, workLabel, RuleBuilderConstants.CardBackground, Color.white);
            curX = wtRect.xMax + 6f;

            // Only include priorities that have rules, plus the selected one.
            var filteredOrder = order
                .Where(p => (counts.TryGetValue(p, out var c) && c > 0) || p == selectedPriority)
                .Distinct()
                .ToList();
            if (selectedPriority >= 0 && !filteredOrder.Contains(selectedPriority))
            {
                filteredOrder.Add(selectedPriority);
            }

            // Build chain items
            var items = new List<PriorityItem>();
            bool passedSelected = false;
            int shownCount = 0;
            int totalWithRules = filteredOrder.Count;

            foreach (var p in filteredOrder)
            {
                if (!expanded && passedSelected)
                {
                    int hidden = totalWithRules - shownCount;
                    if (hidden > 0)
                    {
                        items.Add(new PriorityItem { IsMore = true, Remaining = hidden });
                    }
                    break;
                }

                items.Add(new PriorityItem
                {
                    Priority = p,
                    IsSelected = p == selectedPriority
                });
                shownCount++;

                if (p == selectedPriority)
                {
                    passedSelected = true;
                }
            }

            // Draw chain
            foreach (var item in items)
            {
                DrawArrow(ref curX, centerY);
                if (item.IsMore)
                {
                    string more = item.Remaining > 0
                        ? $"+{item.Remaining}"
                        : string.Empty;
                    if (string.IsNullOrEmpty(more))
                    {
                        continue;
                    }
                    var moreSize = Text.CalcSize(more) + new Vector2(12f, 4f);
                    Rect moreRect = new Rect(curX, centerY - moreSize.y / 2f, moreSize.x, moreSize.y);
                    DrawBadge(moreRect, more, RuleBuilderConstants.PanelBackgroundLight, Color.white);
                    if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(moreRect))
                    {
                        expanded = true;
                    }
                    curX = moreRect.xMax + 6f;
                }
                else
                {
                    string label = item.Priority == 0 ? "BWT_Priority_Disabled".Translate() : $"P{item.Priority}";
                    var labelSize = Text.CalcSize(label) + new Vector2(12f, 4f);
                    Rect prRect = new Rect(curX, centerY - labelSize.y / 2f, labelSize.x, labelSize.y);
                    DrawBadge(prRect,
                        label,
                        WorkPrioritySystem.GetPriorityColor(item.Priority),
                        item.IsSelected ? Color.white : Color.black);
                    if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(prRect))
                    {
                        state.SelectedPriority = item.Priority;
                        state.SelectedRule = null;
                        expanded = false; // collapse up to the newly selected priority
                    }
                    curX = prRect.xMax + 6f;
                }
            }

            return expanded;
        }

        private static void DrawArrow(ref float curX, float centerY)
        {
            const float arrowWidth = 16f;
            const float arrowHeight = 14f;
            Rect arrowRect = new Rect(curX, centerY - arrowHeight / 2f, arrowWidth, arrowHeight);

            var oldColor = GUI.color;
            var oldAnchor = Text.Anchor;
            var oldFont = Text.Font;

            GUI.color = RuleBuilderConstants.SubtleTextColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            RWWidgets.Label(arrowRect, "→");

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            curX = arrowRect.xMax + 6f;
        }

        private static void DrawBadge(Rect rect, string text, Color bg, Color fg)
        {
            var oldColor = GUI.color;
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, bg);
            RWWidgets.DrawBox(rect, 1);
            GUI.color = fg;
            Text.Anchor = TextAnchor.MiddleCenter;
            RWWidgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;
        }

        private struct PriorityItem
        {
            public int Priority;
            public bool IsSelected;
            public bool IsMore;
            public int Remaining;
        }
    }
}

