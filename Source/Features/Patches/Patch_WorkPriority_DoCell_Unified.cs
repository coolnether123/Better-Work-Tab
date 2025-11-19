using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{

    /// <summary>
    /// A unified patch for PawnColumnWorker_WorkPriority.DoCell that handles all custom drawing
    /// for the skill overlay feature. It layers our drawing on top of vanilla, so vanilla keeps
    /// all click/priority logic. We then replace or augment visuals depending on Shift state.
    ///
    /// Behavior matrix:
    /// - No Shift:
    ///     Vanilla priority box in center (clickable) +
    ///     optional small skill numbers / "best pawn" outline.
    ///
    /// - Shift held, NOT hovering a cell:
    ///     Big center = SKILL (colored)
    ///     Top-right  = nothing
    ///     Cell still clickable because vanilla already handled input.
    ///
    /// - Shift held, hovering a cell:
    ///     Big center = PRIORITY (vanilla)
    ///     Top-right  = small SKILL (colored)
    ///     Cell still clickable (vanilla).
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_Unified
    {
        /// <summary>
        /// This Prefix prevents vanilla DoCell from running for non-skill work types
        /// when Shift is held and the overlay feature is enabled. This hides the
        /// priority number for work types that don't use skills.
        /// </summary>
        public static bool Prefix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            var workType = __instance.def.workType;

            // Basic validation: let vanilla handle invalid cases.
            if (pawn.Dead
                || pawn.workSettings == null
                || !pawn.workSettings.EverWork
                || workType == null
                || pawn.WorkTypeIsDisabled(workType))
            {
                return true; // Let vanilla run.
            }

            bool featureEnabled = BetterWorkTabMod.Settings.enableSkillOverlayFeature;
            bool shiftHeld = ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;

            // If feature is off or shift not held, let vanilla run normally.
            if (!featureEnabled || !shiftHeld)
            {
                return true;
            }

            // If this is a NON-SKILL work type and shift is held, skip vanilla entirely.
            // The Postfix will do nothing for these, resulting in an empty cell.
            if (workType.relevantSkills.Count == 0)
            {
                return false; // Skip vanilla, draw nothing.
            }

            // For skill-based work types when shift is held, let vanilla run first.
            // The Postfix will then draw our overlay on top.
            return true;
        }
        /// <summary>
        /// We let vanilla DoCell run first so it draws the normal priority box and
        /// handles all mouse input. This Postfix then draws our overlays on top.
        /// </summary>
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            var workType = __instance.def.workType;

            // Basic validation: dead / no work settings / never works / invalid work type.
            if (pawn.Dead
                || pawn.workSettings == null
                || !pawn.workSettings.EverWork
                || workType == null
                || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            // If the column header hover "peek" is active, do not override vanilla.
            // This allows you to see pure priorities for that work type.
            if (ColumnHoverManager.HoveredWorkType == workType)
            {
                return;
            }

            bool featureEnabled = BetterWorkTabMod.Settings.enableSkillOverlayFeature;
            bool shiftHeld = ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;

            // If the main feature is disabled, do not apply any overlay.
            if (!featureEnabled)
            {
                return;
            }

            // If Shift is NOT held, just return and let vanilla draw with its normal extras.
            // Vanilla's priority box will display, and old overlay systems can add their extras.
            if (!shiftHeld)
            {
                return;
            }

            // -----------------------------------------------
            // From here: Shift IS held AND feature is enabled.
            // Only apply overlay logic to skill-based work types.
            // Non-skill work types should display normally via vanilla.
            // -----------------------------------------------

            // If this work type doesn't use skills, there's nothing meaningful to overlay.
            // Return and let vanilla display it unchanged.
            if (workType.relevantSkills.Count == 0)
            {
                return;
            }

            int skillLevel = GetSkillLevel(pawn, workType);
            bool hovering = Mouse.IsOver(rect);

            // Reconstruct the inner 25x25 work box rect (same as vanilla).
            float boxX = rect.x + (rect.width - 25f) / 2f;
            float boxY = rect.y + 2.5f;
            Rect boxRect = new Rect(boxX, boxY, 25f, 25f);

            if (!hovering)
            {
                // -----------------------------------------------
                // CASE 2: Shift held, NOT hovering
                //  - Big center: SKILL (colored)
                //  - Nothing in top-right
                //  - Cell remains clickable (vanilla already set it up).
                // -----------------------------------------------

                bool incapable = IsIncapableOfWholeWorkType(pawn, workType);

                if (Event.current.type == EventType.Repaint)
                {
                    // Draw the vanilla-like background (age/disabled tint, passion flames)
                    // but does NOT draw any number or handle clicks.
                    CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(
                        boxX,
                        boxY,
                        pawn,
                        workType,
                        incapable);
                }

                // Draw big colored SKILL number in the center.
                DrawBigSkillNumber(boxRect, skillLevel);
            }
            else
            {
                // -----------------------------------------------
                // CASE 3: Shift held, HOVERING over this cell
                //  - Big center: PRIORITY (vanilla, unchanged)
                //  - Small top-right: SKILL (colored)
                //  - Cell is still clickable (vanilla).
                // -----------------------------------------------

                // Do NOT redraw the box, so vanilla's priority remains visible.
                // Only add the small colored skill number in the corner.
                DrawSmallSkillNumbers(rect, skillLevel);
            }

            // Best-pawn outline: shown regardless of hover state, if configured.
            if (ShouldShowUI(
                    BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare,
                    ShiftHelper.State))
            {
                DrawBestPawnForSkillBox(rect, pawn, table, __instance);
            }
        }

        // --------------------------------------------------------------------
        // Helper methods
        // --------------------------------------------------------------------

        /// <summary>
        /// Returns true if a UI element configured with <paramref name="mode"/>
        /// should be visible in the current Shift state.
        /// </summary>
        private static bool ShouldShowUI(
            BetterWorkTabSettings.ShowUIMode mode,
            BetterWorkTabSettings.ShowUIMode currentState)
        {
            return mode == BetterWorkTabSettings.ShowUIMode.Always
                || mode == currentState;
        }

        /// <summary>
        /// Computes the average of relevant skills for this work type and clamps it to 0..20.
        /// </summary>
        private static int GetSkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn.skills == null)
            {
                return 0;
            }

            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            return Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
        }

        /// <summary>
        /// Draws a large, centered skill number in the given rect, colored by level.
        /// </summary>
        private static void DrawBigSkillNumber(Rect rect, int level)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            Widgets.Label(rect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        /// <summary>
        /// Draws a small skill number in the top-right corner of the cell rect.
        /// </summary>
        private static void DrawSmallSkillNumbers(Rect rect, int level)
        {
            // Corner box anchored at top-right of the priority cell, same position as old overlay.
            Rect boxRect = new Rect(rect.x + 16f, rect.y - 2f, 25f, 25f);

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);

            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        /// <summary>
        /// Draws the "best pawn for this work type" outline box around the inner work box
        /// if this pawn is ranked highest compared to others in the table.
        /// </summary>
        private static void DrawBestPawnForSkillBox(
            Rect rect,
            Pawn pawn,
            PawnTable table,
            PawnColumnWorker_WorkPriority instance)
        {
            foreach (var otherPawn in table.cachedPawns)
            {
                if (otherPawn == pawn)
                {
                    continue;
                }

                // Compare uses the same ordering as the Work column (higher skill first).
                if (instance.Compare(pawn, otherPawn) < 0)
                {
                    return;
                }
            }

            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect outlineRect = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);

            Widgets.DrawBoxSolidWithOutline(
                outlineRect,
                Color.clear,
                BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare,
                3);
        }

        /// <summary>
        /// Determines if the pawn is incapable of the entire work type by checking
        /// if they can do at least one of the work givers.
        /// </summary>
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
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the color to use for a given skill level.
        /// </summary>
        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3)
            {
                return BetterWorkTabMod.Settings.Color_VeryLowSkill;
            }

            if (level <= 9)
            {
                return BetterWorkTabMod.Settings.Color_LowSkill;
            }

            if (level <= 15)
            {
                return BetterWorkTabMod.Settings.Color_GoodLowSkill;
            }

            return BetterWorkTabMod.Settings.Color_ExcellentSkill;
        }
    }
}