using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Spine.Harmony.Transpilers.VNext;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal static class BwtCallRedirectCompiler
    {
        internal static PatchResult Compile(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            BwtCallRedirectProfile profile)
        {
            return Compile(instructions, original, profile, BwtBuildIdentity.From(original));
        }

        internal static PatchResult Compile(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            BwtCallRedirectProfile profile,
            BwtBuildIdentity selectedBuild)
        {
            if (profile == null)
                return IlTranspiler.Reject(
                    instructions, original, "BWT.ProfileCompiler", PatchDiagnosticCode.InvalidPlan,
                    "No call-redirect profile was selected.");
            if (!profile.Target.Matches(original, selectedBuild))
                return IlTranspiler.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedMethodContext,
                    "The target does not match the exact profile identity " + profile.Target.Describe() + ".");

            string bindingError = profile.ValidateBindings();
            if (bindingError != null)
                return IlTranspiler.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedSignature,
                    "The profile binding contract was rejected: " + bindingError + ".");

            return IlRecipes.RedirectCall(
                instructions,
                original,
                profile.Source.Method,
                profile.Replacement.Method,
                profile.Id,
                profile.Cardinality,
                PatchRequirement.Required);
        }
    }

    internal static class BwtCallRedirectConsumers
    {
        internal static IEnumerable<CodeInstruction> ApplyTipPriorityLookup(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtCallRedirectCompiler.Compile(
                instructions, original, BwtCallRedirectProfiles.TipPriorityLookup);
        }

        internal static IEnumerable<CodeInstruction> ApplyPawnLabelCloseWorkTab(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtCallRedirectCompiler.Compile(
                instructions, original, BwtCallRedirectProfiles.PawnLabelCloseWorkTab);
        }
    }

}
