using Better_Work_Tab.Features;
using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;
using LudeonTK;

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
                GUI.DrawTexture(rect, WidgetsWork.WorkBoxBGTex_AgeDisabled);

            }
            else
            {
                Rect rect = new Rect(x, y, 25f, 25f);

                // This applies the same red tint that vanilla uses for incapable work types to maintain visual consistency
                if (incapableBecauseOfCapacities)
                    GUI.color = BetterWorkTabMod.Settings.Color_IncapableBecauseOfCapacities;

                // This draws the work box background including passion flame effects exactly like vanilla does
                WidgetsWork.DrawWorkBoxBackground(rect, p, wType);

                // This resets the GUI color after drawing the background to prevent affecting other UI elements
                GUI.color = Color.white;

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
            bool shiftHeld = Event.current != null && Event.current.shift;
            var wt = __instance.def.workType; // Moved this line up

            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature || (!SkillOverlayState.ShowSkills && !shiftHeld))
                return true;

            // If skill overlay is not globally active, and shift is held,
            // we need to check if the current work type is one of the excluded ones.
            // TODO Currently this just skips it so numbers or check marks will show and be clickable.
            if (!SkillOverlayState.ShowSkills && shiftHeld)
            {
                if (wt.relevantSkills.Count == 0)
                {
                    return false; // Do not show skill overlay for these work types when only shift is held
                }
            }

            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return false;

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

    // Patch: Replace the priority number inside the vanilla box with the skill level.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_CornerNumber
    {
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            //Log.Message("Postfix called");
            bool shiftHeld = Event.current != null && Event.current.shift;
            var worktype = __instance.def.workType; // Moved this line up

            //if (!(!BetterWorkTabMod.Settings.enableSkillOverlayFeature || (!SkillOverlayState.ShowSkills && !shiftHeld)))
            //    return;

            //// If skill overlay is not globally active, and shift is held,
            //// we need to check if the current work type is one of the excluded ones.
            //// TODO Currently this just skips it so numbers or check marks will show and be clickable.
            if (SkillOverlayState.ShowSkills || shiftHeld)
            {
                return; //only show if shift is not held
            }
            if (worktype.relevantSkills.Count == 0)
            {
                return; // Do not show skill overlay for these work types when only shift is held
            }
            //}

            //ensure the pawn is not dead, has work settings, and will ever perform the worktype
            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return;

            //ensure the worktype is valid and not disabled for this pawn
            if (worktype == null || pawn.WorkTypeIsDisabled(worktype))
                return;

            //determine if the pawn is incapable of the entire worktype
            bool incapable = IsIncapableOfWholeWorkType(pawn, worktype);


            float x = rect.x + 16f;// + (rect.width / 4f / 2f);
            float y = rect.y - 2;// -4f;// + (((rect.height - 25f) / 4f) / 2f);
            Rect boxRect = new Rect(x, y, 25f, 25f);

            //if (Event.current.type == EventType.Repaint)
            //{
            //    Widgets.TextArea(boxRect, "THIS IS THE THING AND IT'S HUGE SO IT CAN SEE IT");
            //    //CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(x, y, pawn, worktype, incapable);
            //}

            // This calculates the average skill level across all skills relevant to this work type
            int level = 0;
            if (pawn.skills != null)
            {
                float avg = pawn.skills.AverageOfRelevantSkillsFor(worktype);
                level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
            }

            var oldF = Text.Font;
            var oldA = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldF;
            Text.Anchor = oldA;

            // This preserves the vanilla tooltip functionality so players can still see work type details
            TooltipHandler.TipRegion(boxRect,
                () => WidgetsWork.TipForPawnWorker(pawn, worktype, incapable),
                pawn.thingIDNumber ^ worktype.GetHashCode());

            return;
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
            if (level <= 3) return BetterWorkTabMod.Settings.Color_VeryLowSkill;  // Red for very low skills
            if (level <= 9) return BetterWorkTabMod.Settings.Color_LowSkill;  // Orange for low skills
            if (level <= 15) return BetterWorkTabMod.Settings.Color_GoodLowSkill; // White for good skills
            return BetterWorkTabMod.Settings.Color_ExcellentLowSkill;                  // Green for excellent skills
        }
    }


    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    public static class PawnTable_HighlightRowAndColumn
    {
        private static WorkTypeDef worktypeToHighlight = null;

        public static void SetWorktypeToHighlight(WorkTypeDef wt)
        {
            worktypeToHighlight = wt;
        }

        //public static void ResetTransparency()
        //{
        //    transparency = 0.5f;
        //}

        static void Prefix(PawnTable __instance, Vector2 position)
        {

            if (!BetterWorkTabMod.Settings.ShowPawnAndWorktypeHighlights) return;

            var worktypeColumns = __instance.columns.FindAll((a) => { return a.workerClass == typeof(PawnColumnWorker_WorkPriority); });

            if (!worktypeColumns.Any()) return;

            float totalWidth = 0f;
            foreach (var col in __instance.cachedColumnWidths)
            {
                totalWidth += col;
            }

            float totalHeight = 0f;
            foreach (var col in __instance.cachedRowHeights)
            {
                totalHeight += col;
            }

            //float rowStartingPoint = position.y;
            //for (int i = 0; i < rowIndex; i++)
            //{
            //    rowStartingPoint += __instance.cachedRowHeights[i];
            //}


            Rect outRect = new Rect((int)position.x, (int)position.y + (int)__instance.cachedHeaderHeight, (int)__instance.cachedSize.x, (int)__instance.cachedSize.y - (int)__instance.cachedHeaderHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, (int)__instance.cachedHeightNoScrollbar - (int)__instance.cachedHeaderHeight);
            Widgets.BeginScrollView(outRect, ref __instance.scrollPosition, viewRect);
                HighlightSelectedPawn(__instance, position, totalWidth);

               HighlightWorktype(__instance, position, totalHeight);
            
            Widgets.EndScrollView();

        }

        private static void HighlightSelectedPawn(PawnTable __instance, Vector2 position, float totalWidth)
        {

            float startingY = 0;// position.y /*+ __instance.HeaderHeight*/;
            for (int i = 0; i < __instance.cachedPawns.Count; i++)
            {
                var rect = new Rect(position.x, startingY, totalWidth, __instance.cachedRowHeights[i]);
                if (Find.Selector.IsSelected(__instance.cachedPawns[i]) )
                    if(BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight && worktypeToHighlight != null)
                        Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_FloatMenuPawnAndWorktypeHighlight);
                    else if (BetterWorkTabMod.Settings.DoSelectedPawnHighlight)
                        Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_CursorPawnAndWorktypeHighlight);

                
                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rect))
                    Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_MouseHoverPawnAndWorktypeHighlight);

                startingY += __instance.cachedRowHeights[i];
            }
        }

        private static void HighlightWorktype(PawnTable __instance, Vector2 position, float totalHeight)
        {
            float startingX = 0;// position.x;
            for (int i = 0; i < __instance.columns.Count; i++)
            {

            float columnStartingPoint = position.x;

            var rect = new Rect(startingX, 0 /*+ __instance.HeaderHeight*/, __instance.cachedColumnWidths[i], totalHeight);

            if (BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight && worktypeToHighlight == __instance.columns[i].workType && __instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_FloatMenuPawnAndWorktypeHighlight);

            if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rect) && __instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_MouseHoverPawnAndWorktypeHighlight);
                startingX += __instance.cachedColumnWidths[i];
            }

        }
    }

    //I hate having to do it this way but I don't think there's another ways because harmony won't patch inhertiated methods
    //That is correct. There is no better way.

    [HarmonyPatch(typeof(Window), nameof(Window.PreClose))]
    public static class MainTabWindow_Work_PreOpen
    {
        public static void Postfix(Window __instance)
        {
            if (!(__instance is MainTabWindow_Work)) return;

            PawnTable_HighlightRowAndColumn.SetWorktypeToHighlight(null);
        }
    }
}