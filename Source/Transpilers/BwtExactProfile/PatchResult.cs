using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using HarmonyLib;

namespace Better_Work_Tab.Transpilers.BwtExactProfile
{
    /// <summary>Describes the outcome of a transactional IL patch.</summary>
    public enum PatchOutcome
    {
        Applied,
        AlreadyApplied,
        OptionalMatchMissing,
        Rejected,
        Failed
    }

    /// <summary>Severity assigned to a structured patch diagnostic.</summary>
    public enum PatchDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>Stable categories for patch results and safety failures.</summary>
    public enum PatchDiagnosticCode
    {
        Applied,
        AlreadyApplied,
        OptionalMatchMissing,
        NullInstructionStream,
        NullInstruction,
        InputEnumerationFault,
        InvalidPlan,
        NoMatch,
        AmbiguousMatch,
        IdempotencyMarkerAmbiguous,
        OverlappingEdits,
        EditOutOfRange,
        CallSignatureMismatch,
        InvalidOperand,
        InvalidBranchTarget,
        InvalidSwitchTarget,
        InvalidExceptionRegion,
        InvalidPrefix,
        MetadataNotPreserved,
        StackUnderflow,
        StackHeightMergeMismatch,
        StackTypeMergeMismatch,
        StackTypeMismatch,
        InvalidReturn,
        UnsupportedOpcode,
        UnsupportedSignature,
        UnsupportedMethodContext,
        StructuralValidationFailed,
        UnknownStackType,
        InternalException
    }

    /// <summary>Structured evidence explaining a patch result or safety decision.</summary>
    public sealed class PatchDiagnostic
    {
        public PatchDiagnostic(
            PatchDiagnosticCode code, PatchDiagnosticSeverity severity, string patchId,
            string operationId, string targetMethodId, int? startIndex, int? endExclusive,
            string message, string exceptionType = null, string exceptionMessage = null)
        {
            Code = code;
            Severity = severity;
            PatchId = IlMethodSnapshot.Trim(patchId, 96);
            OperationId = IlMethodSnapshot.Trim(operationId, 96);
            TargetMethodId = IlMethodSnapshot.Trim(targetMethodId, 384);
            StartIndex = startIndex;
            EndExclusive = endExclusive;
            Message = IlMethodSnapshot.Trim(message, 384);
            ExceptionType = IlMethodSnapshot.Trim(exceptionType, 192);
            ExceptionMessage = IlMethodSnapshot.Trim(exceptionMessage, 384);
        }
        public PatchDiagnosticCode Code { get; private set; }
        public PatchDiagnosticSeverity Severity { get; private set; }
        public string PatchId { get; private set; }
        public string OperationId { get; private set; }
        public string TargetMethodId { get; private set; }
        public int? StartIndex { get; private set; }
        public int? EndExclusive { get; private set; }
        public string Message { get; private set; }
        public string ExceptionType { get; private set; }
        public string ExceptionMessage { get; private set; }
        internal PatchDiagnostic WithContext(string patchId, string targetMethodId)
        {
            return new PatchDiagnostic(
                Code, Severity, patchId, OperationId, targetMethodId,
                StartIndex, EndExclusive, Message, ExceptionType, ExceptionMessage);
        }
        public override string ToString() { return Code + ": " + Message; }
    }

    /// <summary>The immutable instruction stream, outcome, and diagnostics of a patch plan.</summary>
    public sealed class BwtPatchResult : IEnumerable<CodeInstruction>
    {
        private const int MaximumDiagnostics = 128;
        private readonly List<CodeInstruction> _instructions;
        private int _failureReported;
        internal BwtPatchResult(
            PatchOutcome outcome, IEnumerable<CodeInstruction> instructions,
            IEnumerable<PatchDiagnostic> diagnostics, string patchId, string targetMethodId)
        {
            Outcome = outcome;
            _instructions = IlInstructionSnapshot.CloneList(instructions);
            var copy = new List<PatchDiagnostic>();
            if (diagnostics != null)
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic == null || copy.Count == MaximumDiagnostics) break;
                    copy.Add(diagnostic);
                }
            }
            Diagnostics = new ReadOnlyCollection<PatchDiagnostic>(copy);
            PatchId = IlMethodSnapshot.Trim(patchId, 96);
            TargetMethodId = IlMethodSnapshot.Trim(targetMethodId, 384);
        }
        public PatchOutcome Outcome { get; private set; }
        public string PatchId { get; private set; }
        public string TargetMethodId { get; private set; }
        public IReadOnlyList<PatchDiagnostic> Diagnostics { get; private set; }
        internal bool TryClaimFailureReport()
        {
            return Interlocked.CompareExchange(ref _failureReported, 1, 0) == 0;
        }
        public IReadOnlyList<CodeInstruction> Instructions
        {
            get
            {
                return new ReadOnlyCollection<CodeInstruction>(
                    IlInstructionSnapshot.CloneList(_instructions));
            }
        }
        public IEnumerator<CodeInstruction> GetEnumerator()
        {
            return IlInstructionSnapshot.CloneList(_instructions).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        internal string FormatDiagnostics()
        {
            if (Diagnostics.Count == 0) return "code=InternalException";
            var values = new List<string>();
            foreach (var diagnostic in Diagnostics)
                values.Add("code=" + diagnostic.Code + ",severity=" + diagnostic.Severity +
                    ",operation=" + (diagnostic.OperationId ?? "-") + ",message=" + diagnostic.Message);
            return String.Join(";", values.ToArray());
        }
    }
}
