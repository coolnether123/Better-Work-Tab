using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Spine.Harmony.Transpilers.VNext
{
    public static class IlRecipes
    {
        /// <summary>
        /// Redirects a matching call through a signature-compatible replacement.
        /// The default is one required exact match. Idempotency is reported only by
        /// recipes that declare an exact topology marker.
        /// </summary>
        public static PatchResult RedirectCall(
            IEnumerable<CodeInstruction> instructions,
            MethodBase targetMethod,
            MethodInfo callToReplace,
            MethodInfo replacementCall,
            string patchId = null,
            MatchMode matchMode = MatchMode.ExactlyOne,
            PatchRequirement requirement = PatchRequirement.Required)
        {
            if (callToReplace == null || replacementCall == null)
                throw new ArgumentNullException(callToReplace == null ? "callToReplace" : "replacementCall");
            IlAnchor.ValidateMatchMode(matchMode);
            IlEditDescription.ValidateRequirement(requirement);

            var resolvedPatchId = String.IsNullOrWhiteSpace(patchId)
                ? "RedirectCall." + callToReplace.Name + ".To." + replacementCall.Name
                : patchId.Trim();
            var sourceAnchor = IlAnchor.Sequence(
                resolvedPatchId + ".SourceCall",
                matchMode,
                IlPredicate.Call(callToReplace, includeCallvirt: true));
            var plan = new IlTranspilerPlan(
                resolvedPatchId,
                new[] { sourceAnchor },
                new[]
                {
                    IlEditDescription.ReplaceCall(
                        resolvedPatchId + ".Redirect",
                        sourceAnchor.Id,
                        replacementCall,
                        requirement)
                });
            return IlTranspiler.Execute(instructions, targetMethod, plan);
        }

        internal static PatchResult InsertGuardBeforeCallFirstMatch(
            IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator,
            string planId, IlAnchor boundary, MethodInfo sourceCall,
            Func<Label, Label, IEnumerable<CodeInstruction>> guardFactory, IlPredicate precedingBoundary,
            params IlAnchor[] idempotencyMarkers)
        {
            RequireMatchMode(boundary, MatchMode.FirstMatch);
            return InsertGuard(instructions, original, generator, planId, boundary, sourceCall,
                guardFactory, precedingBoundary, idempotencyMarkers);
        }

        private static PatchResult InsertGuard(
            IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator,
            string planId, IlAnchor boundary, MethodInfo sourceCall,
            Func<Label, Label, IEnumerable<CodeInstruction>> guardFactory, IlPredicate precedingBoundary,
            IlAnchor[] idempotencyMarkers)
        {
            if (generator == null || boundary == null || sourceCall == null || guardFactory == null)
                throw new ArgumentException("A generator, boundary, source call, and guard factory are required.");
            var snapshot = IlInstructionSnapshot.Capture(instructions, original);
            var usedLabels = UsedLabels(snapshot.CloneInstructions());
            var fallback = DefineUniqueLabel(generator, usedLabels);
            var skip = DefineUniqueLabel(generator, usedLabels);
            var replacement = guardFactory(fallback, skip);
            if (replacement == null) throw new ArgumentException("The guard factory returned null.", "guardFactory");
            var operation = IlEditDescription.InsertGuardBeforeCall(
                planId + ".Guard", boundary.Id, boundary.Predicates.Count - 1, sourceCall, replacement,
                fallback, skip, PatchRequirement.Required, precedingBoundary);
            var anchors = new List<IlAnchor> { boundary };
            if (idempotencyMarkers != null) anchors.AddRange(idempotencyMarkers);
            var marker = idempotencyMarkers == null || idempotencyMarkers.Length == 0
                ? null
                : new IlIdempotencyMarker(idempotencyMarkers);
            var plan = new IlTranspilerPlan(planId, anchors, new[] { operation }, marker);
            return IlTranspiler.Execute(snapshot, plan);
        }

        private static void RequireMatchMode(IlAnchor anchor, MatchMode expected)
        {
            if (anchor == null) throw new ArgumentNullException("anchor");
            IlAnchor.ValidateMatchMode(expected);
            if (anchor.Cardinality != expected)
                throw new ArgumentException("This recipe requires a " + expected + " anchor.", "anchor");
        }

        private static Label DefineUniqueLabel(ILGenerator generator, ISet<Label> used)
        {
            Label label;
            do
            {
                label = generator.DefineLabel();
            }
            while (!used.Add(label));
            return label;
        }

        private static HashSet<Label> UsedLabels(IList<CodeInstruction> instructions)
        {
            var labels = new HashSet<Label>();
            foreach (var instruction in instructions)
                if (instruction != null && instruction.labels != null) labels.UnionWith(instruction.labels);
            return labels;
        }
    }
}
