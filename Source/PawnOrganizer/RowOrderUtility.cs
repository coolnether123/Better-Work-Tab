using System.Collections.Generic;
using Verse;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;

namespace Better_Work_Tab.PawnOrganizer
{
    internal static class RowOrderUtility
    {
#if v0_18 || v0_17 || v0_16 || vAlpha4
        private static readonly Dictionary<int, int> LegacyPawnRowOrder = new Dictionary<int, int>();
#endif

        public static int GetPawnRowOrder(Pawn pawn)
        {
#if vAlpha4
            if (pawn == null)
                return 0;
#else
            if (pawn?.playerSettings == null)
                return 0;
#endif

#if v0_18 || v0_17 || v0_16 || vAlpha4
            if (LegacyPawnRowOrder.TryGetValue(pawn.thingIDNumber, out var legacyOrder))
                return legacyOrder;

            return pawn.thingIDNumber;
#else
            if (!MultiplayerBridge.Active)
                return pawn.playerSettings.displayOrder;

            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return pawn.playerSettings.displayOrder;

            return profile.PawnRowOrder.TryGetValue(pawn.thingIDNumber, out var val)
                ? val
                : pawn.playerSettings.displayOrder;
#endif
        }

        public static void SetPawnRowOrder(Pawn pawn, int order)
        {
#if vAlpha4
            if (pawn == null)
                return;
#else
            if (pawn?.playerSettings == null)
                return;
#endif

#if v0_18 || v0_17 || v0_16 || vAlpha4
            LegacyPawnRowOrder[pawn.thingIDNumber] = order;
#else
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
#endif
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
