using System.Collections.Generic;
using RimWorld;
using Verse;
using System;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This class handles providing context data for work assignment rules.
    /// </summary>
    [Obsolete("Use WorkAssignmentRuleset classes instead", true)]
    /// 
    public class AutoAssignmentContext
    {
        public BetterWorkTabSettings Settings { get; }
        public IEnumerable<WorkTypeDef> AllWorkTypes { get; }
        public HashSet<Pawn> BestDoctors { get; }
        public HashSet<WorkTypeDef> TouchedWorkTypes { get; }

        public AutoAssignmentContext(BetterWorkTabSettings settings, IEnumerable<WorkTypeDef> allWorkTypes, HashSet<Pawn> bestDoctors, HashSet<WorkTypeDef> touchedWorkTypes)
        {
            Settings = settings;
            AllWorkTypes = allWorkTypes;
            BestDoctors = bestDoctors;
            TouchedWorkTypes = touchedWorkTypes;
        }
    }
}