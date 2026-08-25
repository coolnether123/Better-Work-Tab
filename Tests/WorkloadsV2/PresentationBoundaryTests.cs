using System;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.UI.Workloads;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PresentationBoundaryTests
    {
        public static void Run()
        {
            ResultContractsAreStructured();
            ResolverLocalizesRepresentativeCodes();
            ResolverDistinguishesCommitOutcomes();
            SourceKeepsPresentationAtTheUiBoundary();
        }

        private static void ResultContractsAreStructured()
        {
            TestAssert.True(typeof(WorkloadOperationResult).GetProperty("Message") == null,
                "non-generic workload results must not expose player-facing prose");
            TestAssert.True(typeof(WorkloadOperationResult<WorkloadTemplate>).GetProperty("Message") == null,
                "generic workload results must not expose player-facing prose");
            TestAssert.True(typeof(WorkloadMultiplayerCommitStatus).GetProperty("Message") == null,
                "multiplayer workload status must not expose player-facing prose");
            TestAssert.True(typeof(WorkloadValidationIssue).GetProperty("Message") == null,
                "validation issues must be code, severity, and path only");
            TestAssert.True(typeof(WorkloadDescriptor).GetProperty("Diagnostic") == null,
                "workload descriptors must not carry diagnostic prose");
            TestAssert.True(typeof(WorkloadSessionDecision).GetProperty("RejectionReason") == null,
                "session decisions must expose rejection facts instead of prose");
            var context = new WorkloadDiagnosticContext(
                stableId: "night-shift",
                path: "state.schedules[0]",
                validationCode: WorkloadValidationCode.InvalidSchedulePayload);
            WorkloadOperationResult failed = WorkloadOperationResult.Fail(
                WorkloadDiagnosticCode.InvalidState,
                context);
            TestAssert.False(failed.Succeeded, "structured failure must retain failure state");
            TestAssert.Equal(WorkloadDiagnosticCode.InvalidState, failed.Code,
                "structured failure must retain its stable code");
            TestAssert.Equal("state.schedules[0]", failed.Context.Path,
                "structured failure must retain its presentation argument");
        }

        private static void ResolverLocalizesRepresentativeCodes()
        {
            TestAssert.Equal(
                "Load or start a game before using workloads.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.NoCurrentGame)),
                "the presentation boundary must localize no-game failures");
            TestAssert.Equal(
                "Enter a workload name.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.InvalidLabel)),
                "the presentation boundary must localize invalid labels");
            TestAssert.Equal(
                "The workload changed or conflicts with another saved workload. Refresh the preview and try again.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.PersistenceConflict)),
                "the presentation boundary must localize persistence conflicts");
            TestAssert.Equal(
                "Cannot apply this workload because the saved data at definition.scope is invalid.",
                WorkloadPresentationResolver.Resolve(new WorkloadValidationIssue(
                    WorkloadValidationSeverity.Error,
                    WorkloadValidationCode.InvalidScopeMode,
                "definition.scope")),
                "validation paths must remain structured until localized");
            TestAssert.Equal(
                "The specific-job change could not be applied.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult<WorkloadSession>.Fail(
                        WorkloadDiagnosticCode.InvalidState),
                    "The specific-job change could not be applied."),
                "generic structured failures must retain a caller-localized UI fallback");
            TestAssert.Equal(
                "Enter a workload name.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult<WorkloadSession>.Fail(
                        WorkloadDiagnosticCode.InvalidLabel),
                    "The specific-job change could not be applied."),
                "specific resolver output must take precedence over a caller fallback");
            TestAssert.Equal(
                "Cannot apply this workload because required data is missing at definition.label.",
                WorkloadPresentationResolver.Resolve(new WorkloadValidationIssue(
                    WorkloadValidationSeverity.Error,
                    WorkloadValidationCode.MissingLabel,
                    "definition.label")),
                "missing validation data must be resolved from its code, not merely its path");
            TestAssert.Equal(
                "Cannot apply this workload because state.parentPriorities[0].pawn refers to something that is no longer available.",
                WorkloadPresentationResolver.Resolve(new WorkloadValidationIssue(
                    WorkloadValidationSeverity.Error,
                    WorkloadValidationCode.UnknownPawnId,
                    "state.parentPriorities[0].pawn")),
                "unknown identities must have a distinct localized validation presentation");
            TestAssert.Equal(
                "Cannot apply this workload because saved entries conflict at state.manualModes.",
                WorkloadPresentationResolver.Resolve(new WorkloadValidationIssue(
                    WorkloadValidationSeverity.Error,
                    WorkloadValidationCode.ConflictingManualModes,
                    "state.manualModes")),
                "conflicting validation facts must not be flattened into generic invalid-path prose");

            TestAssert.Equal(
                "That workload mode is unavailable.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.ModeUnavailable)),
                "an unsupported mode transition must use mode wording");
            TestAssert.Equal(
                "This workload contains data from an older or unsupported format. Apply and save are disabled to avoid losing it.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.UnsupportedLegacyState)),
                "unsupported legacy data must use data wording");
            TestAssert.Equal(
                "Better Work Tab could not start the multiplayer workload action.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.MultiplayerUnavailable)),
                "multiplayer admission failure must not be presented as an unavailable workload mode");
            TestAssert.Equal(
                "Better Work Tab could not read all current Work-tab settings.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.CaptureFailed)),
                "capture exceptions must resolve from a dedicated structured code");
            TestAssert.Equal(
                "Better Work Tab could not finish that workload action.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.UnsupportedDecision)),
                "an unsupported internal decision must not be presented as an unavailable workload mode");
            TestAssert.Equal(
                "Better Work Tab could not find that workload.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.NotFound)),
                "a lookup miss must use lookup wording");
            TestAssert.Equal(
                "Another Work-tab system controls the affected priorities, so Better Work Tab cannot change them.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.ExternalPriorityAuthority)),
                "authority failures must use authority wording");
            TestAssert.Equal(
                "Multiplayer is still finishing the previous workload action. You cannot edit this preview yet.",
                WorkloadPresentationResolver.Resolve(
                    WorkloadOperationResult.Fail(WorkloadDiagnosticCode.RollbackFailed)),
                "rollback failure must use recovery wording without diagnostic details");
            TestAssert.Equal(
                "This preview cannot clear hourly priorities yet. No changes were made.",
                WorkloadPresentationResolver.ResolveUnsupportedClear(
                    new[] { WorkloadStateDimension.Schedules }),
                "unsupported clears must carry dimensions as structured presentation arguments");
        }

        private static void ResolverDistinguishesCommitOutcomes()
        {
            TestAssert.Equal(
                "Workload applied.",
                WorkloadPresentationResolver.Resolve(new WorkloadV2CommitResult
                {
                    Succeeded = true,
                    Code = WorkloadDiagnosticCode.None,
                    Report = new WorkloadV2CommitReport
                    {
                        DecisionKind = WorkloadDecisionKind.Apply
                    }
                }),
                "successful Apply must not resolve to an empty message");
            TestAssert.Equal(
                "Workload applied, but some entries were skipped.",
                WorkloadPresentationResolver.Resolve(new WorkloadV2CommitResult
                {
                    Succeeded = true,
                    Code = WorkloadDiagnosticCode.None,
                    Report = new WorkloadV2CommitReport
                    {
                        DecisionKind = WorkloadDecisionKind.Apply,
                        IsPartial = true
                    }
                }),
                "partial Apply must retain its structured partial outcome");
            TestAssert.Equal(
                "Workload saved.",
                WorkloadPresentationResolver.Resolve(new WorkloadV2CommitResult
                {
                    Succeeded = true,
                    Code = WorkloadDiagnosticCode.None,
                    Report = new WorkloadV2CommitReport
                    {
                        DecisionKind = WorkloadDecisionKind.Update
                    }
                }),
                "successful Update must use save wording");
            TestAssert.Equal(
                "New workload saved.",
                WorkloadPresentationResolver.Resolve(new WorkloadV2CommitResult
                {
                    Succeeded = true,
                    Code = WorkloadDiagnosticCode.None,
                    Report = new WorkloadV2CommitReport
                    {
                        DecisionKind = WorkloadDecisionKind.Fork
                    }
                }),
                "successful Fork must use new-workload wording");

            var applied = new WorkloadV2CommitResult
            {
                Succeeded = true,
                Code = WorkloadDiagnosticCode.None,
                Report = new WorkloadV2CommitReport
                {
                    DecisionKind = WorkloadDecisionKind.Apply
                }
            };
            TestAssert.Equal(
                "Workload applied.",
                WorkloadPresentationResolver.Resolve(
                    new WorkloadMultiplayerCommitStatus(
                        "request",
                        WorkloadMultiplayerCommitState.Succeeded,
                        WorkloadDiagnosticCode.None,
                        result: applied)),
                "terminal multiplayer success must resolve the structured commit outcome");
            TestAssert.Equal(
                "Multiplayer is still finishing the previous workload action. You cannot edit this preview yet.",
                WorkloadPresentationResolver.Resolve(
                    new WorkloadMultiplayerCommitStatus(
                        "request",
                        WorkloadMultiplayerCommitState.RollbackFailed,
                        WorkloadDiagnosticCode.RollbackFailed,
                        result: applied)),
                "a rolled-back provisional success must resolve rollback state, not stale success");
        }

        private static void SourceKeepsPresentationAtTheUiBoundary()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Workloads", "V2", "Runtime", "WorkloadBackendContracts.cs"),
                "workload presentation boundary contracts");
            string runtimeRoot = Path.Combine(root, "Source", "Features", "Workloads", "V2");
            foreach (string file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                TestAssert.False(source.IndexOf(".Translate(", StringComparison.Ordinal) >= 0,
                    "workload domain/runtime code must not translate result data: " + file);
            }

            string gateway = File.ReadAllText(Path.Combine(
                root, "Source", "UI", "Workloads", "WorkloadGateway.cs"));
            string header = File.ReadAllText(Path.Combine(root, "Source", "UI", "HeaderButtons.cs"));
            string settingsJson = File.ReadAllText(Path.Combine(
                root, "Source", "UI", "Settings", "BWTSettingsJsonService.cs"));
            string settingsRouter = File.ReadAllText(Path.Combine(
                root, "Source", "UI", "Settings", "BWTWorkTabContextSettingsRouter.cs"));
            TestAssert.False(gateway.IndexOf("result.Message", StringComparison.Ordinal) >= 0,
                "the workload gateway must resolve structured results before display");
            TestAssert.False(gateway.IndexOf("status.Message", StringComparison.Ordinal) >= 0,
                "the workload gateway must resolve structured multiplayer status before display");
            TestAssert.False(header.IndexOf("result.Message", StringComparison.Ordinal) >= 0,
                "the workload footer must not render result-carried prose");
            TestAssert.False(settingsJson.IndexOf("workloadTransition.Message", StringComparison.Ordinal) >= 0,
                "settings import must resolve structured mode-transition results");
            TestAssert.False(settingsRouter.IndexOf("transition.Message", StringComparison.Ordinal) >= 0,
                "settings controls must resolve structured mode-transition results");
            TestAssert.False(settingsRouter.IndexOf(
                    "\"The global Better Work Tab presentation values could not be read safely: \" + ex.Message",
                    StringComparison.Ordinal) >= 0,
                "global workload presentation snapshot failures must not expose exception details");
            TestAssert.False(settingsRouter.IndexOf(
                    "\"The active workload preview could not be read safely: \" + ex.Message",
                    StringComparison.Ordinal) >= 0,
                "active workload preview snapshot failures must not expose exception details");
            TestAssert.Contains(settingsRouter,
                "[BWT] Workload settings global presentation snapshot read failed: \" + ex",
                "global workload presentation read exceptions must remain in technical logs");
            TestAssert.Contains(settingsRouter,
                "[BWT] Active workload presentation preview read failed: \" + ex",
                "active workload preview read exceptions must remain in technical logs");
            TestAssert.False(gateway.IndexOf("GetUnsupportedClearMessage", StringComparison.Ordinal) >= 0,
                "the UI must resolve structured unsupported-clear dimensions itself");
            TestAssert.Contains(gateway, "WorkloadPresentationResolver.Resolve(",
                "workload UI consumers must use the presentation resolver");
            TestAssert.Contains(gateway, "Resolve(result, failureMessage)",
                "preview draft editing must preserve its caller-localized failure fallback");

            string session = File.ReadAllText(Path.Combine(
                root, "Source", "Features", "Workloads", "V2", "WorkloadSession.cs"));
            TestAssert.False(session.IndexOf("GetUnsupportedClearMessage", StringComparison.Ordinal) >= 0,
                "the workload session must not build unsupported-clear presentation prose");

            string backend = File.ReadAllText(Path.Combine(
                root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs"));
            AssertTypeSourceHasNoPresentationProseProperties(
                backend,
                "public sealed class WorkloadV2CommitEntry",
                "public sealed class WorkloadV2CommitReport",
                "V2 commit report entries");
            AssertTypeSourceHasNoPresentationProseProperties(
                backend,
                "public sealed class WorkloadV2CommitResult",
                "internal sealed class WorkloadPreparedTransaction",
                "V2 commit results");
            int missingForkDecision = backend.IndexOf(
                "decision?.RejectionCode == WorkloadSessionDecisionCode.MissingForkStableId",
                StringComparison.Ordinal);
            TestAssert.True(missingForkDecision >= 0 && backend.IndexOf(
                    "WorkloadDiagnosticCode.MissingStableId",
                    missingForkDecision,
                    Math.Min(300, backend.Length - missingForkDecision),
                    StringComparison.Ordinal) >= 0,
                "missing fork identities must retain their structured diagnostic code");
            int ensureForkTarget = backend.IndexOf(
                "private static void EnsureForkTargetIsNew",
                StringComparison.Ordinal);
            TestAssert.True(ensureForkTarget >= 0 && backend.IndexOf(
                    "WorkloadDiagnosticCode.MissingStableId",
                    ensureForkTarget,
                    Math.Min(600, backend.Length - ensureForkTarget),
                    StringComparison.Ordinal) >= 0,
                "fork persistence preflight must retain the missing-identity diagnostic code");
            TestAssert.Contains(backend, "Log.Error(\"[BWT] Workloads V2 live baseline capture failed: \" + exception)",
                "exception details must be retained in technical logs");
            int captureFailureLog = backend.IndexOf(
                "Log.Error(\"[BWT] Typed workload capture failed.\\n\" + exception)",
                StringComparison.Ordinal);
            TestAssert.True(captureFailureLog >= 0,
                "template-capture exception details must be retained in technical logs");
            TestAssert.True(backend.IndexOf(
                    "WorkloadDiagnosticCode.CaptureFailed",
                    captureFailureLog,
                    Math.Min(300, backend.Length - captureFailureLog),
                    StringComparison.Ordinal) >= 0,
                "template-capture exceptions must return a structured capture-failure code");
            TestAssert.False(gateway.IndexOf("exception.Message", StringComparison.Ordinal) >= 0,
                "exception details must not cross into workload presentation");
            TestAssert.False(gateway.IndexOf("FailureReason", StringComparison.Ordinal) >= 0,
                "technical presentation-transaction failure details must not be rendered by workload UI");
            TestAssert.False(File.ReadAllText(Path.Combine(
                    root, "Source", "UI", "Workloads", "WorkloadPresentationResolver.cs"))
                    .IndexOf("FailureReason", StringComparison.Ordinal) >= 0,
                "the resolver must consume stable diagnostic facts, not technical failure prose");

            string english = File.ReadAllText(Path.Combine(
                root, "Languages", "English", "Keyed", "English.xml"));
            TestAssert.Contains(english, "<BWT_Workload_ValidationMissingAtPath>",
                "representative validation-code presentation must have a localization key");
            TestAssert.Contains(english, "<BWT_Workload_AppliedPartial>",
                "structured partial Apply presentation must have a localization key");
            TestAssert.Contains(english, "<BWT_Workload_UnsupportedClear>",
                "structured unsupported-clear presentation must have a localization key");
        }

        private static void AssertTypeSourceHasNoPresentationProseProperties(
            string source,
            string startMarker,
            string endMarker,
            string contract)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            int end = start < 0
                ? -1
                : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            TestAssert.True(start >= 0 && end > start,
                contract + " must remain discoverable by the architecture contract");
            string typeSource = source.Substring(start, end - start);
            string[] names = { "Message", "Detail", "Diagnostic", "Reason", "Explanation" };
            for (int i = 0; i < names.Length; i++)
            {
                TestAssert.False(typeSource.IndexOf(
                        "string " + names[i],
                        StringComparison.Ordinal) >= 0,
                    contract + " must not expose free-form " + names[i] + " prose");
            }
        }
    }
}
