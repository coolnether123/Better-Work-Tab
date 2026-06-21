using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Patches
{
    // Custom work box drawer that preserves all vanilla visuals except priority number.
    public static class CustomWorkBoxDrawer
    {
        private const float CompactPriorityCellSize = 25f;
        private const float CompactPriorityOffsetY = -2f;
        private const float CompactPriorityLabelWidth = 18f;
        private const float CompactPriorityLabelHeight = 16f;

#if v0_16
        private static readonly FieldInfo LegacyWorkBoxBgBadField = typeof(WidgetsWork).GetField("WorkBoxBGTex_Bad", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo LegacyWorkBoxBgMidField = typeof(WidgetsWork).GetField("WorkBoxBGTex_Mid", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo LegacyWorkBoxBgExcellentField = typeof(WidgetsWork).GetField("WorkBoxBGTex_Excellent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo LegacyPassionMinorField = typeof(WidgetsWork).GetField("PassionWorkboxMinorIcon", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo LegacyPassionMajorField = typeof(WidgetsWork).GetField("PassionWorkboxMajorIcon", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool legacyWorkBoxTexturesLoaded;
        private static Texture2D legacyWorkBoxBgBad;
        private static Texture2D legacyWorkBoxBgMid;
        private static Texture2D legacyWorkBoxBgExcellent;
        private static Texture2D legacyPassionMinor;
        private static Texture2D legacyPassionMajor;

        public static void DrawModernLegacyPriorityCell(Rect cellRect, Pawn pawn, WorkTypeDef workType, bool incapableBecauseOfCapacities, int priority)
        {
            if (pawn == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            DrawModernLegacyPriorityBackground(cellRect, pawn, workType, incapableBecauseOfCapacities);
            DrawModernPriorityNumber(cellRect, priority);
        }

        private static void DrawModernLegacyPriorityBackground(Rect rect, Pawn pawn, WorkTypeDef workType, bool incapableBecauseOfCapacities)
        {
            LoadLegacyWorkBoxTextures();

            Color oldColor = GUI.color;
            if (incapableBecauseOfCapacities)
            {
                GUI.color = BetterWorkTabMod.Settings?.Color_IncapableBecauseOfCapacities ?? DefaultSettings.Color_IncapableBecauseOfCapacities;
            }

            float averageSkill = pawn.skills?.AverageOfRelevantSkillsFor(workType) ?? 0f;
            Texture2D baseTexture;
            Texture2D overlayTexture;
            float overlayAlpha;

            if (averageSkill <= 14f)
            {
                baseTexture = legacyWorkBoxBgBad;
                overlayTexture = legacyWorkBoxBgMid;
                overlayAlpha = averageSkill / 14f;
            }
            else
            {
                baseTexture = legacyWorkBoxBgMid;
                overlayTexture = legacyWorkBoxBgExcellent;
                overlayAlpha = (averageSkill - 14f) / 6f;
            }

            if (baseTexture != null)
            {
                GUI.DrawTexture(rect, baseTexture);
            }
            else
            {
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0.25f, 0.25f, 0.25f, oldColor.a));
            }

            if (overlayTexture != null)
            {
                Color tintColor = GUI.color;
                GUI.color = new Color(tintColor.r, tintColor.g, tintColor.b, tintColor.a * Mathf.Clamp01(overlayAlpha));
                GUI.DrawTexture(rect, overlayTexture);
            }

            DrawLegacyPassionIcon(rect, pawn, workType);
            GUI.color = oldColor;
        }

        private static void DrawLegacyPassionIcon(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType?.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                return;
            }

            Passion passion = pawn.skills.MaxPassionOfRelevantSkillsFor(workType);
            if ((int)passion <= 0)
            {
                return;
            }

            Rect position = rect;
            position.xMin = rect.center.x;
            position.yMin = rect.center.y;

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            if (passion == Passion.Minor && legacyPassionMinor != null)
            {
                GUI.DrawTexture(position, legacyPassionMinor);
            }
            else if (passion == Passion.Major && legacyPassionMajor != null)
            {
                GUI.DrawTexture(position, legacyPassionMajor);
            }

            GUI.color = oldColor;
        }

        private static void DrawModernPriorityNumber(Rect cellRect, int priority)
        {
            if (priority <= 0)
            {
                return;
            }

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
            Widgets.Label(cellRect.ContractedBy(-3f), priority.ToString());

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static void LoadLegacyWorkBoxTextures()
        {
            if (legacyWorkBoxTexturesLoaded)
            {
                return;
            }

            legacyWorkBoxTexturesLoaded = true;
            legacyWorkBoxBgBad = LegacyWorkBoxBgBadField?.GetValue(null) as Texture2D;
            legacyWorkBoxBgMid = LegacyWorkBoxBgMidField?.GetValue(null) as Texture2D;
            legacyWorkBoxBgExcellent = LegacyWorkBoxBgExcellentField?.GetValue(null) as Texture2D;
            legacyPassionMinor = LegacyPassionMinorField?.GetValue(null) as Texture2D;
            legacyPassionMajor = LegacyPassionMajorField?.GetValue(null) as Texture2D;
        }
#endif

        /// <summary>
        /// Draws a work box with vanilla visuals (background, passion flames, incapable tint)
        /// but WITHOUT the priority number or click handling.
        /// </summary>
        public static void DrawWorkBoxForSkillOverlay(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
                // This handles age-disabled work types by showing the vanilla age restriction texture and message
                int minAgeRequired;
                if (!p.IsWorkTypeDisabledByAge(wType, out minAgeRequired))
                    return;

                Rect rect = new Rect(x, y, 25f, 25f);

                // This preserves the vanilla age restriction feedback when clicking on age-disabled work
                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                {
                    MessageCompat.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, false);
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
#if v1_1 || (v1_0 || v0_19)
                DrawLegacyWorkBoxBackground(rect, p, wType);
#else
                WidgetsWork.DrawWorkBoxBackground(rect, p, wType);
#endif

                // This resets the GUI color after drawing the background to prevent affecting other UI elements
                GUI.color = Color.white;

            }
        }

        /// <summary>
        /// Draws a work box background for priority-first views without the skill-level color fill or vanilla priority number.
        /// </summary>
        public static void DrawWorkBoxForPriorityOnly(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
                return;
            }

            Rect rect = new Rect(x, y, 25f, 25f);

            if (incapableBecauseOfCapacities)
                GUI.color = BetterWorkTabMod.Settings.Color_IncapableBecauseOfCapacities;

#if v1_1 || (v1_0 || v0_19)
            DrawLegacyNeutralWorkBoxBackground(rect, p, wType);
#else
            WidgetsWork.DrawWorkBoxBackground(rect, p, wType);
#endif

            GUI.color = Color.white;
        }

#if v1_1 || (v1_0 || v0_19)
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

        private static void DrawLegacyNeutralWorkBoxBackground(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            if (WidgetsWork.WorkBoxBGTex_Mid != null)
                GUI.DrawTexture(rect, WidgetsWork.WorkBoxBGTex_Mid);
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

            int level = SkillCompat.Level(skill);
            if (level <= 3)
#if v0_16
                return WidgetsWork.WorkBoxBGTex_Bad;
#else
                return WidgetsWork.WorkBoxBGTex_Awful;
#endif
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
            Rect labelRect = new Rect(
                cellRect.xMax - CompactPriorityLabelWidth - 1f,
                cellRect.y + CompactPriorityOffsetY,
                CompactPriorityLabelWidth,
                CompactPriorityLabelHeight);

            DrawTinyPriorityLabel(labelRect, label);
        }

        public static void DrawCenteredPriority(Rect cellRect, int priority)
        {
            if (priority <= 0)
            {
                return;
            }

            string label = priority.ToString();
            GameFont font = label.Length > 1 ? GameFont.Tiny : GameFont.Small;
            Rect labelRect = new Rect(cellRect.x, cellRect.y, cellRect.width, cellRect.height);
            DrawPriorityLabel(labelRect, label, priority, font, TextAnchor.MiddleCenter);
        }

        private static void DrawTinyPriorityLabel(Rect labelRect, string label)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Widgets.Label(labelRect, label);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawPriorityLabel(Rect labelRect, string label, int priority, GameFont font, TextAnchor anchor)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = font;
            Text.Anchor = anchor;

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            Widgets.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), label);

            GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
            Widgets.Label(labelRect, label);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }
    }
}
