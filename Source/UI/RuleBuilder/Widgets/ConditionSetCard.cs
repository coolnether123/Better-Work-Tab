using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using RWWidgets = Verse.Widgets;

using Better_Work_Tab.UI.RuleBuilder.Widgets;
using Better_Work_Tab.UI;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Displays a single condition set (rule) with simple row-based editing.
    /// </summary>
    public static class ConditionSetCard
    {
        public enum CardAction { None, Delete, Edit }

        private static string _editingConditionKey;
        private static readonly ConditionDragController DragController = new ConditionDragController();

        private const float NameHeight = 24f;
        private const float RowHeight = 28f;
        private const float RowSpacing = 2f;
        private const float DeleteButtonSize = 20f;

        /// <summary>
        /// Draw the card.
        /// </summary>
        public static CardAction Draw(
            Rect rect,
            WorkAssignmentRule rule,
            int matchCount,
            bool isReadOnly,
            RuleBuilderState state)
        {
            var action = CardAction.None;

            // Minimal frame
            RWWidgets.DrawBox(rect, 1);
            RWWidgets.DrawBoxSolid(rect, new Color(0, 0, 0, 0.06f));

            Rect innerRect = rect.ContractedBy(6f);

            // Header row
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, NameHeight);
            DrawHeader(headerRect, rule, matchCount, isReadOnly);

            // Conditions list
            Rect listRect = new Rect(
                innerRect.x,
                headerRect.yMax + 2f,
                innerRect.width - (isReadOnly ? 0f : DeleteButtonSize + 6f),
                innerRect.height - NameHeight - 8f);

            DrawConditions(listRect, rule.Parameters, isReadOnly, state);

            // Delete button
            if (!isReadOnly)
            {
                float deleteX = listRect.xMax - DeleteButtonSize - 3f;
                float deleteY = headerRect.y + (NameHeight - DeleteButtonSize) / 2f;
                Rect deleteRect = new Rect(deleteX, deleteY, DeleteButtonSize, DeleteButtonSize);
                if (RWWidgets.ButtonImage(deleteRect, TexButton.Delete, Color.white, GenUI.MouseoverColor))
                {
                    action = CardAction.Delete;
                }
                TooltipHandler.TipRegion(deleteRect, "BWT_DeleteRule".Translate());
            }

            // Select card
            if (RWWidgets.ButtonInvisible(rect))
            {
                state.SelectedRule = rule;
            }

            return action;
        }

        private static void DrawHeader(Rect rect, WorkAssignmentRule rule, int matchCount, bool isReadOnly)
        {
            // Handle double-click rename
            if (!isReadOnly && Mouse.IsOver(rect) && Event.current.type == EventType.MouseDown && Event.current.clickCount == 2)
            {
                Event.current.Use();
                string currentName = rule.Name ?? "";
                Find.WindowStack.Add(new Dialog_RenameGeneric(currentName, (newName) =>
                {
                    rule.Name = newName;
                }));
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.LabelColor;

            string displayName = string.IsNullOrEmpty(rule.Name)
                ? ConditionNameGenerator.Generate(rule.Parameters)
                : rule.Name;

            RWWidgets.Label(rect, displayName);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = RuleBuilderConstants.SuccessColor;
            RWWidgets.Label(rect, $"({matchCount})");

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect, BuildTooltip(rule, matchCount));
        }

        private static void DrawConditions(Rect rect, WorkAssignmentParameters parameters, bool isReadOnly, RuleBuilderState state)
        {
            var conditions = ConditionRegistry.GetActiveConditions(parameters);
            float curY = rect.y;
            var rowRects = new List<(string key, Rect rect)>();

            if (conditions.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = RuleBuilderConstants.SubtleTextColor;
                RWWidgets.Label(new Rect(rect.x, curY, rect.width, RowHeight), "BWT_NoConditions".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                curY += RowHeight + RowSpacing;
            }
            else
            {
                foreach (var condition in conditions)
                {
                    Rect rowRect = new Rect(rect.x, curY, rect.width, RowHeight);
                    DrawConditionRow(rowRect, condition, isReadOnly, parameters, state);
                    rowRects.Add((condition.Key, rowRect));
                    curY += RowHeight + RowSpacing;
                }
            }

            // Enable drag/drop reordering to match classic rule editing flexibility.
            if (!isReadOnly)
            {
                DragController.HandleDrag(rowRects, parameters, state);
                if (DragController.IsDragging && DragController.PreviewLineY.HasValue)
                {
                    float lineY = DragController.PreviewLineY.Value;
                    var oldColor = GUI.color;
                    GUI.color = RuleBuilderConstants.HeaderColor;
                    RWWidgets.DrawLineHorizontal(rect.x, lineY, rect.width);
                    GUI.color = oldColor;
                }
            }

            if (!isReadOnly)
            {
                Rect addRect = new Rect(rect.x, curY + 2f, rect.width, 24f);
                if (RWWidgets.ButtonText(addRect, "+ " + "BWT_AddCondition".Translate()))
                {
                    ConditionPickerMenu.Show(parameters, state);
                }
            }
        }

        private static void DrawConditionRow(Rect rect, ConditionInfo condition, bool isReadOnly, WorkAssignmentParameters parameters, RuleBuilderState state)
        {
            bool forceEdit = BetterWorkTabMod.Settings?.alwaysShowConditionEditors ?? false;
            bool isEditing = forceEdit || _editingConditionKey == condition.Key;

            Color rowColor = isEditing
                ? new Color(0.3f, 0.5f, 0.3f, 0.25f)
                : (Mouse.IsOver(rect) ? RuleBuilderConstants.CardBackgroundHover : new Color(0, 0, 0, 0.03f));

            RWWidgets.DrawBoxSolid(rect, rowColor);
            RWWidgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);

            Rect labelRect = new Rect(rect.x + 6f, rect.y, rect.width * 0.35f, rect.height);
            Rect valueRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelRect.width - 52f, rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            RWWidgets.Label(labelRect, condition.ShortLabel);

            if (isEditing && !isReadOnly)
            {
                ConditionValueEditor.Draw(valueRect, condition, parameters, state);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                GUI.color = RuleBuilderConstants.LabelColor;
                RWWidgets.Label(valueRect, condition.GetValueDisplay());

                if (!isReadOnly && RWWidgets.ButtonInvisible(valueRect))
                {
                    _editingConditionKey = condition.Key;
                }
            }

            if (!isReadOnly)
            {
                Rect deleteRect = new Rect(rect.xMax - 22f, rect.y + (rect.height - 18f) / 2f, 18f, 18f);
                if (RWWidgets.ButtonImage(deleteRect, TexButton.CloseXSmall, Color.white, GenUI.MouseoverColor))
                {
                    ConditionRegistry.Clear(condition.Key, parameters);
                    state.NotifyRulesModified();
                    if (_editingConditionKey == condition.Key)
                    {
                        _editingConditionKey = null;
                    }
                }
            }

            TooltipHandler.TipRegion(rect, condition.Tooltip);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private static string BuildTooltip(WorkAssignmentRule rule, int matchCount)
        {
            var sb = new System.Text.StringBuilder();

            string ruleName = string.IsNullOrEmpty(rule.Name)
                ? ConditionNameGenerator.Generate(rule.Parameters)
                : rule.Name;

            sb.AppendLine($"<b>{ruleName}</b>");
            sb.AppendLine();

            sb.AppendLine($"<color=#66CC66>Matches: {matchCount} pawns</color>");
            sb.AppendLine();

            var conditions = ConditionRegistry.GetActiveConditions(rule.Parameters);
            if (conditions.Count > 0)
            {
                sb.AppendLine("Conditions:");
                foreach (var cond in conditions)
                {
                    sb.AppendLine($"  - {cond.Label}: {cond.GetValueDisplay()}");
                }
                sb.AppendLine();
                sb.AppendLine("<i>Click a row to edit its value</i>");
            }
            else
            {
                sb.AppendLine("<i>No conditions (always matches)</i>");
            }

            return sb.ToString();
        }
    }
}
