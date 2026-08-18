using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Better_Work_Tab.Transpilers.BwtExactProfile;
using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal static class BwtPriorityCompiler
    {
        internal static BwtPatchResult Compile(
            IEnumerable<CodeInstruction> instructions, MethodBase original,
            BwtPriorityConstantProfile profile, BwtBuildIdentity selectedBuild)
        {
            if (profile == null)
                return BwtExactProfileExecutor.Reject(
                    instructions, original, "BWT.ProfileCompiler", PatchDiagnosticCode.InvalidPlan,
                    "No priority-constant profile was selected.");
            if (!profile.Target.Matches(original, selectedBuild))
                return BwtExactProfileExecutor.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedMethodContext,
                    "The target does not match the exact profile identity " + profile.Target.Describe() + ".");
            string bindingError = profile.Validate();
            if (bindingError != null)
                return BwtExactProfileExecutor.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedSignature,
                    "The profile binding contract was rejected: " + bindingError + ".");

            var snapshot = IlInstructionSnapshot.Capture(instructions, original);
            List<MethodInfo> providerMethods = FindProviderMethods(snapshot, profile);
            var anchors = new List<IlAnchor>();
            var operations = new List<IlEditDescription>();
            var markers = new List<IlAnchor>();
            foreach (BwtPriorityConstantEdit edit in profile.Edits)
            {
                string prefix = profile.Id + "." + edit.Id;
                bool headerShape = profile.Shape == BwtPriorityProfileShape.HeaderClicked;
                IlAnchor source = CreateAnchor(edit, prefix + ".Source", false, providerMethods, headerShape);
                IlAnchor marker = CreateAnchor(edit, prefix + ".Marker", true, providerMethods, headerShape);
                anchors.Add(source);
                anchors.Add(marker);
                operations.Add(IlEditDescription.ReplaceInstructionWithCall(
                    prefix + ".Replace", source.Id, ReplacementOffset(edit.Context),
                    SourcePredicate(edit, providerMethods), edit.ReplacementCall, edit.Requirement));
                markers.Add(marker);
            }

            var plan = new IlTranspilerPlan(
                profile.Id, anchors, operations, new IlIdempotencyMarker(markers));
            return BwtExactProfileExecutor.Execute(snapshot, plan);
        }

        private static IlAnchor CreateAnchor(
            BwtPriorityConstantEdit edit, string id, bool marker, List<MethodInfo> providerMethods,
            bool headerShape)
        {
            IlPredicate value = marker
                ? IlPredicate.Call(edit.ReplacementCall, includeCallvirt: true)
                : SourcePredicate(edit, providerMethods);
            switch (edit.Context)
            {
                case BwtPriorityConstantContext.WrapUnderflow:
                    if (headerShape)
                        return HeaderUnderflowAnchor(id, value);
                    return IlAnchor.Sequence(
                            id, MatchMode.ExactlyOne,
                            IlPredicate.Call(edit.ContextCall, includeCallvirt: true),
                            IlPredicate.Int32Constant(1), IlPredicate.Opcode(OpCodes.Sub),
                            IlPredicate.LocalStore(), IlPredicate.LocalLoad(),
                            IlPredicate.Int32Constant(0),
                            IlPredicate.ComparisonBranch(IlComparison.GreaterOrEqual), value)
                        .WithLocalRelationship(3, 4);
                case BwtPriorityConstantContext.WrapOverflow:
                    return IlAnchor.Sequence(
                            id, MatchMode.ExactlyOne,
                            IlPredicate.Call(edit.ContextCall, includeCallvirt: true),
                            IlPredicate.Int32Constant(1), IlPredicate.Opcode(OpCodes.Add),
                            IlPredicate.LocalStore(), IlPredicate.LocalLoad(), value,
                            IlPredicate.ComparisonBranch(IlComparison.LessThanOrEqual),
                            IlPredicate.Int32Constant(0))
                        .WithLocalRelationship(3, 4);
                case BwtPriorityConstantContext.CachedWrapOverflow:
                    return IlAnchor.Sequence(
                        id, MatchMode.ExactlyOne,
                            IlPredicate.Opcode(OpCodes.Ldloc_3),
                            IlPredicate.Int32Constant(1), IlPredicate.Opcode(OpCodes.Add),
                            IlPredicate.Opcode(OpCodes.Stloc_S), IlPredicate.Opcode(OpCodes.Ldloc_S),
                            value, IlPredicate.ComparisonBranch(IlComparison.LessThanOrEqual))
                        .WithSequenceMatcher((instructions, start) =>
                            IsLocal(instructions[start + 3], 5) &&
                            IsLocal(instructions[start + 4], 5) &&
                            SameLocalReference(instructions[start + 3], instructions[start + 4]) &&
                            instructions[start + 6].opcode == OpCodes.Ble_S);
                case BwtPriorityConstantContext.BeforeSetPriority:
                    return IlAnchor.Sequence(
                        id, MatchMode.ExactlyOne, value,
                        IlPredicate.Call(edit.ContextCall, includeCallvirt: true));
                case BwtPriorityConstantContext.SetPriorityUpperBound:
                    return IlAnchor.Sequence(
                        id, MatchMode.ExactlyOne,
                        IlPredicate.Argument(2), IlPredicate.Int32Constant(0),
                        IlPredicate.ComparisonBranch(IlComparison.LessThan),
                        IlPredicate.Argument(2), value,
                        IlPredicate.ComparisonBranch(IlComparison.LessThanOrEqual));
                default: throw new ArgumentOutOfRangeException("Context");
            }
        }

        private static IlAnchor HeaderUnderflowAnchor(string id, IlPredicate value)
        {
            return IlAnchor.Sequence(id, MatchMode.ExactlyOne,
                IlPredicate.Opcode(OpCodes.Ldloc_3),
                IlPredicate.Int32Constant(1), IlPredicate.Opcode(OpCodes.Sub),
                IlPredicate.Opcode(OpCodes.Stloc_S), IlPredicate.Opcode(OpCodes.Ldloc_S),
                IlPredicate.Int32Constant(0), IlPredicate.ComparisonBranch(IlComparison.GreaterOrEqual), value)
                .WithSequenceMatcher((instructions, start) =>
                    IsLocal(instructions[start + 3], 4) &&
                    IsLocal(instructions[start + 4], 4) &&
                    SameLocalReference(instructions[start + 3], instructions[start + 4]));
        }

        private static bool IsLocal(CodeInstruction instruction, int expectedIndex)
        {
            int actualIndex;
            return TryLocalIndex(instruction, out actualIndex) && actualIndex == expectedIndex;
        }

        private static bool SameLocalReference(CodeInstruction first, CodeInstruction second)
        {
            return first.operand != null && ReferenceEquals(first.operand, second.operand);
        }

        private static bool TryLocalIndex(CodeInstruction instruction, out int index)
        {
            index = -1;
            string name = instruction.opcode == null ? null : instruction.opcode.Name;
            if (name == "ldloc.0" || name == "stloc.0") { index = 0; return true; }
            if (name == "ldloc.1" || name == "stloc.1") { index = 1; return true; }
            if (name == "ldloc.2" || name == "stloc.2") { index = 2; return true; }
            if (name == "ldloc.3" || name == "stloc.3") { index = 3; return true; }
            if (name == "ldloc" || name == "stloc" || name == "ldloc.s" || name == "stloc.s")
            {
                var localBuilder = instruction.operand as LocalBuilder;
                if (localBuilder != null)
                {
                    index = localBuilder.LocalIndex;
                    return true;
                }
                try { index = Convert.ToInt32(instruction.operand); return true; }
                catch (Exception) { }
            }
            return false;
        }

        private static IlPredicate SourcePredicate(
            BwtPriorityConstantEdit edit, List<MethodInfo> providerMethods)
        {
            if (edit.Context != BwtPriorityConstantContext.SetPriorityUpperBound ||
                providerMethods.Count == 0)
                return IlPredicate.Int32Constant(edit.ExpectedValue);
            var values = new List<IlPredicate> { IlPredicate.Int32Constant(edit.ExpectedValue) };
            foreach (MethodInfo method in providerMethods)
                values.Add(IlPredicate.Call(method, includeCallvirt: true));
            return IlPredicate.AnyOf(values.ToArray());
        }

        private static int ReplacementOffset(BwtPriorityConstantContext context)
        {
            switch (context)
            {
                case BwtPriorityConstantContext.WrapUnderflow: return 7;
                case BwtPriorityConstantContext.WrapOverflow:
                case BwtPriorityConstantContext.CachedWrapOverflow: return 5;
                case BwtPriorityConstantContext.BeforeSetPriority: return 0;
                case BwtPriorityConstantContext.SetPriorityUpperBound: return 4;
                default: throw new ArgumentOutOfRangeException("context");
            }
        }

        private static List<MethodInfo> FindProviderMethods(
            IlInstructionSnapshot snapshot, BwtPriorityConstantProfile profile)
        {
            var result = new List<MethodInfo>();
            foreach (BwtPriorityConstantEdit edit in profile.Edits)
                foreach (BwtExternalProviderBinding binding in edit.ExternalProviders)
                    foreach (CodeInstruction instruction in snapshot.CloneInstructions())
                    {
                        var method = instruction.operand as MethodInfo;
                        if (method != null && binding.Matches(method) &&
                            (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                            !result.Contains(method))
                            result.Add(method);
                    }
            return result;
        }
    }

    internal static class BwtPriorityConsumers
    {
        internal static IEnumerable<CodeInstruction> ApplyDrawWorkBox(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            BwtPatchResult result = BwtPriorityCompiler.Compile(
                instructions, original, BwtPriorityProfiles.DrawWorkBoxPriorityConstants,
                BwtBuildIdentity.From(original));
            return result;
        }

        internal static IEnumerable<CodeInstruction> ApplyHeaderClicked(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            BwtPatchResult result = BwtPriorityCompiler.Compile(
                instructions, original, BwtPriorityProfiles.HeaderClickedPriorityConstants,
                BwtBuildIdentity.From(original));
            return result;
        }

        internal static IEnumerable<CodeInstruction> ApplySetPriority(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            BwtPatchResult result = BwtPriorityCompiler.Compile(
                instructions, original, BwtPriorityProfiles.SetPriorityUpperBound,
                BwtBuildIdentity.From(original));
            return result;
        }
    }
}
