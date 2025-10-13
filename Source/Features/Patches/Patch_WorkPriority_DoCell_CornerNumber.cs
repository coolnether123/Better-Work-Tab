using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
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
}