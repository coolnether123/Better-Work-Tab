using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Spine.Harmony.Infrastructure;

namespace Spine.Harmony.Transpilers.VNext
{
    internal sealed class IlResolvedEdit
    {
        internal IlResolvedEdit(
            IlEditDescription operation, int start, int endExclusive,
            IEnumerable<CodeInstruction> replacement, int sourceIndex,
            bool guardHasLabels = false, bool guardHasBlocks = false)
        {
            Operation = operation;
            Start = start;
            EndExclusive = endExclusive;
            SourceIndex = sourceIndex;
            Replacement = IlInstructionSnapshot.CloneList(replacement);
            GuardHasLabels = guardHasLabels;
            GuardHasBlocks = guardHasBlocks;
        }
        internal IlEditDescription Operation { get; private set; }
        internal int Start { get; private set; }
        internal int EndExclusive { get; private set; }
        internal int SourceIndex { get; private set; }
        internal List<CodeInstruction> Replacement { get; private set; }
        internal bool GuardHasLabels { get; private set; }
        internal bool GuardHasBlocks { get; private set; }
        internal bool IsGuardEntry(int index, bool exceptionBlocks)
        {
            return Operation.Kind == IlEditKind.InsertGuardBeforeCall && index == Start &&
                (exceptionBlocks ? GuardHasBlocks : GuardHasLabels);
        }
    }

    internal static class IlTranspiler
    {
        internal static PatchResult Execute(
            IEnumerable<CodeInstruction> instructions, MethodBase originalMethod,
            IlTranspilerPlan plan)
        {
            // Capture outside the catch: a broken enumerable must propagate, not return a partial body.
            return Execute(IlInstructionSnapshot.Capture(instructions, originalMethod), plan);
        }
        internal static PatchResult Execute(IlInstructionSnapshot snapshot, IlTranspilerPlan plan)
        {
            var planId = plan == null ? "<null-plan>" : plan.Id;
            var methodId = snapshot == null ? "<unknown-method>" : snapshot.Method.Identity;
            var diagnostics = new List<PatchDiagnostic>();
            var original = new List<CodeInstruction>();
            try
            {
                if (snapshot == null)
                    return Reject(diagnostics, planId, methodId,
                        PatchDiagnosticCode.NullInstructionStream, "No snapshot was supplied.", original);
                original = snapshot.CloneInstructions();
                if (plan == null)
                    return Reject(diagnostics, planId, methodId,
                        PatchDiagnosticCode.InvalidPlan, "No plan was supplied.", original);
                if (snapshot.NullInput)
                    return Reject(diagnostics, plan.Id, methodId,
                        PatchDiagnosticCode.NullInstructionStream, "The stream was null.", original);
                if (snapshot.HasNullInstruction)
                    return Reject(diagnostics, plan.Id, methodId, PatchDiagnosticCode.NullInstruction,
                        "The stream contains a null instruction.", original);
                var baseline = IlVerifier.Verify(snapshot, original);
                CopyDiagnostics(diagnostics, baseline.Diagnostics, plan.Id, methodId);
                if (!baseline.IsValid)
                    return VerificationFailure(baseline.Diagnostics, diagnostics, plan.Id, methodId, original);
                var markerStatus = plan.IdempotencyMarker == null
                    ? IlMarkerStatus.None : plan.IdempotencyMarker.Inspect(original, plan);
                if (markerStatus == IlMarkerStatus.Applied)
                {
                    AddDiagnostic(diagnostics, PatchDiagnosticCode.AlreadyApplied,
                        PatchDiagnosticSeverity.Info, plan.Id, null, methodId,
                        "The declared idempotency marker is already present.");
                    return Complete(PatchOutcome.AlreadyApplied, original, diagnostics, plan.Id, methodId);
                }
                if (markerStatus == IlMarkerStatus.Ambiguous)
                {
                    AddDiagnostic(diagnostics, PatchDiagnosticCode.IdempotencyMarkerAmbiguous,
                        RequiredSeverity(plan), plan.Id, null, methodId,
                        "The idempotency marker occurs more than once.");
                    return Complete(PatchOutcome.Rejected, original, diagnostics, plan.Id, methodId);
                }
                if (markerStatus == IlMarkerStatus.Unproven)
                {
                    var severity = RequiredSeverity(plan);
                    AddDiagnostic(diagnostics, PatchDiagnosticCode.StructuralValidationFailed,
                        severity, plan.Id, null, methodId,
                        "A marker-shaped sequence does not prove this plan's topology.");
                    var hasSource = plan.Operations.Any(operation =>
                        plan.AnchorFor(operation.AnchorId).Resolve(original).Count != 0);
                    if (!hasSource)
                        AddDiagnostic(diagnostics, PatchDiagnosticCode.NoMatch, severity, plan.Id,
                            plan.Operations.Count == 0 ? null : plan.Operations[0].OperationId, methodId,
                            "The source anchor was absent while the marker topology was unproven.");
                    return Complete(PatchOutcome.Rejected, original, diagnostics, plan.Id, methodId);
                }
                var resolved = Resolve(plan, original, diagnostics, methodId);
                if (resolved == null)
                    return Complete(PatchOutcome.Rejected, original, diagnostics, plan.Id, methodId);
                if (resolved.Count == 0 && plan.Operations.All(operation => !operation.Required))
                    return Complete(PatchOutcome.OptionalMatchMissing, original, diagnostics, plan.Id, methodId);
                if (!CheckOverlap(resolved, diagnostics, plan.Id, methodId))
                    return Complete(PatchOutcome.Rejected, original, diagnostics, plan.Id, methodId);
                var candidate = IlInstructionSnapshot.CloneList(original);
                foreach (var edit in resolved.OrderByDescending(edit => edit.Start)
                    .ThenByDescending(edit => edit.EndExclusive))
                    Apply(edit, candidate);
                var verification = IlVerifier.Verify(snapshot, candidate, resolved);
                CopyDiagnostics(diagnostics, verification.Diagnostics, plan.Id, methodId);
                if (!verification.IsValid)
                    return VerificationFailure(verification.Diagnostics, diagnostics, plan.Id, methodId, original);
                AddDiagnostic(diagnostics, PatchDiagnosticCode.Applied,
                    PatchDiagnosticSeverity.Info, plan.Id, null, methodId,
                    "The complete plan was validated and applied.");
                return Complete(PatchOutcome.Applied, candidate, diagnostics, plan.Id, methodId);
            }
            catch (Exception exception)
            {
                return Failed(diagnostics, planId, methodId, null, exception, original);
            }
        }

        internal static PatchResult Reject(
            IEnumerable<CodeInstruction> instructions, MethodBase originalMethod,
            string planId, PatchDiagnosticCode code, string detail)
        {
            var snapshot = IlInstructionSnapshot.Capture(instructions, originalMethod);
            var diagnostics = new List<PatchDiagnostic>();
            AddDiagnostic(diagnostics, code, PatchDiagnosticSeverity.Error, planId, null,
                snapshot.Method.Identity, detail);
            return Complete(PatchOutcome.Rejected, snapshot.CloneInstructions(), diagnostics,
                planId, snapshot.Method.Identity);
        }

        private static List<IlResolvedEdit> Resolve(
            IlTranspilerPlan plan, IList<CodeInstruction> original,
            IList<PatchDiagnostic> diagnostics, string methodId)
        {
            var result = new List<IlResolvedEdit>();
            foreach (var operation in plan.Operations)
            {
                var anchor = plan.AnchorFor(operation.AnchorId);
                var matches = anchor == null ? new List<IlMatch>() : anchor.Resolve(original);
                if (matches.Count == 0)
                {
                    if (!operation.Required)
                    {
                        AddDiagnostic(diagnostics, PatchDiagnosticCode.OptionalMatchMissing,
                            PatchDiagnosticSeverity.Info, plan.Id, operation.OperationId, methodId,
                            "Optional operation was not present.");
                        continue;
                    }
                    AddDiagnostic(diagnostics, PatchDiagnosticCode.NoMatch,
                        PatchDiagnosticSeverity.Error, plan.Id, operation.OperationId, methodId,
                        "Required anchor did not match any instruction sequence.");
                    return null;
                }
                if (anchor == null || anchor.Cardinality == MatchMode.ExactlyOne && matches.Count != 1)
                {
                    AddDiagnostic(diagnostics, PatchDiagnosticCode.AmbiguousMatch,
                        operation.Required ? PatchDiagnosticSeverity.Error : PatchDiagnosticSeverity.Warning,
                        plan.Id, operation.OperationId, methodId,
                        "The anchor matched more than one instruction sequence.");
                    return null;
                }
                if (anchor.Cardinality == MatchMode.FirstMatch)
                    matches = new List<IlMatch> { matches[0] };
                foreach (var match in matches)
                {
                    if (!TryResolveEdit(operation, match, original, methodId, plan.Id,
                        diagnostics, out var edit)) return null;
                    result.Add(edit);
                }
            }
            return result;
        }
        private static bool TryResolveEdit(
            IlEditDescription operation, IlMatch match, IList<CodeInstruction> original,
            string methodId, string planId, IList<PatchDiagnostic> diagnostics,
            out IlResolvedEdit edit)
        {
            edit = null;
            var start = match.Start;
            var sourceIndex = match.Start;
            if (operation.Kind == IlEditKind.ReplaceCall)
            {
                MethodBase sourceMethod;
                if (!IsCall(original[sourceIndex], out sourceMethod))
                    return RejectEdit(operation, planId, methodId, diagnostics,
                        PatchDiagnosticCode.CallSignatureMismatch,
                        "The call anchor does not contain a call instruction.", out edit);
                string detail;
                if (!IlVerifier.AreCallShapesCompatible(sourceMethod, original[sourceIndex].opcode,
                    operation.ReplacementMethod, out detail))
                    return RejectEdit(operation, planId, methodId, diagnostics,
                        PatchDiagnosticCode.CallSignatureMismatch, detail, out edit);
                edit = CreateCallEdit(operation, original, sourceIndex);
                return true;
            }
            if (operation.Kind == IlEditKind.ReplaceInstructionWithCall)
            {
                sourceIndex = match.Start + operation.MatchOffset;
                if (sourceIndex < match.Start || sourceIndex >= match.EndExclusive ||
                    operation.ExpectedInstruction == null ||
                    !operation.ExpectedInstruction.Matches(original[sourceIndex]))
                    return RejectEdit(operation, planId, methodId, diagnostics,
                        PatchDiagnosticCode.NoMatch,
                        "The replacement offset did not contain the expected instruction.", out edit);
                string detail;
                if (!IlVerifier.CanReplaceInt32ConstantWithCall(operation.ReplacementMethod, out detail))
                    return RejectEdit(operation, planId, methodId, diagnostics,
                        PatchDiagnosticCode.CallSignatureMismatch, detail, out edit);
                edit = CreateCallEdit(operation, original, sourceIndex);
                return true;
            }
            if (operation.Kind == IlEditKind.InsertGuardBeforeCall)
            {
                sourceIndex = match.Start + operation.MatchOffset;
                MethodBase sourceMethod;
                if (sourceIndex < match.Start || sourceIndex >= match.EndExclusive ||
                    !IsCall(original[sourceIndex], out sourceMethod) ||
                    !Object.Equals(sourceMethod, operation.GuardSourceMethod))
                    return RejectEdit(operation, planId, methodId, diagnostics,
                        PatchDiagnosticCode.NoMatch,
                        "The guard boundary does not contain its declared source call.", out edit);
                if (operation.GuardInsertionPrefix != null)
                {
                    if (start == 0 || !operation.GuardInsertionPrefix.Matches(original[start - 1]))
                        return RejectEdit(operation, planId, methodId, diagnostics,
                            PatchDiagnosticCode.NoMatch,
                            "The guarded call has no matching stack-boundary prefix.", out edit);
                    start--;
                }
                string guardDetail;
                PatchDiagnosticCode guardCode;
                if (!GuardEntryIndices(original, start, sourceIndex, out var hasLabels, out var hasBlocks,
                    out guardCode, out guardDetail))
                    return RejectEdit(operation, planId, methodId, diagnostics, guardCode,
                        guardDetail, out edit);
                edit = new IlResolvedEdit(operation, start, start,
                    operation.CloneReplacement(), sourceIndex, hasLabels, hasBlocks);
                return true;
            }
            return RejectEdit(operation, planId, methodId, diagnostics,
                PatchDiagnosticCode.InvalidPlan, "The edit kind is not supported.", out edit);
        }
        private static bool RejectEdit(
            IlEditDescription operation, string planId, string methodId,
            IList<PatchDiagnostic> diagnostics, PatchDiagnosticCode code, string detail,
            out IlResolvedEdit edit)
        {
            edit = null;
            AddDiagnostic(diagnostics, code,
                operation.Required ? PatchDiagnosticSeverity.Error : PatchDiagnosticSeverity.Warning,
                planId, operation.OperationId, methodId, detail);
            return false;
        }
        private static IlResolvedEdit CreateCallEdit(
            IlEditDescription operation, IList<CodeInstruction> original, int sourceIndex)
        {
            return new IlResolvedEdit(operation, sourceIndex, sourceIndex + 1,
                new[] { CallInstruction(original[sourceIndex], operation.ReplacementMethod) }, sourceIndex);
        }
        private static bool CheckOverlap(
            IList<IlResolvedEdit> edits, IList<PatchDiagnostic> diagnostics,
            string planId, string methodId)
        {
            for (var i = 0; i < edits.Count; i++)
                for (var j = i + 1; j < edits.Count; j++)
                    if (RangesOverlap(edits[i], edits[j]))
                    {
                        AddDiagnostic(diagnostics, PatchDiagnosticCode.OverlappingEdits,
                            edits.Any(edit => edit.Operation.Required)
                                ? PatchDiagnosticSeverity.Error : PatchDiagnosticSeverity.Warning,
                            planId, edits[j].Operation.OperationId, methodId,
                            "Two declarative edits claim the same instruction boundary.");
                        return false;
                    }
            return true;
        }
        private static bool RangesOverlap(IlResolvedEdit left, IlResolvedEdit right) =>
            left.Start == left.EndExclusive
                ? right.Start == right.EndExclusive ? left.Start == right.Start :
                    left.Start >= right.Start && left.Start < right.EndExclusive
                : right.Start == right.EndExclusive
                    ? right.Start >= left.Start && right.Start < left.EndExclusive
                    : left.Start < right.EndExclusive && right.Start < left.EndExclusive;
        private static void Apply(IlResolvedEdit edit, IList<CodeInstruction> code)
        {
            if (edit.Operation.Kind == IlEditKind.InsertGuardBeforeCall)
            {
                ApplyGuard(edit, code);
                return;
            }
            if (edit.Start < edit.EndExclusive)
            {
                TransferMetadata(code, edit.Start, edit.EndExclusive, edit.Replacement);
                for (var i = edit.EndExclusive - 1; i >= edit.Start; i--) code.RemoveAt(i);
            }
            InsertAt(code, edit.Start, edit.Replacement);
        }
        private static void ApplyGuard(IlResolvedEdit edit, IList<CodeInstruction> code)
        {
            var guard = IlInstructionSnapshot.CloneList(edit.Replacement);
            if (guard.Count == 0)
                throw new InvalidOperationException("A guard must contain at least one instruction.");
            MoveGuardEntryMetadata(edit, code, guard[0]);
            AddLabel(code[edit.Start], edit.Operation.GuardFallbackLabel);
            var skipIndex = edit.SourceIndex + 1;
            if (skipIndex >= code.Count) code.Add(new CodeInstruction(OpCodes.Nop));
            AddLabel(code[skipIndex], edit.Operation.GuardSkipLabel);
            InsertAt(code, edit.Start, guard);
        }
        private static void MoveGuardEntryMetadata(
            IlResolvedEdit edit, IList<CodeInstruction> code, CodeInstruction destination)
        {
            var incoming = IncomingLabels(code);
            if (edit.GuardHasLabels)
            {
                var source = code[edit.Start];
                foreach (var label in (source.labels ?? new List<Label>()).Where(incoming.Contains).ToList())
                {
                    AddLabel(destination, label);
                    source.labels.Remove(label);
                }
            }
            if (edit.GuardHasBlocks)
            {
                var source = code[edit.Start];
                if (destination.blocks == null) destination.blocks = new List<ExceptionBlock>();
                destination.blocks.AddRange(source.blocks.Where(IlVerifier.IsExceptionEntry));
                source.blocks.RemoveAll(IlVerifier.IsExceptionEntry);
            }
        }
        private static bool GuardEntryIndices(
            IList<CodeInstruction> code, int start, int sourceIndex,
            out bool hasLabels, out bool hasBlocks,
            out PatchDiagnosticCode rejectionCode, out string rejectionDetail)
        {
            hasLabels = false;
            hasBlocks = false;
            rejectionCode = PatchDiagnosticCode.MetadataNotPreserved;
            rejectionDetail = null;
            var incoming = IncomingLabels(code);
            bool firstBoundary = false;
            if (IlVerifier.IsPrefix(code[start].opcode) || start > 0 && IlVerifier.IsPrefix(code[start - 1].opcode))
            {
                rejectionCode = PatchDiagnosticCode.InvalidPrefix;
                rejectionDetail = "Guard insertion would split a prefix group.";
                return false;
            }
            for (var index = start; index <= sourceIndex && index < code.Count; index++)
            {
                var instruction = code[index];
                if (IlVerifier.IsPrefix(instruction.opcode))
                {
                    rejectionCode = PatchDiagnosticCode.InvalidPrefix;
                    rejectionDetail = "Guard insertion would split a prefix group.";
                    return false;
                }
                if (instruction.blocks != null && instruction.blocks.Any(IlVerifier.IsExceptionEntry))
                {
                    if (index > start)
                    {
                        rejectionCode = PatchDiagnosticCode.InvalidExceptionRegion;
                        rejectionDetail = "Guard insertion cannot move an interior exception-entry marker.";
                        return false;
                    }
                    hasBlocks = true;
                }
                if (instruction.labels != null && instruction.labels.Any(incoming.Contains))
                {
                    if (index > start)
                    {
                        rejectionDetail = firstBoundary
                            ? "Guard insertion has multiple distinct incoming entry boundaries."
                            : "Guard insertion has an incoming label after its first instruction.";
                        return false;
                    }
                    hasLabels = true;
                    firstBoundary = true;
                }
            }
            return true;
        }
        private static HashSet<Label> IncomingLabels(IList<CodeInstruction> code)
        {
            var result = new HashSet<Label>();
            foreach (var instruction in code)
            {
                var direct = instruction.operand as Label?;
                if (direct.HasValue) result.Add(direct.Value);
                var table = instruction.operand as IEnumerable<Label>;
                if (table == null) continue;
                foreach (var label in table) result.Add(label);
            }
            return result;
        }
        private static void TransferMetadata(
            IList<CodeInstruction> code, int start, int end, IList<CodeInstruction> replacement)
        {
            if (replacement.Count == 0)
            {
                for (var i = start; i < end; i++)
                    if (IlVerifier.HasMetadata(code[i]))
                        throw new InvalidOperationException(
                            "A metadata-bearing range cannot be replaced by nothing.");
                return;
            }
            for (var i = start + 1; i < end; i++)
                if (code[i].labels != null && code[i].labels.Count != 0)
                    throw new InvalidOperationException(
                        "A branch enters the interior of a removed range.");
            if (code[start].labels != null) replacement[0].labels.AddRange(code[start].labels);
            if (code[start].blocks != null) replacement[0].blocks.AddRange(code[start].blocks);
        }
        private static void InsertAt(
            IList<CodeInstruction> code, int index, IEnumerable<CodeInstruction> values)
        {
            var replacements = IlInstructionSnapshot.CloneList(values);
            for (var i = 0; i < replacements.Count; i++) code.Insert(index + i, replacements[i]);
        }
        private static CodeInstruction CallInstruction(CodeInstruction source, MethodBase replacement) =>
            new CodeInstruction(SelectCallOpcode(source.opcode, replacement), replacement);
        private static OpCode SelectCallOpcode(OpCode sourceOpcode, MethodBase replacement)
        {
            var method = replacement as MethodInfo;
            if (method != null && method.IsStatic) return OpCodes.Call;
            return sourceOpcode == OpCodes.Callvirt ? OpCodes.Callvirt : OpCodes.Call;
        }
        private static bool IsCall(CodeInstruction instruction, out MethodBase method)
        {
            method = instruction == null ? null : instruction.operand as MethodBase;
            return instruction != null && method != null &&
                (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt);
        }
        private static void AddLabel(CodeInstruction instruction, Label label)
        {
            if (instruction.labels == null) instruction.labels = new List<Label>();
            if (!instruction.labels.Contains(label)) instruction.labels.Add(label);
        }
        private static bool HasFault(IEnumerable<PatchDiagnostic> diagnostics)
        { return diagnostics.Any(diagnostic => diagnostic.Code == PatchDiagnosticCode.InternalException); }
        private static PatchResult VerificationFailure(
            IEnumerable<PatchDiagnostic> verification, IList<PatchDiagnostic> diagnostics,
            string planId, string methodId, IEnumerable<CodeInstruction> original)
        {
            return HasFault(verification)
                ? Failed(diagnostics, planId, methodId, null, null, original)
                : Complete(PatchOutcome.Rejected, original, diagnostics, planId, methodId);
        }
        private static void CopyDiagnostics(
            IList<PatchDiagnostic> destination, IEnumerable<PatchDiagnostic> source,
            string planId, string methodId)
        {
            foreach (var diagnostic in source)
                destination.Add(diagnostic.WithContext(planId, methodId));
        }
        private static void AddDiagnostic(
            IList<PatchDiagnostic> diagnostics, PatchDiagnosticCode code,
            PatchDiagnosticSeverity severity, string planId, string operationId,
            string methodId, string detail, Exception exception = null)
        {
            diagnostics.Add(new PatchDiagnostic(code, severity, planId, operationId, methodId,
                null, null, detail,
                exception == null ? null : exception.GetType().FullName,
                exception == null ? null : exception.Message));
        }
        private static PatchResult Reject(
            IList<PatchDiagnostic> diagnostics, string planId, string methodId,
            PatchDiagnosticCode code, string detail, IEnumerable<CodeInstruction> original)
        {
            AddDiagnostic(diagnostics, code, PatchDiagnosticSeverity.Error,
                planId, null, methodId, detail);
            return Complete(PatchOutcome.Rejected, original, diagnostics, planId, methodId);
        }
        private static PatchResult Failed(
            IList<PatchDiagnostic> diagnostics, string planId, string methodId,
            string operationId, Exception exception, IEnumerable<CodeInstruction> original)
        {
            if (exception != null)
                AddDiagnostic(diagnostics, PatchDiagnosticCode.InternalException,
                    PatchDiagnosticSeverity.Error, planId, operationId, methodId,
                    "The transaction raised an exception and was rolled back.", exception);
            else if (!HasFault(diagnostics))
                AddDiagnostic(diagnostics, PatchDiagnosticCode.InternalException,
                    PatchDiagnosticSeverity.Error, planId, operationId, methodId,
                    "The verifier faulted while inspecting the candidate.");
            return Complete(PatchOutcome.Failed, original, diagnostics, planId, methodId);
        }
        private static PatchResult Complete(
            PatchOutcome outcome, IEnumerable<CodeInstruction> instructions,
            IEnumerable<PatchDiagnostic> diagnostics, string planId, string methodId)
        {
            var result = new PatchResult(outcome, instructions, diagnostics, planId, methodId);
            if (outcome == PatchOutcome.Rejected || outcome == PatchOutcome.Failed)
                ReportFailure(result);
            return result;
        }
        internal static void ReportFailure(PatchResult result)
        {
            if (result == null || (result.Outcome != PatchOutcome.Rejected &&
                result.Outcome != PatchOutcome.Failed) || !result.TryClaimFailureReport()) return;
            var error = result.Outcome == PatchOutcome.Failed || result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == PatchDiagnosticSeverity.Error);
            var message = "[VNext] " + (error ? "Required" : "Optional") +
                " patch failure for " + result.PatchId + " on " + result.TargetMethodId +
                ": " + result.FormatDiagnostics();
            if (error) MMLog.WriteError(message);
            else MMLog.WriteWarning(message);
        }
        private static PatchDiagnosticSeverity RequiredSeverity(IlTranspilerPlan plan)
        {
            return plan.Operations.Any(operation => operation.Required)
                ? PatchDiagnosticSeverity.Error : PatchDiagnosticSeverity.Warning;
        }
    }
}
