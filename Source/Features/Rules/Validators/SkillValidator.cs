using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Checks skill-based constraints:
    /// - Passion levels
    /// - Skill thresholds
    /// - Highest skill among pawns
    /// - Top-X skills for a pawn
    /// - Nth-best pawn for a worktype
    /// - Nth-best worktype for a pawn
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
            var skills = pawn.skills;

            // Passion filter
            if (p.PassionLevel > -1)
            {
                if (skills == null)
                    return false;

                if (skills.MaxPassionOfRelevantSkillsFor(wt) != (Passion)p.PassionLevel)
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
                    if (other == pawn)
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
                var topWorktypes = DefDatabase<WorkTypeDef>
                    .AllDefs
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
                        .AverageOfRelevantSkillsFor(wt);

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
                var allWorkTypes = DefDatabase<WorkTypeDef>
                    .AllDefsListForReading
                    .OrderBy(w => w.naturalPriority)  
                    .Reverse()
                    .ToList();
                allWorkTypes.RemoveDuplicates();

                var ranked = allWorkTypes
                    .Where(w => !pawn.WorkTypeIsDisabled(w))  
                    .OrderByDescending(w => pawn.skills?.AverageOfRelevantSkillsFor(w) ?? 0f)  
                    .ToList();

                if (ranked.Count >= p.IsNthBestSkill)
                {
                    if (ranked[p.IsNthBestSkill - 1] != wt)
                        return false;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }
}