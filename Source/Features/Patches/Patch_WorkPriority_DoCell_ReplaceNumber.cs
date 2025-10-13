using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
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
}