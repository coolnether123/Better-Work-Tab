using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Visual card widget for displaying a work type in the selection grid.
    /// Shows work type icon, label, and rule configuration status.
    /// </summary>
    public static class WorkTypeCard
    {
        private const float IconSize = 32f;
        private const float BorderWidth = 2f;
        private const float RuleCountBadgeSize = 20f;

        public static bool Draw(Rect rect, WorkTypeDef workType, bool isConfigured, int ruleCount)
        {
            bool isHovered = Mouse.IsOver(rect);
            bool clicked = false;

            Color bgColor = isHovered
                ? RuleBuilderConstants.CardBackgroundHover
                : RuleBuilderConstants.CardBackground;
            Verse.Widgets.DrawBoxSolid(rect, bgColor);

            if (isConfigured)
            {
#if v1_2 || v1_1 || (v1_0 || v0_19)
                Widgets12.DrawBox(rect, (int)BorderWidth, Texture2D.whiteTexture);
#else
                Verse.Widgets.DrawBox(rect, (int)BorderWidth, Texture2D.whiteTexture);
#endif
                GUI.color = RuleBuilderConstants.CardBorderConfigured;
                Verse.Widgets.DrawBox(rect, (int)BorderWidth);
                GUI.color = Color.white;
            }
            else
            {
                Verse.Widgets.DrawBox(rect, 1);
            }

            Rect iconRect = new Rect(
                rect.x + (rect.width - IconSize) / 2f,
                rect.y + 6f,
                IconSize,
                IconSize);

            Texture2D icon = GetWorkTypeIcon(workType);
            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon);
            }

            Rect labelRect = new Rect(
                rect.x + 4f,
                iconRect.yMax + 2f,
                rect.width - 8f,
                rect.height - iconRect.height - 12f);

            var oldAnchor = Text.Anchor;
            var oldFont = Text.Font;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = isConfigured ? Color.white : new Color(0.8f, 0.8f, 0.8f);

            string label = workType.labelShort.CapitalizeFirst();
            Verse.Widgets.Label(labelRect, label);

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = Color.white;

            if (ruleCount > 0)
            {
                DrawRuleCountBadge(rect, ruleCount);
            }

            string tooltip = BuildTooltip(workType, isConfigured, ruleCount);
            TooltipHandler.TipRegion(rect, tooltip);

            if (Verse.Widgets.ButtonInvisible(rect))
            {
                clicked = true;
            }

            return clicked;
        }

        private static void DrawRuleCountBadge(Rect cardRect, int count)
        {
            Rect badgeRect = new Rect(
                cardRect.xMax - RuleCountBadgeSize - 4f,
                cardRect.y + 4f,
                RuleCountBadgeSize,
                RuleCountBadgeSize);

            Verse.Widgets.DrawBoxSolid(badgeRect, RuleBuilderConstants.SuccessColor);

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;

            Verse.Widgets.Label(badgeRect, count.ToString());

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private static Texture2D GetWorkTypeIcon(WorkTypeDef workType)
        {
            return null;
        }

        private static string BuildTooltip(WorkTypeDef workType, bool isConfigured, int ruleCount)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"<b>{workType.labelShort.CapitalizeFirst()}</b>");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(workType.description))
            {
                sb.AppendLine(workType.description);
                sb.AppendLine();
            }

            if (workType.relevantSkills != null && workType.relevantSkills.Count > 0)
            {
                sb.Append("BWT_RelevantSkills".Translate() + ": ");
                sb.AppendLine(string.Join(", ",
                    workType.relevantSkills.Select(s => s.LabelCap).ToArray()));
                sb.AppendLine();
            }

            if (isConfigured)
            {
                sb.AppendLine($"<color=#66CC66>{"BWT_RulesConfigured".Translate(ruleCount)}</color>");
            }
            else
            {
                sb.AppendLine($"<color=#999999>{"BWT_NoRulesConfigured".Translate()}</color>");
            }

            sb.AppendLine();
            sb.AppendLine($"<i>{"BWT_ClickToEdit".Translate()}</i>");

            return sb.ToString();
        }
    }
}
