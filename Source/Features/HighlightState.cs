using RimWorld;
using Verse;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Tracks persistent highlight targets (e.g., selected via float menu).
    /// </summary>
    public static class HighlightState
    {
        private static Pawn _floatMenuHighlightedPawn;
        private static WorkTypeDef _floatMenuHighlightedWorkType;
        private static WorkGiverDef _floatMenuHighlightedWorkGiver;

        public static void SetWorktypeToHighlight(Pawn pawn, WorkTypeDef workType)
        {
            _floatMenuHighlightedPawn = pawn;
            _floatMenuHighlightedWorkType = workType;
            _floatMenuHighlightedWorkGiver = null;
        }

        public static void SetSubWorkGiverToHighlight(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            _floatMenuHighlightedPawn = pawn;
            _floatMenuHighlightedWorkType = workType;
            _floatMenuHighlightedWorkGiver = workGiver;
        }

        public static void ClearWorktypeHighlight()
        {
            _floatMenuHighlightedPawn = null;
            _floatMenuHighlightedWorkType = null;
            _floatMenuHighlightedWorkGiver = null;
        }

        public static Pawn GetHighlightedPawn() => _floatMenuHighlightedPawn;
        public static WorkTypeDef GetHighlightedWorkType() => _floatMenuHighlightedWorkType;
        public static WorkGiverDef GetHighlightedWorkGiver() => _floatMenuHighlightedWorkGiver;
    }
}
