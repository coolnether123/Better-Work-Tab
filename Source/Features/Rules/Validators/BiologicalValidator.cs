using RimWorld;
using Better_Work_Tab.Features.Rules;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Checks biological/identity constraints:
    /// - Gender
    /// - Pregnancy status
    /// - Xenotype
    /// - Trait requirements
    /// </summary>
    public static class BiologicalValidator
    {
        public static bool Validate(Pawn pawn, WorkAssignmentParameters p)
        {
            // Gender requirement
            if (p.Gender != null && pawn.gender != p.Gender)
                return false;

            // Pregnancy requirement
            if (p.IsPregnant)
            {
                bool pregnant = pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false;
                if (!pregnant)
                    return false;
            }

            // Xenotype requirement
            if (p.Xenotype != null && pawn.genes?.Xenotype != p.Xenotype)
                return false;

            // Trait requirement (def + degree)
            if (p.RequiredTrait != null)
            {
                var traitDef = p.RequiredTrait.Item1;
                var degree = p.RequiredTrait.Item2;
                bool hasTrait = pawn.story?.traits.HasTrait(traitDef, degree) ?? false;

                if (!hasTrait)
                    return false;
            }

            return true;
        }
    }
}