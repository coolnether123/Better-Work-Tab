using Better_Work_Tab.Features;
using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Patches
{
    // Global overlay state. Toggle is in the Work tab header.
    public static class SkillOverlayState
    {
        /// <summary>
        /// When true, the priority cell is replaced with the skill level (0–20).
        /// </summary>
        public static bool ShowSkills = false;

        /// <summary>
        /// Returns true if the overlay should render right now (global toggle OR Shift held).
        /// </summary>
        public static bool ActiveNow
        {
            get
            {
                bool shift = Event.current != null && Event.current.shift;
                return ShowSkills || shift;
            }
        }
    }

    // Custom work box drawer that preserves all vanilla visuals except priority number.
    public static class CustomWorkBoxDrawer
    {
        /// <summary>
        /// Draws a work box with vanilla visuals (background, passion flames, incapable tint)
        /// but WITHOUT the priority number or click handling.
        /// </summary>
        public static void DrawWorkBoxForSkillOverlay(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
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

                var tex = (Texture2D)AccessTools.Field(typeof(WidgetsWork), "WorkBoxBGTex_AgeDisabled").GetValue(null);
                GUI.DrawTexture(rect, tex);
            }
            else
            {
                Rect rect = new Rect(x, y, 25f, 25f);

                // This applies the same red tint that vanilla uses for incapable work types to maintain visual consistency
                if (incapableBecauseOfCapacities)
                    GUI.color = new Color(1f, 0.3f, 0.3f);

                // This draws the work box background including passion flame effects exactly like vanilla does
                var method = typeof(WidgetsWork).GetMethod("DrawWorkBoxBackground", BindingFlags.NonPublic | BindingFlags.Static);
                method.Invoke(null, new object[] { rect, p, wType });

                // This resets the GUI color after drawing the background to prevent affecting other UI elements
                GUI.color = Color.white;

                // This intentionally skips priority number drawing and click handling because the mod will overlay skill levels instead
            }
        }
    }

    // Patch: add ONE checkbox to the top of the Work tab near "Manual priorities".
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_WorkTab_AddSingleToggle
    {
        private const float SkillToggleX_RightOfManualPriorities = 150f;
        private const float SkillToggleY_Top = 5f;
        private const float SkillToggleWidth = 230f;
        private const float SkillToggleHeight = 30f;

        private const float AutoAssignButtonWidth = 150f;
        private const float AutoAssignButtonHeight = 28f;
        private const float AutoAssignButtonMarginX = 6f;
        private const float AutoAssignButtonMarginY = 2f;

        public static void Postfix(Rect rect)
        {
            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature) return;

            if (Event.current == null || Event.current.type == EventType.Layout) return;

            Rect toggleRect = new Rect(SkillToggleX_RightOfManualPriorities, SkillToggleY_Top, SkillToggleWidth, SkillToggleHeight);

            bool show = SkillOverlayState.ShowSkills;
            Widgets.CheckboxLabeled(toggleRect, "Show skill levels (0–20)", ref show);

            if (show != SkillOverlayState.ShowSkills)
            {
                SkillOverlayState.ShowSkills = show;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            DrawAutoAssignButton(rect);
        }

        private static void DrawAutoAssignButton(Rect headerRect)
        {
            var size = new Vector2(AutoAssignButtonWidth, AutoAssignButtonHeight);
            var btn = new Rect(headerRect.xMax - size.x - AutoAssignButtonMarginX, headerRect.y + AutoAssignButtonMarginY, size.x, size.y);

            if (Widgets.ButtonText(btn, "Auto Assign Work"))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                var assigner = new AutoWorkAssigner(BetterWorkTabMod.Settings);
                assigner.ApplyAutoAssignments();
            }
        }
    }

    // Patch: Replace the priority number inside the vanilla box with the skill level.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_ReplaceNumber
    {
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature || !SkillOverlayState.ActiveNow)
                return true;

            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return false;

            var wt = __instance.def.workType;
            if (wt == null || pawn.WorkTypeIsDisabled(wt))
                return false;

            bool incapable = IsIncapableOfWholeWorkType(pawn, wt);

            float x = rect.x + ((rect.width - 25f) / 2f);
            float y = rect.y + 2.5f;
            Rect boxRect = new Rect(x, y, 25f, 25f);

            if (Event.current.type == EventType.Repaint)
            {
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(x, y, pawn, wt, incapable);
            }

            // This calculates the average skill level across all skills relevant to this work type
            int level = 0;
            if (pawn.skills != null)
            {
                float avg = pawn.skills.AverageOfRelevantSkillsFor(wt);
                level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
            }

            var oldF = Text.Font;
            var oldA = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldF;
            Text.Anchor = oldA;

            // This preserves the vanilla tooltip functionality so players can still see work type details
            TooltipHandler.TipRegion(boxRect,
                () => WidgetsWork.TipForPawnWorker(pawn, wt, incapable),
                pawn.thingIDNumber ^ wt.GetHashCode());

            return false;
        }

        // This determines if a pawn is incapable of a work type by checking if they can do at least one work giver
        private static bool IsIncapableOfWholeWorkType(Pawn p, WorkTypeDef work)
        {
            for (int i = 0; i < work.workGiversByPriority.Count; i++)
            {
                bool canDoThisGiver = true;
                var reqs = work.workGiversByPriority[i].requiredCapacities;
                for (int j = 0; j < reqs.Count; j++)
                {
                    if (!p.health.capacities.CapableOf(reqs[j]))
                    {
                        canDoThisGiver = false;
                        break;
                    }
                }
                if (canDoThisGiver)
                    return false;
            }
            return true;
        }

        // This provides color coding for skill levels to make them easier to read at a glance
        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3) return new Color(0.82f, 0.25f, 0.25f);  // Red for very low skills
            if (level <= 9) return new Color(0.95f, 0.75f, 0.20f);  // Orange for low skills
            if (level <= 15) return new Color(0.95f, 0.95f, 0.95f); // White for good skills
            return new Color(0.35f, 0.85f, 0.35f);                  // Green for excellent skills
        }
    }
}