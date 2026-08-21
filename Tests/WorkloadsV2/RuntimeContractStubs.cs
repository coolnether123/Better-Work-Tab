namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    // WorkloadBackendContracts.cs exposes this result type to callers, while
    // the backend implementation itself is intentionally outside this pure
    // deterministic project. The stub keeps that public contract compilable
    // without pulling RimWorld services into the test executable.
    public sealed class WorkloadV2CommitResult
    {
    }
}
