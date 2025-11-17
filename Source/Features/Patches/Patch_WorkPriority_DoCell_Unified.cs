using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// A unified patch for PawnColumnWorker_WorkPriority.DoCell that handles all custom drawing
    /// for the skill overlay feature. It replaces the vanilla priority number with skill levels
    /// and adds optional indicators for the best pawn for a skill.
    /// This single prefix patch replaces the separate ...ReplaceNumber and ...CornerNumber patches.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_Unified
    {
        /// <summary>
        /// This Prefix completely overrides the original DoCell method to implement custom drawing.
        /// It returns true only if no custom drawing is performed, allowing vanilla behavior to proceed.
        /// </summary>
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            var workType = __instance.def.workType;

            if (ColumnHoverManager.HoveredWorkType == workType)
            {
                // If so, we ALWAYS let the vanilla method run to show the priority number.
                // This effectively "peeks" through the skill overlay for this specific column.
                return true;
            }
            // If the main skill overlay feature is disabled, do nothing and let vanilla run.
            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature)
            {
                return true;
            }

            // The big skill number only appears when Shift is held.
            if (ShiftHelper.State != BetterWorkTabSettings.ShowUIMode.Shifted)
            {
                return true; // Not holding shift, let vanilla run.
            }

            // --- Shift is held, so we take over drawing ---

            

            // "Hide" non-skill-based work types by drawing nothing.
            if (workType.relevantSkills.Count == 0)
            {
                return false; // Skip vanilla, draw nothing.
            }

            // Basic validation
            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
            {
                return false; // Skip vanilla, draw nothing.
            }

            // Draw the full replacement UI (background, big skill number)
            bool incapable = IsIncapableOfWholeWorkType(pawn, workType);
            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect boxRect = new Rect(x, y, 25f, 25f);

            if (Event.current.type == EventType.Repaint)
            {
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(x, y, pawn, workType, incapable);
            }

            int level = GetSkillLevel(pawn, workType);
            DrawBigSkillNumber(boxRect, level);

            TooltipHandler.TipRegion(boxRect, () => WidgetsWork.TipForPawnWorker(pawn, workType, incapable), pawn.thingIDNumber ^ workType.GetHashCode());

            // By returning false, we prevent the vanilla DoCell from running.
            return false;
        }

        /// <summary>
        /// The Postfix handles the 'add-on' scenarios.
        /// It runs AFTER the original DoCell method.
        /// </summary>
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            var workType = __instance.def.workType;
            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            bool overlayActive = BetterWorkTabMod.Settings.enableSkillOverlayFeature &&
                                 ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
            if (overlayActive)
            {
                DrawPriorityOnHover(rect, pawn, workType);
                return;
            }

            if (workType.relevantSkills.Count == 0)
            {
                return;
            }

            // Check if we should show the small skill numbers in the corner.
            if (ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers, ShiftHelper.State))
            {
                int level = GetSkillLevel(pawn, workType);
                DrawSmallSkillNumbers(rect, level);
            }

            // Check if we should show the "best pawn for skill" outline.
            if (ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare, ShiftHelper.State))
            {
                DrawBestPawnForSkillBox(rect, pawn, table, __instance);
            }
        }

        // --- All Helper Methods ---

        private static bool ShouldShowUI(BetterWorkTabSettings.ShowUIMode mode, BetterWorkTabSettings.ShowUIMode currentState)
        {
            return mode == BetterWorkTabSettings.ShowUIMode.Always || mode == currentState;
        }

        private static void DrawPriorityOnHover(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            if (!Mouse.IsOver(rect))
            {
                return;
            }

            bool showSmallSkills = ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers, ShiftHelper.State);
            if (showSmallSkills)
            {
                int level = GetSkillLevel(pawn, workType);
                DrawSmallSkillNumbers(rect, level);
            }

            if (!Current.Game.playSettings.useWorkPriorities)
            {
                return;
            }

            int priority = pawn.workSettings?.GetPriority(workType) ?? 0;
            if (priority <= 0)
            {
                return;
            }

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = Color.white;
            Widgets.Label(rect.ContractedBy(2f), priority.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static int GetSkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn.skills == null) return 0;
            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            return Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
        }

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

        private static void DrawSmallSkillNumbers(Rect rect, int level)
        {
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

        private static void DrawBestPawnForSkillBox(Rect rect, Pawn pawn, PawnTable table, PawnColumnWorker_WorkPriority instance)
        {
            foreach (var otherPawn in table.cachedPawns)
            {
                if (otherPawn == pawn) continue;
                if (instance.Compare(pawn, otherPawn) < 0) return;
            }

            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect outlineRect = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);

            Widgets.DrawBoxSolidWithOutline(outlineRect, Color.clear, BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare, 3);
        }

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
                if (canDoThisGiver) return false;
            }
            return true;
        }

        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3) return BetterWorkTabMod.Settings.Color_VeryLowSkill;
            if (level <= 9) return BetterWorkTabMod.Settings.Color_LowSkill;
            if (level <= 15) return BetterWorkTabMod.Settings.Color_GoodLowSkill;
            return BetterWorkTabMod.Settings.Color_ExcellentSkill;
        }
    }
}
