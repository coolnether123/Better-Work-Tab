using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Better_Work_Tab.Transpilers.BwtExactProfile;
using Verse;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal static class BwtHeaderCompiler
    {
        internal static IEnumerable<CodeInstruction> ApplyDisableHighlight(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            BwtPatchResult result = Compile(
                instructions, generator, original, BwtHeaderProfiles.DisableHighlight,
                BwtBuildIdentity.From(original));
            return result;
        }

        internal static BwtPatchResult Compile(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original,
            BwtHeaderProfile profile, BwtBuildIdentity selectedBuild)
        {
            if (profile == null)
                return BwtExactProfileExecutor.Reject(
                    instructions, original, "BWT.ProfileCompiler", PatchDiagnosticCode.InvalidPlan,
                    "No header profile was selected.");
            if (!profile.Target.Matches(original, selectedBuild))
                return BwtExactProfileExecutor.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedMethodContext,
                    "The target does not match the exact profile identity " + profile.Target.Describe() + ".");
            string bindingError = profile.Validate();
            if (bindingError != null)
                return BwtExactProfileExecutor.Reject(
                    instructions, original, profile.Id, PatchDiagnosticCode.UnsupportedSignature,
                    "The profile binding contract was rejected: " + bindingError + ".");

            IlAnchor marker = IlAnchor.Sequence(
                profile.Id + ".HighlightGuard", MatchMode.ExactlyOne,
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Ldsfld),
                    IlPredicate.Field(profile.SettingsField)),
                IlPredicate.Opcode(OpCodes.Brfalse),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Ldsfld),
                    IlPredicate.Field(profile.SettingsField)),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Ldfld),
                    IlPredicate.Field(profile.AngledHeadersField)),
                IlPredicate.Opcode(OpCodes.Brfalse),
                IlPredicate.Call(profile.IsWorkTabCall, includeCallvirt: true),
                IlPredicate.Opcode(OpCodes.Brfalse),
                IlPredicate.Opcode(OpCodes.Ldarg_0),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Isinst),
                    IlPredicate.OperandEquals(profile.WorkPriorityWorkerType)),
                IlPredicate.Opcode(OpCodes.Brtrue)).WithSequenceMatcher(
                    (codes, start) => MatchesGuardMarker(codes, start, profile));

            IlInstructionSnapshot snapshot = IlInstructionSnapshot.Capture(instructions, original);
            List<CodeInstruction> code = snapshot.CloneInstructions();
            IlAnchor boundary = CreateRealHeaderBoundary(profile);
            List<IlMatch> markerShape = marker.Resolve(code, enforceMatcher: false);
            if (markerShape.Count > 1)
                return BwtExactProfileExecutor.Reject(code, original, profile.Id,
                    PatchDiagnosticCode.IdempotencyMarkerAmbiguous,
                    "The header guard idempotency marker occurs more than once.");
            if (markerShape.Count == 1)
            {
                if (marker.Resolve(code).Count != 1)
                    return BwtExactProfileExecutor.Reject(code, original, profile.Id,
                        PatchDiagnosticCode.StructuralValidationFailed,
                        "The header guard marker shape does not prove the patch topology.");
                IlVerificationReport verification = IlVerifier.Verify(snapshot, code);
                if (!verification.IsValid)
                    return BwtExactProfileExecutor.Reject(code, original, profile.Id,
                        PatchDiagnosticCode.StructuralValidationFailed,
                        "The existing header guard failed BWT exact-profile baseline verification.");
                return new BwtPatchResult(PatchOutcome.AlreadyApplied, code,
                    new[] { new PatchDiagnostic(PatchDiagnosticCode.AlreadyApplied,
                        PatchDiagnosticSeverity.Info, profile.Id, null, snapshot.Method.Identity,
                        null, null, "The header guard idempotency marker is already present.") },
                    profile.Id, snapshot.Method.Identity);
            }

            return BwtExactProfileRecipes.InsertGuardBeforeCallFirstMatch(
                code, original, generator, profile.Id, boundary, profile.HighlightCall,
                (fallback, skip) => CreateGuard(profile, fallback, skip), null);
        }

        private static IEnumerable<CodeInstruction> CreateGuard(
            BwtHeaderProfile profile, Label fallback, Label skip)
        {
            return new[]
            {
                new CodeInstruction(OpCodes.Ldsfld, profile.SettingsField),
                new CodeInstruction(OpCodes.Brfalse, fallback),
                new CodeInstruction(OpCodes.Ldsfld, profile.SettingsField),
                new CodeInstruction(OpCodes.Ldfld, profile.AngledHeadersField),
                new CodeInstruction(OpCodes.Brfalse, fallback),
                new CodeInstruction(OpCodes.Call, profile.IsWorkTabCall),
                new CodeInstruction(OpCodes.Brfalse, fallback),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Isinst, profile.WorkPriorityWorkerType),
                new CodeInstruction(OpCodes.Brtrue, skip)
            };
        }

        private static IlAnchor CreateRealHeaderBoundary(BwtHeaderProfile profile)
        {
            MethodInfo getInteractableHeaderRect = AccessTools.Method(
                typeof(PawnColumnWorker), "GetInteractableHeaderRect",
                new[] { typeof(UnityEngine.Rect), typeof(PawnTable) });
            MethodInfo isOver = AccessTools.Method(
                typeof(Mouse), nameof(Mouse.IsOver), new[] { typeof(UnityEngine.Rect) });
            return IlAnchor.Sequence(
                profile.Id + ".HighlightCall", MatchMode.FirstMatch,
                IlPredicate.Opcode(OpCodes.Ldarg_0),
                IlPredicate.Opcode(OpCodes.Ldarg_1),
                IlPredicate.Opcode(OpCodes.Ldarg_2),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Callvirt),
                    IlPredicate.Call(getInteractableHeaderRect, includeCallvirt: true)),
                IlPredicate.LocalStore(),
                IlPredicate.LocalLoad(),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Call),
                    IlPredicate.Call(isOver, includeCallvirt: false)),
                IlPredicate.Opcode(OpCodes.Brfalse_S),
                IlPredicate.LocalLoad(),
                IlPredicate.AllOf(IlPredicate.Opcode(OpCodes.Call),
                    IlPredicate.Call(profile.HighlightCall, includeCallvirt: false)))
                .WithSequenceMatcher((codes, start) => MatchesRealHeaderBoundary(
                    codes, start, getInteractableHeaderRect, isOver, profile.HighlightCall));
        }

        private static bool MatchesRealHeaderBoundary(
            IList<CodeInstruction> instructions, int start, MethodInfo getInteractableHeaderRect,
            MethodInfo isOver, MethodInfo highlightCall)
        {
            const int boundaryLength = 10;
            if (instructions == null || start < 0 || start + boundaryLength > instructions.Count ||
                getInteractableHeaderRect == null || isOver == null || highlightCall == null)
                return false;
            if (!MatchesOpcode(instructions[start], OpCodes.Ldarg_0) ||
                !MatchesOpcode(instructions[start + 1], OpCodes.Ldarg_1) ||
                !MatchesOpcode(instructions[start + 2], OpCodes.Ldarg_2) ||
                !MatchesInstruction(instructions[start + 3], OpCodes.Callvirt,
                    getInteractableHeaderRect) ||
                !MatchesInstruction(instructions[start + 6], OpCodes.Call, isOver) ||
                !MatchesOpcode(instructions[start + 7], OpCodes.Brfalse_S) ||
                !MatchesInstruction(instructions[start + 9], OpCodes.Call, highlightCall))
                return false;

            if (!SameLocal(instructions[start + 4], instructions[start + 5]) ||
                !SameLocal(instructions[start + 4], instructions[start + 8]))
                return false;

            Label continuation;
            if (!TryGetBranchTarget(instructions[start + 7], OpCodes.Brfalse_S, out continuation))
                return false;
            int continuationIndex;
            // The false branch may skip later header-tip instructions before resuming.
            return FindUniqueLabelTarget(instructions, continuation, out continuationIndex) &&
                continuationIndex > start + boundaryLength - 1;
        }

        private static bool MatchesGuardMarker(
            IList<CodeInstruction> instructions, int start, BwtHeaderProfile profile)
        {
            const int guardLength = 10;
            if (instructions == null || start < 0 || start + guardLength >= instructions.Count ||
                !MatchesInstruction(instructions[start], OpCodes.Ldsfld, profile.SettingsField))
                return false;

            Label fallback;
            Label secondFallback;
            Label thirdFallback;
            Label skip;
            if (!TryGetBranchTarget(instructions[start + 1], OpCodes.Brfalse, out fallback) ||
                !MatchesInstruction(instructions[start + 2], OpCodes.Ldsfld, profile.SettingsField) ||
                !MatchesInstruction(instructions[start + 3], OpCodes.Ldfld, profile.AngledHeadersField) ||
                !TryGetBranchTarget(instructions[start + 4], OpCodes.Brfalse, out secondFallback) ||
                !MatchesInstruction(instructions[start + 5], OpCodes.Call, profile.IsWorkTabCall) ||
                !TryGetBranchTarget(instructions[start + 6], OpCodes.Brfalse, out thirdFallback) ||
                !MatchesOpcode(instructions[start + 7], OpCodes.Ldarg_0) ||
                !MatchesInstruction(instructions[start + 8], OpCodes.Isinst, profile.WorkPriorityWorkerType) ||
                !TryGetBranchTarget(instructions[start + 9], OpCodes.Brtrue, out skip))
                return false;

            if (!fallback.Equals(secondFallback) || !fallback.Equals(thirdFallback) ||
                fallback.Equals(skip))
                return false;

            int fallbackTarget;
            if (!FindUniqueLabelTarget(instructions, fallback, out fallbackTarget))
                return false;
            int originalBoundary = start + guardLength;
            int highlightCall;
            if (!MatchesRealHeaderBoundaryAt(
                instructions, originalBoundary, profile, out highlightCall))
                return false;

            if (fallbackTarget != originalBoundary)
                return false;

            int skipTarget;
            return FindUniqueLabelTarget(instructions, skip, out skipTarget) &&
                skipTarget == highlightCall + 1;
        }

        private static bool MatchesRealHeaderBoundaryAt(
            IList<CodeInstruction> instructions, int start, BwtHeaderProfile profile,
            out int callIndex)
        {
            MethodInfo getInteractableHeaderRect = AccessTools.Method(
                typeof(PawnColumnWorker), "GetInteractableHeaderRect",
                new[] { typeof(UnityEngine.Rect), typeof(PawnTable) });
            MethodInfo isOver = AccessTools.Method(
                typeof(Mouse), nameof(Mouse.IsOver), new[] { typeof(UnityEngine.Rect) });
            callIndex = start + 9;
            return MatchesRealHeaderBoundary(
                instructions, start, getInteractableHeaderRect, isOver, profile.HighlightCall);
        }

        private static bool SameLocal(CodeInstruction store, CodeInstruction firstLoad)
        {
            if (!IsLocal(store, "stloc") || !IsLocal(firstLoad, "ldloc")) return false;
            LocalBuilder stored = store.operand as LocalBuilder;
            LocalBuilder loaded = firstLoad.operand as LocalBuilder;
            if (stored != null || loaded != null)
                return stored != null && ReferenceEquals(stored, loaded);
            return LocalIndex(store) >= 0 && LocalIndex(store) == LocalIndex(firstLoad);
        }

        private static bool IsLocal(CodeInstruction instruction, string prefix)
        {
            string name = instruction == null ? null : instruction.opcode.Name;
            return name == prefix || name != null &&
                name.StartsWith(prefix + ".", StringComparison.Ordinal);
        }

        private static int LocalIndex(CodeInstruction instruction)
        {
            if (instruction.operand is LocalBuilder)
                return ((LocalBuilder)instruction.operand).LocalIndex;
            if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3)
                return 3;
            try { return Convert.ToInt32(instruction.operand); }
            catch (Exception) { return -1; }
        }

        private static bool MatchesInstruction(
            CodeInstruction instruction, OpCode opcode, object operand)
        {
            return instruction != null && instruction.opcode == opcode &&
                Object.Equals(instruction.operand, operand);
        }

        private static bool MatchesOpcode(CodeInstruction instruction, OpCode opcode)
        {
            return instruction != null && instruction.opcode == opcode;
        }

        private static bool TryGetBranchTarget(
            CodeInstruction instruction, OpCode opcode, out Label target)
        {
            if (instruction != null && instruction.opcode == opcode && instruction.operand is Label)
            {
                target = (Label)instruction.operand;
                return true;
            }
            target = default(Label);
            return false;
        }

        private static bool FindUniqueLabelTarget(
            IList<CodeInstruction> instructions, Label label, out int targetIndex)
        {
            targetIndex = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                CodeInstruction instruction = instructions[index];
                if (instruction == null || instruction.labels == null ||
                    !instruction.labels.Contains(label))
                    continue;
                if (targetIndex >= 0) return false;
                targetIndex = index;
            }
            return targetIndex >= 0;
        }
    }
}
