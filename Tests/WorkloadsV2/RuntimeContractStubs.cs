namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    // WorkloadBackendContracts.cs exposes this result type to callers, while
    // the backend implementation itself is intentionally outside this pure
    // deterministic project. The stub keeps that public contract compilable
    // without pulling RimWorld services into the test executable.
    public sealed class WorkloadV2CommitResult
    {
        public bool Succeeded { get; set; }
        public WorkloadDiagnosticCode Code { get; set; }
        public WorkloadDiagnosticContext Context { get; set; }
        public WorkloadV2CommitReport Report { get; set; }
    }

    public sealed class WorkloadV2CommitReport
    {
        public Better_Work_Tab.Features.Workloads.V2.WorkloadDecisionKind DecisionKind { get; set; }
        public bool IsPartial { get; set; }
        public bool IsSemanticNoOp { get; set; }
    }
}
