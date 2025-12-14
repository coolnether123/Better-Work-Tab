using Better_Work_Tab.Features.Rules;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    public static class RuleRowWidget
    {
        public enum RowAction
        {
            None,
            Select,
            Delete,
            MoveUp,
            MoveDown
        }

        public static RowAction Draw(
            Rect rect,
            WorkAssignmentRule rule,
            int indexDisplay,
            int matchCount,
            bool isSelected,
            bool isDisabled)
        {
            if (isSelected)
                Verse.Widgets.DrawHighlightSelected(rect);
            else if (Mouse.IsOver(rect))
                Verse.Widgets.DrawHighlight(rect);

            Rect leftRect = rect.LeftPart(0.6f);
            Rect matchRect = rect.LeftPart(0.8f).RightPart(0.2f);
            Rect controlsRect = rect.RightPart(0.2f);

            Text.Anchor = TextAnchor.MiddleLeft;
            Verse.Widgets.Label(leftRect, $"{indexDisplay}. {rule.Name}");
            Text.Anchor = TextAnchor.UpperLeft;

            var oldColor = GUI.color;
            GUI.color = new Color(0.7f, 0.9f, 0.7f);
            Verse.Widgets.Label(matchRect, $"{ "BWT_Matches".Translate() } {matchCount}");
            GUI.color = oldColor;

            if (!isDisabled)
            {
                Rect deleteRect = new Rect(controlsRect.xMax - 24f, controlsRect.y + 4f, 24f, 24f);
                Rect upRect = new Rect(deleteRect.x - 26f, deleteRect.y, 24f, 24f);
                Rect downRect = new Rect(upRect.x - 26f, upRect.y, 24f, 24f);

                if (Verse.Widgets.ButtonText(upRect, "Up")) return RowAction.MoveUp;
                if (Verse.Widgets.ButtonText(downRect, "Dn")) return RowAction.MoveDown;
                if (Verse.Widgets.ButtonImage(deleteRect, TexButton.Delete, Color.white, GenUI.MouseoverColor))
                    return RowAction.Delete;
            }

            if (Verse.Widgets.ButtonInvisible(rect))
            {
                return RowAction.Select;
            }

            return RowAction.None;
        }
    }
}
