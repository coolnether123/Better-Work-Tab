using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Ensures work is assigned based on the pawn's social and relational status.
    /// Checks:
    /// - If the pawn has a child on the map.
    /// </summary>
    public static class SocialValidator
    {
        public static bool Validate(Pawn pawn, WorkAssignmentParameters p)
        {
            if (!p.HasChildOnMap)
                return true;

            var map = Find.CurrentMap;
            if (map == null)
                return false;

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            foreach (var child in map.mapPawns.FreeColonists
                .Where(ch => (int)ch.DevelopmentalStage < (int)DevelopmentalStage.Adult))
            {
                if (child == pawn)
                    continue;

                if (child.GetFather() == pawn || child.GetMother() == pawn)
                    return true; // Found a child!
            }
#endif

            return false; // No children found
        }
    }
}