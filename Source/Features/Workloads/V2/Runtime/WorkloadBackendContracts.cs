using System;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
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
        NotFound = 17
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
