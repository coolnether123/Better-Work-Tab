using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RWWidgets = Verse.Widgets;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Column 2: Displays priority buttons for the selected work type.
    /// Each button shows a badge with the number of rules for that priority.
    /// Clicking selects the priority and updates column 3.
    /// </summary>
    public class PrioritySelectorPanel
    {
        private const float HeaderHeight = 40f;
        private const float ButtonHeight = 40f;
        private const float ButtonSpacing = 6f;
        private const float BadgeSize = 20f;
        private string _draggingKey;
        private float? _previewLineY;

        /// <summary>
        /// Draws the priority selector panel.
        /// </summary>
        public void Draw(Rect rect, RuleBuilderState state)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackgroundLight);
            RWWidgets.DrawBox(rect, 1);

            Rect innerRect = rect.ContractedBy(8f);
            state.EnsurePriorityOrder();

            if (state.SelectedWorkType == null)
            {
                DrawEmptyState(innerRect, "BWT_SelectWorkTypeFirst".Translate());
                return;
            }

            // Header showing selected work type
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, HeaderHeight);
            DrawWorkTypeHeader(headerRect, state.SelectedWorkType);

            // Priority buttons
            Rect buttonsRect = new Rect(
                innerRect.x,
                headerRect.yMax + 12f,
                innerRect.width,
                innerRect.height - headerRect.height - 16f);
            DrawPriorityButtons(buttonsRect, state);
        }

        private void DrawWorkTypeHeader(Rect rect, WorkTypeDef workType)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(rect, workType.labelShort.CapitalizeFirst());

            Rect lineRect = new Rect(rect.x, rect.yMax - 2f, rect.width, 2f);
            RWWidgets.DrawBoxSolid(lineRect, RuleBuilderConstants.HeaderColor * 0.5f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawPriorityButtons(Rect rect, RuleBuilderState state)
        {
            var ruleCounts = state.GetPriorityRuleCounts(state.SelectedWorkType);
            bool isReadOnly = state.IsRulesetReadOnly;

            // Label
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, 24f);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(labelRect, "BWT_SelectPriority".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Buttons
            float buttonY = labelRect.yMax + 8f;
            var order = state.PriorityOrder;
            var rects = new System.Collections.Generic.List<(int priority, Rect rect)>();

            for (int idx = 0; idx < order.Count; idx++)
            {
                int priority = order[idx];
                Rect buttonRect = new Rect(
                    rect.x,
                    buttonY + idx * (ButtonHeight + ButtonSpacing),
                    rect.width,
                    ButtonHeight);

                int ruleCount = ruleCounts.TryGetValue(priority, out int count) ? count : 0;
                bool isSelected = state.SelectedPriority == priority;

                DrawPriorityButton(buttonRect, priority, ruleCount, isSelected, isReadOnly, state);
                rects.Add((priority, buttonRect));
            }

            if (!isReadOnly)
            {
                HandlePriorityDrag(rects, state);
            }

            if (_previewLineY.HasValue)
            {
                var oldColor = GUI.color;
                GUI.color = RuleBuilderConstants.HeaderColor;
                RWWidgets.DrawLineHorizontal(rect.x, _previewLineY.Value, rect.width);
                GUI.color = oldColor;
            }
        }

        private void DrawPriorityButton(
            Rect rect,
            int priority,
            int ruleCount,
            bool isSelected,
            bool isReadOnly,
            RuleBuilderState state)
        {
            // Background color based on selection state
            bool isDragging = state.DragController.IsDragging;
            bool isDragHover = isDragging && Mouse.IsOver(rect);
            
            Color bgColor;
            if (isSelected)
            {
                bgColor = RuleBuilderConstants.PriorityColors[priority];
            }
            else if (isDragHover)
            {
                bgColor = new Color(0.3f, 0.5f, 0.7f, 0.5f);
            }
            else if (Mouse.IsOver(rect))
            {
                bgColor = RuleBuilderConstants.CardBackgroundHover;
            }
            else
            {
                bgColor = RuleBuilderConstants.CardBackground;
            }

            RWWidgets.DrawBoxSolid(rect, bgColor);
            
            // Draw special border when drag hovering
            if (isDragHover)
            {
                GUI.color = new Color(0.5f, 0.7f, 1f, 0.8f);
                RWWidgets.DrawBox(rect, 3);
                GUI.color = Color.white;
            }
            else
            {
                RWWidgets.DrawBox(rect, isSelected ? 2 : 1);
            }

            // Priority indicator
            Rect indicatorRect = new Rect(rect.x + 8f, rect.y + 8f, 24f, 24f);
            Color indicatorColor = isSelected
                ? Color.white
                : RuleBuilderConstants.PriorityColors[priority];

            RWWidgets.DrawBoxSolid(indicatorRect, indicatorColor);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = isSelected ? RuleBuilderConstants.PriorityColors[priority] : Color.white;

            string priorityLabel = priority == 0 ? "X" : priority.ToString();
            RWWidgets.Label(indicatorRect, priorityLabel);

            // Priority description
            Rect labelRect = new Rect(
                indicatorRect.xMax + 8f,
                rect.y,
                rect.width - indicatorRect.width - BadgeSize - 32f,
                rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = isSelected ? Color.white : RuleBuilderConstants.LabelColor;

            string label = GetPriorityLabel(priority);
            RWWidgets.Label(labelRect, label);

            // Rule count badge
            if (ruleCount > 0)
            {
                Rect badgeRect = new Rect(
                    rect.xMax - BadgeSize - 8f,
                    rect.y + (rect.height - BadgeSize) / 2f,
                    BadgeSize,
                    BadgeSize);

                RWWidgets.DrawBoxSolid(badgeRect, RuleBuilderConstants.SuccessColor);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = Color.white;
                RWWidgets.Label(badgeRect, ruleCount.ToString());
                Text.Font = GameFont.Small;
            }
            
            // Hover switch for drag
            if (state.DragController.IsDragging && Mouse.IsOver(rect))
            {
                state.DragController.NotifyPriorityHover(priority);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Click handler
            if (RWWidgets.ButtonInvisible(rect))
            {
                state.SelectedPriority = priority;
            }

            // Tooltip
            string tooltip = GetPriorityTooltip(priority, ruleCount);
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private string GetPriorityLabel(int priority)
        {
            return priority switch
            {
                0 => "BWT_Priority_Disabled".Translate(),
                1 => "BWT_Priority_Highest".Translate(),
                2 => "BWT_Priority_High".Translate(),
                3 => "BWT_Priority_Normal".Translate(),
                4 => "BWT_Priority_Low".Translate(),
                _ => $"Priority {priority}"
            };
        }

        private string GetPriorityTooltip(int priority, int ruleCount)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"<b>{GetPriorityLabel(priority)}</b>");
            sb.AppendLine();

            string desc = priority switch
            {
                0 => "BWT_Priority_Disabled_Desc".Translate(),
                1 => "BWT_Priority_Highest_Desc".Translate(),
                2 => "BWT_Priority_High_Desc".Translate(),
                3 => "BWT_Priority_Normal_Desc".Translate(),
                4 => "BWT_Priority_Low_Desc".Translate(),
                _ => ""
            };

            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine(desc);
                sb.AppendLine();
            }

            if (ruleCount > 0)
            {
                sb.AppendLine($"<color=#66CC66>{ruleCount} condition set(s)</color>");
            }
            else
            {
                sb.AppendLine("<color=#999999>No conditions set</color>");
            }

            sb.AppendLine();
            sb.AppendLine("<i>Click to select</i>");

            return sb.ToString();
        }

        private void DrawEmptyState(Rect rect, string message)
        {
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            RWWidgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void HandlePriorityDrag(System.Collections.Generic.List<(int priority, Rect rect)> rects, RuleBuilderState state)
        {
            var evt = Event.current;
            if (evt == null || rects.Count == 0) return;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                foreach (var tuple in rects)
                {
                    if (tuple.rect.Contains(evt.mousePosition))
                    {
                        _draggingKey = tuple.priority.ToString();
                        evt.Use();
                        break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_draggingKey) && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove))
            {
                int draggingPriority = int.Parse(_draggingKey);
                int targetIndex = CalculateTargetIndex(rects, evt.mousePosition.y);
                state.MovePriority(draggingPriority, targetIndex);
                _previewLineY = CalculatePreviewLine(rects, targetIndex);
                evt.Use();
            }
            else if (!string.IsNullOrEmpty(_draggingKey) && (evt.type == EventType.MouseUp || evt.type == EventType.MouseLeaveWindow))
            {
                _draggingKey = null;
                _previewLineY = null;
                evt.Use();
            }
        }

        private int CalculateTargetIndex(System.Collections.Generic.List<(int priority, Rect rect)> rects, float mouseY)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (mouseY < rects[i].rect.center.y)
                {
                    return i;
                }
            }
            return rects.Count;
        }

        private float CalculatePreviewLine(System.Collections.Generic.List<(int priority, Rect rect)> rects, int targetIndex)
        {
            if (targetIndex <= 0)
            {
                return rects[0].rect.y;
            }
            if (targetIndex >= rects.Count)
            {
                return rects[rects.Count - 1].rect.yMax;
            }
            return rects[targetIndex - 1].rect.yMax;
        }
    }
}
