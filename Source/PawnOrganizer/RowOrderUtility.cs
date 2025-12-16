using System.Collections.Generic;
using Verse;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;

namespace Better_Work_Tab.PawnOrganizer
{
    internal static class RowOrderUtility
    {
        public static int GetPawnRowOrder(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return 0;

            if (!MultiplayerBridge.Active)
                return pawn.playerSettings.displayOrder;

            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return pawn.playerSettings.displayOrder;

            return profile.PawnRowOrder.TryGetValue(pawn.thingIDNumber, out var val)
                ? val
                : pawn.playerSettings.displayOrder;
        }

        public static void SetPawnRowOrder(Pawn pawn, int order)
        {
            if (pawn?.playerSettings == null)
                return;

            if (!MultiplayerBridge.Active)
            {
                pawn.playerSettings.displayOrder = order;
                return;
            }

            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return;

            profile.PawnRowOrder[pawn.thingIDNumber] = order;
            BWTLocalProfileStore.MarkDirty();
        }

        public static void ShiftPawnRowOrdersFrom(List<Pawn> pawns, int fromOrderInclusive)
        {
            if (pawns == null)
                return;

            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                var pawn = pawns[i];
                var cur = GetPawnRowOrder(pawn);
                if (cur >= fromOrderInclusive)
                    SetPawnRowOrder(pawn, cur + 1);
            }
        }
    }
}
