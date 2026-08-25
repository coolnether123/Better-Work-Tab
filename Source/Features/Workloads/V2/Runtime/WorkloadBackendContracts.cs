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
            WorkloadDiagnosticContext context = null,
            WorkloadV2CommitResult result = null)
        {
            RequestId = requestId ?? string.Empty;
            State = state;
            Code = code;
            Context = context ?? WorkloadDiagnosticContext.Empty;
            Result = result;
        }

        public string RequestId { get; private set; }
        public WorkloadMultiplayerCommitState State { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public WorkloadDiagnosticContext Context { get; private set; }
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
        // Value 16 was the removed coarse UnsupportedOperation code. Keep the
        // gap so existing diagnostic identifiers retain their numeric values.
        NotFound = 17,
        PersistenceConflict = 18,
        BaselineChanged = 19,
        InvalidPermutation = 20,
        InvalidOwnership = 21,
        UnsupportedLegacyState = 22,
        MutationCapabilityRejected = 23,
        RollbackRequired = 24,
        RollbackFailed = 25,
        ModeUnavailable = 26,
        UnsupportedDecision = 27,
        UnsupportedClear = 28,
        CaptureFailed = 29,
        MultiplayerUnavailable = 30,
        UnsupportedPresentationData = 31,
        UnsupportedRuntimeState = 32
    }

    /// <summary>
    /// Structured values that qualify a workload diagnostic. These values are
    /// stable inputs to presentation and logging. They never contain completed
    /// player-facing sentences or exception text.
    /// </summary>
    public sealed class WorkloadDiagnosticContext
    {
        private static readonly WorkloadDiagnosticContext EmptyContext =
            new WorkloadDiagnosticContext();

        public WorkloadDiagnosticContext(
            string stableId = null,
            string path = null,
            string peerId = null,
            WorkloadValidationCode? validationCode = null,
            int? expectedVersion = null,
            int? actualVersion = null)
        {
            StableId = stableId ?? string.Empty;
            Path = path ?? string.Empty;
            PeerId = peerId ?? string.Empty;
            ValidationCode = validationCode;
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }

        public static WorkloadDiagnosticContext Empty => EmptyContext;
        public string StableId { get; private set; }
        public string Path { get; private set; }
        public string PeerId { get; private set; }
        public WorkloadValidationCode? ValidationCode { get; private set; }
        public int? ExpectedVersion { get; private set; }
        public int? ActualVersion { get; private set; }
    }

    public sealed class WorkloadOperationResult
    {
        private WorkloadOperationResult(
            bool succeeded,
            WorkloadDiagnosticCode code,
            WorkloadDiagnosticContext context)
        {
            Succeeded = succeeded;
            Code = code;
            Context = context ?? WorkloadDiagnosticContext.Empty;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public WorkloadDiagnosticContext Context { get; private set; }

        public static WorkloadOperationResult Ok()
        {
            return new WorkloadOperationResult(true, WorkloadDiagnosticCode.None, null);
        }

        public static WorkloadOperationResult Fail(
            WorkloadDiagnosticCode code,
            WorkloadDiagnosticContext context = null)
        {
            return new WorkloadOperationResult(false, code, context);
        }
    }

    public sealed class WorkloadOperationResult<T>
    {
        private WorkloadOperationResult(
            bool succeeded,
            WorkloadDiagnosticCode code,
            WorkloadDiagnosticContext context,
            T value)
        {
            Succeeded = succeeded;
            Code = code;
            Context = context ?? WorkloadDiagnosticContext.Empty;
            Value = value;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public WorkloadDiagnosticContext Context { get; private set; }
        public T Value { get; private set; }

        public static WorkloadOperationResult<T> Ok(T value)
        {
            return new WorkloadOperationResult<T>(true, WorkloadDiagnosticCode.None, null, value);
        }

        public static WorkloadOperationResult<T> Fail(
            WorkloadDiagnosticCode code,
            WorkloadDiagnosticContext context = null)
        {
            return new WorkloadOperationResult<T>(false, code, context, default(T));
        }
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
            WorkloadDiagnosticCode diagnosticCode = WorkloadDiagnosticCode.None)
        {
            Backend = backend;
            StableId = stableId ?? string.Empty;
            Label = label ?? string.Empty;
            IsCurrent = isCurrent;
            IsReadOnly = isReadOnly;
            HasStableId = hasStableId;
            SchemaVersion = schemaVersion;
            DiagnosticCode = diagnosticCode;
        }

        public WorkloadBackendMode Backend { get; private set; }
        public string StableId { get; private set; }
        public string Label { get; private set; }
        public bool IsCurrent { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool HasStableId { get; private set; }
        public int SchemaVersion { get; private set; }
        public WorkloadDiagnosticCode DiagnosticCode { get; private set; }
    }
}
