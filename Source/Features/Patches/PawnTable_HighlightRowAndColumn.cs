using HarmonyLib;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    public static class PawnTable_HighlightRowAndColumn
    {
        public static WorkTypeDef worktypeToHighlight { get; set; }


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
                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rect) && __instance.columns[i].Worker is PawnColumnWorker_WorkPriority)
                {
                    Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    //Log.Message($"Using hover color: {useColor}, Custom enabled: {BetterWorkTabMod.Settings.UseCustomMouseHoverHighlight}");
                    Widgets.DrawBoxSolid(rect, useColor);
                    Widgets.DrawHighlight(rect);
                }

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

                    Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    //Log.Message($"[HighlightWorktype] Custom enabled: {BetterWorkTabMod.Settings.UseCustomMouseHoverHighlight}, Using color: R={useColor.r:F2} G={useColor.g:F2} B={useColor.b:F2} A={useColor.a:F2}");

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

    }
}