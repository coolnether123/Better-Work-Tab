using Better_Work_Tab.Features.Rules;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Convenience base class that provides safe no-op implementations and debug helpers.
    /// Derive from this to keep future modules resilient without repeating boilerplate.
    /// </summary>
    public abstract class ModSupportModuleBase : IModSupportModule
    {
        public abstract string PackageId { get; }

        /// <summary>
        /// Optional friendly name; overrides can supply something nicer than PackageId.
        /// </summary>
        public virtual string DisplayName => PackageId;

        public virtual void OnModsDetected() { }

        public virtual void OnPawnTableRefresh(PawnTable table) { }

        public virtual void OnPawnRowDrawn(Pawn pawn, Rect iconRect) { }

        public virtual void OnRulesEvaluated(Pawn pawn, WorkAssignmentParameters currentParameters) { }

        protected void Debug(string message) => BetterWorkTabMod.DebugLog(message, DebugFeature.ModSupport);
    }
}
