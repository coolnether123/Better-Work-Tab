using System;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// Makes legacy rule predicates read the same stable priority values that
    /// the compiler is updating. It is a read/model adapter only; the
    /// application boundary remains the sole live mutation executor.
    /// </summary>
    internal sealed class RuleApplicationPlanningScope : IDisposable
    {
        [ThreadStatic]
        private static WorkTabAtomicMutationPlan _current;
        private readonly WorkTabAtomicMutationPlan _previous;

        internal RuleApplicationPlanningScope(WorkTabAtomicMutationPlan mutation)
        {
            _previous = _current;
            _current = mutation;
        }

        internal static int GetPriority(Pawn pawn, WorkTypeDef workType)
        {
            return _current?.GetPriority(pawn, workType) ??
                (pawn?.workSettings == null || workType == null
                    ? WorkPrioritySystem.DisabledPriority
                    : pawn.workSettings.GetPriority(workType));
        }

        internal static bool SetPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            return _current != null
                ? _current.SetPriority(pawn, workType, priority)
                : WorkTabApplication.Current?.SetStoredParentPriority(pawn, workType, priority) == true;
        }

        public void Dispose()
        {
            _current = _previous;
        }
    }
}
