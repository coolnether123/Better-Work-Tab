using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Checks social/relational constraints:
    /// - Pawn must have children on the map (if required)
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

            foreach (var child in map.mapPawns.FreeColonists
                .Where(ch => (int)ch.DevelopmentalStage < (int)DevelopmentalStage.Adult))
            {
                if (child == pawn)
                    continue;

                if (child.GetFather() == pawn || child.GetMother() == pawn)
                    return true; // Found a child!
            }

            return false; // No children found
        }
    }
}