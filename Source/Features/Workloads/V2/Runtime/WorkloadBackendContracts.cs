using System;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    public enum WorkloadMultiplayerCommitState
    {
        None = 0,
        Pending = 1,
        Prepared = 2,
        ExecutedAwaitingConfirmation = 3,
        Succeeded = 4,
        Rejected = 5,
        Aborted = 6,
        TimedOut = 7,
        Failed = 8,
        RolledBack = 9,
        RollbackFailed = 10
    }

    public sealed class WorkloadMultiplayerCommitStatus
    {
        internal WorkloadMultiplayerCommitStatus(
            string requestId,
            WorkloadMultiplayerCommitState state,
            WorkloadDiagnosticCode code,
            string message,
            WorkloadV2CommitResult result = null)
        {
            RequestId = requestId ?? string.Empty;
            State = state;
            Code = code;
            Message = message ?? string.Empty;
            Result = result;
        }

        public string RequestId { get; private set; }
        public WorkloadMultiplayerCommitState State { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public string Message { get; private set; }
        public WorkloadV2CommitResult Result { get; private set; }
        public bool IsTerminal =>
            State == WorkloadMultiplayerCommitState.Succeeded ||
            State == WorkloadMultiplayerCommitState.Rejected ||
            State == WorkloadMultiplayerCommitState.Aborted ||
            State == WorkloadMultiplayerCommitState.TimedOut ||
            State == WorkloadMultiplayerCommitState.Failed ||
            State == WorkloadMultiplayerCommitState.RolledBack ||
            State == WorkloadMultiplayerCommitState.RollbackFailed;

        /// <summary>
        /// True when the lifecycle action was accepted locally, including a
        /// terminal success that will be completed by the next UI frame.
        /// </summary>
        public bool IsAccepted =>
            State == WorkloadMultiplayerCommitState.Pending ||
            State == WorkloadMultiplayerCommitState.Prepared ||
            State == WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation ||
            State == WorkloadMultiplayerCommitState.Succeeded;
    }

    public enum WorkloadBackendMode
    {
        Legacy = 0,
        Modern = 1,
        Workload2 = Modern
    }

    public enum WorkloadDiagnosticCode
    {
        None = 0,
        NoCurrentMap = 1,
        NoCurrentGame = 2,
        NoSettings = 3,
        MissingStableId = 4,
        MissingCurrentWorkloadId = 5,
        UnknownWorkloadId = 6,
        AmbiguousStableId = 7,
        UnsupportedSchema = 8,
        NewerSchema = 9,
        ReadOnlyDiagnostic = 10,
        InvalidScopeMode = 11,
        InvalidState = 12,
        InvalidLabel = 13,
        ExternalPriorityAuthority = 14,
        BlockedModeTransition = 15,
        UnsupportedOperation = 16,
        NotFound = 17,
        PersistenceConflict = 18,
        BaselineChanged = 19,
        InvalidPermutation = 20,
        InvalidOwnership = 21,
        UnsupportedLegacyState = 22,
        MutationCapabilityRejected = 23,
        RollbackRequired = 24,
        RollbackFailed = 25
    }

    public sealed class WorkloadOperationResult
    {
        private WorkloadOperationResult(
            bool succeeded,
            WorkloadDiagnosticCode code,
            string message)
        {
            Succeeded = succeeded;
            Code = code;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public string Message { get; private set; }

        public static WorkloadOperationResult Ok(string message = null)
        {
            return new WorkloadOperationResult(true, WorkloadDiagnosticCode.None, message);
        }

        public static WorkloadOperationResult Fail(WorkloadDiagnosticCode code, string message)
        {
            return new WorkloadOperationResult(false, code, message);
        }
    }

    public sealed class WorkloadOperationResult<T>
    {
        private WorkloadOperationResult(
            bool succeeded,
            WorkloadDiagnosticCode code,
            string message,
            T value)
        {
            Succeeded = succeeded;
            Code = code;
            Message = message ?? string.Empty;
            Value = value;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public string Message { get; private set; }
        public T Value { get; private set; }

        public static WorkloadOperationResult<T> Ok(T value, string message = null)
        {
            return new WorkloadOperationResult<T>(true, WorkloadDiagnosticCode.None, message, value);
        }

        public static WorkloadOperationResult<T> Fail(WorkloadDiagnosticCode code, string message)
        {
            return new WorkloadOperationResult<T>(false, code, message, default(T));
        }
    }

    /// <summary>
    /// Structured result of the backend prepare/execute/rollback lifecycle.
    /// It is intentionally independent of the wire protocol so a later
    /// MultiplayerBridge worker can map it to acknowledgements without
    /// recreating workload transaction logic.
    /// </summary>
    internal enum WorkloadBackendTransactionPhase
    {
        None = 0,
        Prepared = 1,
        Executed = 2,
        RolledBack = 3,
        RollbackFailed = 4
    }

    internal sealed class WorkloadBackendTransactionResult
    {
        internal WorkloadBackendTransactionResult(
            WorkloadBackendTransactionPhase phase,
            bool succeeded,
            WorkloadDiagnosticCode code,
            string message,
            WorkloadV2CommitResult commitResult = null)
        {
            Phase = phase;
            Succeeded = succeeded;
            Code = code;
            Message = message ?? string.Empty;
            CommitResult = commitResult;
        }

        internal WorkloadBackendTransactionPhase Phase { get; private set; }
        internal bool Succeeded { get; private set; }
        internal WorkloadDiagnosticCode Code { get; private set; }
        internal string Message { get; private set; }
        internal WorkloadV2CommitResult CommitResult { get; private set; }
        internal bool RequiresRecovery => Phase == WorkloadBackendTransactionPhase.RollbackFailed;
    }

    public sealed class WorkloadDescriptor
    {
        public WorkloadDescriptor(
            WorkloadBackendMode backend,
            string stableId,
            string label,
            bool isCurrent,
            bool isReadOnly,
            bool hasStableId,
            int schemaVersion,
            WorkloadDiagnosticCode diagnosticCode = WorkloadDiagnosticCode.None,
            string diagnostic = null)
        {
            Backend = backend;
            StableId = stableId ?? string.Empty;
            Label = label ?? string.Empty;
            IsCurrent = isCurrent;
            IsReadOnly = isReadOnly;
            HasStableId = hasStableId;
            SchemaVersion = schemaVersion;
            DiagnosticCode = diagnosticCode;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public WorkloadBackendMode Backend { get; private set; }
        public string StableId { get; private set; }
        public string Label { get; private set; }
        public bool IsCurrent { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool HasStableId { get; private set; }
        public int SchemaVersion { get; private set; }
        public WorkloadDiagnosticCode DiagnosticCode { get; private set; }
        public string Diagnostic { get; private set; }
    }
}
