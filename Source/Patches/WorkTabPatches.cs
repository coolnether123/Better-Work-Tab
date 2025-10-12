using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Rules;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.Pkcs;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Diagnostics.Eventing.Reader;
using Better_Work_Tab.UI;

namespace Better_Work_Tab.Patches
{
    // Global overlay state. Toggle is in the Work tab header.
    public static class SkillOverlayState
    {
        /// <summary>
        /// When true, the priority cell is replaced with the skill level (0–20).
        /// </summary>
        //public static bool ShowSkills = false;

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

    // Patch: Replace the priority number inside the vanilla box with the skill level.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_ReplaceNumber
    {
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            bool shiftHeld = Event.current != null && Event.current.shift;
            var wt = __instance.def.workType; // Moved this line up

            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature || (!shiftHeld))
                return true;

            // If skill overlay is not globally active, and shift is held,
            // we need to check if the current work type is one of the excluded ones.
            // TODO Currently this just skips it so numbers or check marks will show and be clickable.
            if (shiftHeld)
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

    // Helper class to determine if shift is held and what the current state is.
    // This is to make it easier to customize how features are rendered: Always, Never, only when shift is NOT held, and only when shift IS held..
    public static class ShiftHelper
    {
        public static BetterWorkTabSettings.ShowUIMode State
        {
            get
            {
                if (Event.current != null && Event.current.shift)
                    return BetterWorkTabSettings.ShowUIMode.Shifted;
                else
                    return BetterWorkTabSettings.ShowUIMode.Unshifted;
            }
        }
    }

    // Patch: Replace the priority number inside the vanilla box with the skill level.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_CornerNumber
    {
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            var worktype = __instance.def.workType; // Moved this line up

            //ensure the pawn is not dead, has work settings, and will ever perform the worktype
            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return;

            //ensure the worktype is valid and not disabled for this pawn
            if (worktype == null || pawn.WorkTypeIsDisabled(worktype))
                return;

            if ((ShiftHelper.State == BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare ||
                BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare == BetterWorkTabSettings.ShowUIMode.Always) &&
                worktype.relevantSkills.Count != 0)
            {

                DrawBestPawnForSkillBox(rect, pawn, table, __instance);

            }

            //// If skill overlay is not globally active, and shift is held,
            //// we need to check if the current work type is one of the excluded ones.
            //// TODO Currently this just skips it so numbers or check marks will show and be clickable.
            if ((ShiftHelper.State == BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers ||
                BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers == BetterWorkTabSettings.ShowUIMode.Always) &&
                worktype.relevantSkills.Count != 0)
            {
                DrawSmallSkillNumbers(rect, pawn, worktype);
            }

            return;
        }

        private static void DrawBestPawnForSkillBox(Rect rect, Pawn pawn, PawnTable table, PawnColumnWorker_WorkPriority instance)
        {
            //check agains all other pawns in the table to see if this pawn is the best at this worktype
            foreach (var otherPawn in table.cachedPawns)
            {
                //skip self
                if (otherPawn == pawn) continue;

                if (instance.Compare(pawn, otherPawn) == -1)
                {
                    return; // Found a better pawn, so exit without drawing
                }
            }

            //create a rect with a size that fits around the cell. This is hardcoded. I don't see a way to do it otherwise.
            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect rect2 = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);

            //Not including the fill color because it's unnecessary. (Fewer customization options though. But for this I don't think that's necessary. If we really want it we can add it later.)
            Widgets.DrawBoxSolidWithOutline(rect2, Color.clear, BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare, 3);
        }
        private static void DrawSmallSkillNumbers(Rect rect, Pawn pawn, WorkTypeDef worktype)
        {

            //determine if the pawn is incapable of the entire worktype
            bool incapable = IsIncapableOfWholeWorkType(pawn, worktype);


            float x = rect.x + 16f;// + (rect.width / 4f / 2f);
            float y = rect.y - 2;// -4f;// + (((rect.height - 25f) / 4f) / 2f);
            Rect boxRect = new Rect(x, y, 25f, 25f);


            // This calculates the average skill level across all skills relevant to this work type
            // We find the average because that's whe vanilla shows with a tooltip.
            int level = 0;
            if (pawn.skills != null)
            {
                float avg = pawn.skills.AverageOfRelevantSkillsFor(worktype);
                level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
            }

            //cache pre-number values
            var oldF = Text.Font;
            var oldA = Text.Anchor;
            var oldColor = GUI.color;

            //set new values for number drawing
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            //draw the number
            Widgets.Label(boxRect, level.ToString());

            //reset values
            GUI.color = oldColor;
            Text.Font = oldF;
            Text.Anchor = oldA;

            // This preserves the vanilla tooltip functionality so players can still see work type details
            TooltipHandler.TipRegion(boxRect,
                () => WidgetsWork.TipForPawnWorker(pawn, worktype, incapable),
                pawn.thingIDNumber ^ worktype.GetHashCode());
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
            return BetterWorkTabMod.Settings.Color_ExcellentSkill;                  // Green for excellent skills
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

        static void Prefix(PawnTable __instance, Vector2 position)
        {
            //Skip if the feature is disabled
            if (!BetterWorkTabMod.Settings.ShowPawnAndWorktypeHighlights) return;

            //get all worktype columns to filter out non-worktype columns
            var worktypeColumns = __instance.columns.FindAll((a) => { return a.workerClass == typeof(PawnColumnWorker_WorkPriority); });

            //if there are no worktype columns, this is not the worktab, so skip
            if (!worktypeColumns.Any()) return;

            //calculate total width and height of the table
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

            //vanilla scrollview setup. This makes it so the highlights scroll with the table, and stay within the table bounds
            Rect outRect = new Rect((int)position.x, (int)position.y + (int)__instance.cachedHeaderHeight, (int)__instance.cachedSize.x, (int)__instance.cachedSize.y - (int)__instance.cachedHeaderHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, (int)__instance.cachedHeightNoScrollbar - (int)__instance.cachedHeaderHeight);
            Widgets.BeginScrollView(outRect, ref __instance.scrollPosition, viewRect);

            //Run code for highlighting pawn rows and worktype columns. Each method handles whether to highlight based on settings.
            HighlightPawn(__instance, position, totalWidth);

            HighlightWorktype(__instance, position, totalHeight);

            Widgets.EndScrollView();

        }

        private static void HighlightPawn(PawnTable __instance, Vector2 position, float totalWidth)
        {
            //each row starts at the same x position, but the y position increases by the height of each row
            float startingY = 0;
            for (int i = 0; i < __instance.cachedPawns.Count; i++)
            {
                //create a rect that covers the entire row for this pawn
                var rect = new Rect(position.x, startingY, totalWidth, __instance.cachedRowHeights[i]);

                //highlight if selected
                if (Find.Selector.IsSelected(__instance.cachedPawns[i]))
                    //use float menu color if opened that way
                    if (BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight && worktypeToHighlight != null)
                        Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);
                    //otherwise use selected color
                    else if (BetterWorkTabMod.Settings.DoSelectedPawnHighlight)
                        Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_CursorHighlight);

                //highlight if mouse is over
                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rect))
                    Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);

                //increment startingY for next row
                startingY += __instance.cachedRowHeights[i];
            }
        }

        private static void HighlightWorktype(PawnTable __instance, Vector2 position, float totalHeight)
        {

            //each column starts at the same y position, but the x position increases by the width of each column
            float startingX = 0;
            for (int i = 0; i < __instance.columns.Count; i++)
            {
                //create a rect that covers the entire column for this worktype
                var rect = new Rect(startingX, 0, __instance.cachedColumnWidths[i], totalHeight);

                //highlight if opened from float menu
                if (BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight && worktypeToHighlight == __instance.columns[i].workType && __instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                    Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);

                //highlight if mouse is over
                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rect) && __instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                {
                    Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);
                    Widgets.DrawHighlight(rect);
                    if (!(__instance.columns[i].Worker is PawnColumnWorker_WorkPriority)) continue;
                    HighlightSimilarWorktypes(__instance.columns[i].workType, __instance.columns.Count, i, __instance, totalHeight);
                }
                //increment startingX for next column
                startingX += __instance.cachedColumnWidths[i];

                    
                                        
                    
                    
                    
                    
                    

            }

        }

        private static void HighlightSimilarWorktypes(WorkTypeDef worktype, int columnCount, int myIndex, PawnTable __instance, float totalHeight)
        {
            var relevantSkills = worktype.relevantSkills;
            float startingX = 0;

            for (int i = 0; i < columnCount; i++)
            {

                if (__instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                {
                    var rect = new Rect(startingX, 0, __instance.cachedColumnWidths[i], totalHeight);
                    foreach (var skill in relevantSkills)
                    {
                        if (__instance.columns[i].workType.relevantSkills.Contains(skill))
                        {
                            if (i != myIndex)
                            {
                                Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_SimilarWorktypeMouseOver);
                                Widgets.DrawHighlight(rect);

                            }
                        }
                    }
                }
                startingX += __instance.cachedColumnWidths[i];

            }

        }



        //Patching Window and filtering to MainTabWindow_Work because MainTabWindow_Work does not have a PreClose method to patch. This is the only way to do this.
        [HarmonyPatch(typeof(Window), nameof(Window.PreClose))]
        public static class MainTabWindow_Work_PreOpen
        {
            public static void Postfix(Window __instance)
            {
                if (!(__instance is MainTabWindow_Work)) return;

                //Clear the highlighted worktype when closing the work tab. This makes it so the highlight only persists while the tab is open, and it will reset when closed.
                PawnTable_HighlightRowAndColumn.SetWorktypeToHighlight(null);
            }
        }
    }
}