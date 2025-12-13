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

        public static void SetWorktypeToHighlight(Pawn pawn, WorkTypeDef workType)
        {
            _floatMenuHighlightedPawn = pawn;
            _floatMenuHighlightedWorkType = workType;
        }

        public static void ClearWorktypeHighlight()
        {
            _floatMenuHighlightedPawn = null;
            _floatMenuHighlightedWorkType = null;
        }

        public static Pawn GetHighlightedPawn() => _floatMenuHighlightedPawn;
        public static WorkTypeDef GetHighlightedWorkType() => _floatMenuHighlightedWorkType;
    }
}
