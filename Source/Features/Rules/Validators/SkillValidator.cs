using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Ensures work is assigned based on the pawn's skills and passions.
    /// Checks:
    /// - Passion level matches the requirement.
    /// - Skill level is within the specified range.
    /// - Pawn has the highest skill for the work type.
    /// - Work type is one of the pawn's top X skills.
    /// - Pawn is the Nth best for the work type.
    /// - Work type is the pawn's Nth best skill.
    /// </summary>
    public static class SkillValidator
    {
        public static bool Validate(
            Pawn pawn,
            WorkTypeDef wt,
            List<Pawn> allPawns,
            WorkAssignmentParameters p
        )
        {
            if (pawn?.skills == null)  
                return false;

            var skills = pawn.skills;

            // Passion filter
            if (p.PassionLevel > -1)
            {
                if (skills == null)
                    return false;

                if (MaxPassionOfRelevantSkillsFor(skills, wt) != (Passion)p.PassionLevel)
                    return false;
            }

            // Skill greater than threshold
            if (p.SkillLevelGreaterThan > -1)
            {
                if (skills == null)
                    return false;

                if (skills.AverageOfRelevantSkillsFor(wt) <= p.SkillLevelGreaterThan)
                    return false;
            }

            // Skill less than threshold
            if (p.SkillLevelLessThan > -1)
            {
                if (skills == null)
                    return false;

                if (skills.AverageOfRelevantSkillsFor(wt) >= p.SkillLevelLessThan)
                    return false;
            }

            // Must be the highest among pawns (ties allowed for equals)
            if (p.HasHighestSkill)
            {
                foreach (var other in allPawns)
                {
                    if (other?.skills == null) 
                        continue;

                    float my = skills?.AverageOfRelevantSkillsFor(wt) ?? 0f;
                    float oth = other.skills?.AverageOfRelevantSkillsFor(wt) ?? 0f;

                    if (my < oth)
                        return false;
                }
            }

            // Worktype must be in pawn's top X skills
            if (p.IsTopXSkill > 0)
            {
                var topWorktypes = WorkAssignmentRule.AllWorkTypes
                    .Where(w => !w.alwaysStartActive && !pawn.WorkTypeIsDisabled(w))  
                    .OrderByDescending(w => pawn.skills?.AverageOfRelevantSkillsFor(w) ?? 0f)  
                    .ToList();

                if (!topWorktypes.Take(p.IsTopXSkill).Contains(wt))
                    return false;
            }

            // Pawn must be Nth-best for this worktype (ties allowed)
            if (p.IsNthBestPawn > 0)
            {
                var sorted = allPawns
                    .Where(pp => !pp.WorkTypeIsDisabled(wt))
                    .OrderByDescending(pp => pp.skills?.AverageOfRelevantSkillsFor(wt) ?? 0f)
                    .ToList();

                if (sorted.Count >= p.IsNthBestPawn)
                {
                    float nthSkill = sorted[p.IsNthBestPawn - 1].skills
                        ?.AverageOfRelevantSkillsFor(wt) ?? 0f;

                    var tier = sorted
                        .Where(pp => pp.skills.AverageOfRelevantSkillsFor(wt) == nthSkill)
                        .ToList();

                    if (!tier.Contains(pawn))
                        return false;
                }
                else
                {
                    return false;
                }
            }

            // Worktype must be pawn's Nth best skill
            if (p.IsNthBestSkill > 0)
            {
                var allWorkTypes = WorkAssignmentRule.AllWorkTypes;

                var ranked = allWorkTypes
                    .Where(w => !pawn.WorkTypeIsDisabled(w))  
                    .OrderByDescending(w => pawn.skills?.AverageOfRelevantSkillsFor(w) ?? 0f)  
                    .ToList();

                if (ranked.Count < p.IsNthBestSkill)
                    return false; // Not enough worktypes

                var nthBestWorktype = ranked[p.IsNthBestSkill - 1];
                float nthBestValue = pawn.skills?.AverageOfRelevantSkillsFor(nthBestWorktype) ?? 0f;
                float wtValue = pawn.skills?.AverageOfRelevantSkillsFor(wt) ?? 0f;

                if (wtValue != nthBestValue)
                    return false;
            }

            return true;
        }

        private static Passion MaxPassionOfRelevantSkillsFor(Pawn_SkillTracker skills, WorkTypeDef workType)
        {
#if vAlpha4
            Passion max = Passion.None;
            if (skills == null || workType?.relevantSkills == null)
                return max;

            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillRecord skill = skills.GetSkill(workType.relevantSkills[i]);
                if (skill != null && (int)skill.passion > (int)max)
                    max = skill.passion;
            }

            return max;
#else
            return skills.MaxPassionOfRelevantSkillsFor(workType);
#endif
        }
    }
}
