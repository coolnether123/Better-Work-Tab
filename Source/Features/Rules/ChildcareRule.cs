using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles assigning childcare priority based on pawn pregnancy status.
    /// </summary>
    public class ChildcareRule : IAssignmentRule
    {
        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            if (!context.Settings.rule_ChildcareEnabled) return;

            bool isPregnant = pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false;

            // Childcare is only set to 1 if a pawn is pregnant
            if (isPregnant && WorkTypeDefOf.Childcare != null && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Childcare))
            {
                setPrioritySafe(WorkTypeDefOf.Childcare, context.Settings.rule_ChildcarePriority);
            }
        }
    }
}