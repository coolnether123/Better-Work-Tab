using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Patches
{
    // Custom work box drawer that preserves all vanilla visuals except priority number.
    public static class CustomWorkBoxDrawer
    {
        private const float CompactPriorityPaddingX = 1f;
        private const float CompactPriorityPaddingY = -1f;
        private const float CompactPriorityHeight = 12f;
        private const float CompactPriorityBaseWidth = 14f;
        private const float CompactPriorityExtraWidthPerDigit = 6f;

        /// <summary>
        /// Draws a work box with vanilla visuals (background, passion flames, incapable tint)
        /// but WITHOUT the priority number or click handling.
        /// </summary>
        public static void DrawWorkBoxForSkillOverlay(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
#if !v1_3 && !v1_2 && !v1_1 && !v1_0
                // This handles age-disabled work types by showing the vanilla age restriction texture and message
                int minAgeRequired;
                if (!p.IsWorkTypeDisabledByAge(wType, out minAgeRequired))
                    return;

                Rect rect = new Rect(x, y, 25f, 25f);

                // This preserves the vanilla age restriction feedback when clicking on age-disabled work
                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                {
                    Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, false);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                GUI.DrawTexture(rect, WidgetsWork.WorkBoxBGTex_AgeDisabled);
#endif
            }
            else
            {
                Rect rect = new Rect(x, y, 25f, 25f);

                // This applies the same red tint that vanilla uses for incapable work types to maintain visual consistency
                if (incapableBecauseOfCapacities)
                    GUI.color = BetterWorkTabMod.Settings.Color_IncapableBecauseOfCapacities;

                // This draws the work box background including passion flame effects exactly like vanilla does
#if v1_1 || v1_0
                DrawLegacyWorkBoxBackground(rect, p, wType);
#else
                WidgetsWork.DrawWorkBoxBackground(rect, p, wType);
#endif

                // This resets the GUI color after drawing the background to prevent affecting other UI elements
                GUI.color = Color.white;

            }
        }

#if v1_1 || v1_0
        private static void DrawLegacyWorkBoxBackground(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            SkillRecord skill = GetFirstRelevantSkill(pawn, workType);
            Texture2D background = GetLegacyWorkBoxBackground(skill);

            if (background != null)
                GUI.DrawTexture(rect, background);

            if (skill == null)
                return;

            if (skill.passion == Passion.Minor && WidgetsWork.PassionWorkboxMinorIcon != null)
                GUI.DrawTexture(rect, WidgetsWork.PassionWorkboxMinorIcon);
            else if (skill.passion == Passion.Major && WidgetsWork.PassionWorkboxMajorIcon != null)
                GUI.DrawTexture(rect, WidgetsWork.PassionWorkboxMajorIcon);
        }

        private static SkillRecord GetFirstRelevantSkill(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType?.relevantSkills == null || workType.relevantSkills.Count == 0)
                return null;

            return pawn.skills.GetSkill(workType.relevantSkills[0]);
        }

        private static Texture2D GetLegacyWorkBoxBackground(SkillRecord skill)
        {
            if (skill == null)
                return WidgetsWork.WorkBoxBGTex_Mid;

            int level = skill.Level;
            if (level <= 3)
                return WidgetsWork.WorkBoxBGTex_Awful;
            if (level <= 7)
                return WidgetsWork.WorkBoxBGTex_Bad;
            if (level <= 13)
                return WidgetsWork.WorkBoxBGTex_Mid;

            return WidgetsWork.WorkBoxBGTex_Excellent;
        }
#endif

        /// <summary>
        /// Draws a compact priority label for custom work cell views.
        /// </summary>
        public static void DrawCompactPriority(Rect cellRect, int priority)
        {
            if (priority <= 0)
            {
                return;
            }

            string label = priority.ToString();
            float width = CompactPriorityBaseWidth + Mathf.Max(0, label.Length - 1) * CompactPriorityExtraWidthPerDigit;
            Rect labelRect = new Rect(
                cellRect.x + CompactPriorityPaddingX,
                cellRect.y + CompactPriorityPaddingY,
                width,
                CompactPriorityHeight);

            DrawPriorityLabel(labelRect, label, priority, GameFont.Tiny, TextAnchor.MiddleCenter);
        }

        private static void DrawPriorityLabel(Rect labelRect, string label, int priority, GameFont font, TextAnchor anchor)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = MaxPriorityLogic.GetPriorityColor(priority);
            Widgets.Label(labelRect, label);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }
    }
}
