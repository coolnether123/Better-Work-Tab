using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// Defines the contract for a work assignment rule, allowing for modular and extensible auto-assignment logic.
    /// </summary>
    public interface IAssignmentRule
    {
        void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context);
    }
}