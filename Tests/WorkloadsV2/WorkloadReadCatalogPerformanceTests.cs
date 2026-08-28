using System;
using System.Collections.Generic;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkloadReadCatalogPerformanceTests
    {
        public static void Run()
        {
            RepeatedReadsReuseOneValidatedCatalog();
            MutationValidationRebuildsTheCatalog();
            VerifiedMutationPublishesTheCatalogWithoutASecondScan();
            ProductionReadsDoNotRevalidatePersistence();
            DefensiveAuditStaysOutOfTheWorkTabUi();
            FooterLegacyPayloadCheckIsRevisionCached();
            InspectionClearingDoesNotEvictFooterDiffs();
            RuntimeScopeMembershipIsConstantTime();
        }

        private static void RepeatedReadsReuseOneValidatedCatalog()
        {
            WorkloadV2PersistenceEnvelope store = CreateStore();
            var catalog = new WorkloadV2DescriptorCatalog();

            catalog.EnsureCurrent(store);
            IReadOnlyList<WorkloadDescriptor> firstList = catalog.Descriptors;
            WorkloadOperationResult<WorkloadDescriptor> firstCurrent = catalog.Current;
            long diagnosticsRevision = store.DiagnosticsRevision;

            for (int i = 0; i < 1000; i++)
            {
                store.EnsureDiagnosticsCurrent();
                catalog.EnsureCurrent(store);
                TestAssert.True(
                    ReferenceEquals(firstList, catalog.Descriptors),
                    "unchanged footer reads must reuse the descriptor list");
                TestAssert.True(
                    ReferenceEquals(firstCurrent, catalog.Current),
                    "unchanged current-workload reads must reuse the result");
            }

            TestAssert.Equal(
                diagnosticsRevision,
                store.DiagnosticsRevision,
                "unchanged reads must not rerun full persistence diagnostics");
            TestAssert.Equal(2, firstList.Count, "the read model must retain every valid workload");
            TestAssert.True(firstCurrent.Succeeded, "the selected workload must remain available");
            TestAssert.Equal("night-shift", firstCurrent.Value.StableId,
                "the selected workload identity must remain unchanged");
        }

        private static void MutationValidationRebuildsTheCatalog()
        {
            WorkloadV2PersistenceEnvelope store = CreateStore();
            var catalog = new WorkloadV2DescriptorCatalog();
            catalog.EnsureCurrent(store);
            IReadOnlyList<WorkloadDescriptor> before = catalog.Descriptors;
            long diagnosticsRevision = store.DiagnosticsRevision;

            store.Records[0].Label = "Late Shift";
            store.RefreshDiagnostics();
            catalog.EnsureCurrent(store);

            TestAssert.False(
                ReferenceEquals(before, catalog.Descriptors),
                "a notified mutation must publish a new descriptor list");
            TestAssert.True(
                store.DiagnosticsRevision > diagnosticsRevision,
                "a mutation boundary must advance the diagnostic revision");
            TestAssert.Equal("Late Shift", catalog.Current.Value.Label,
                "the rebuilt catalog must expose the mutated label");

            WorkloadOperationResult<WorkloadDescriptor> beforeRevisionChange = catalog.Current;
            store.PersistenceRevision++;
            catalog.EnsureCurrent(store);
            TestAssert.False(
                ReferenceEquals(beforeRevisionChange, catalog.Current),
                "a persistence revision change must invalidate the cached current result");
        }

        private static void ProductionReadsDoNotRevalidatePersistence()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine(
                    "Source",
                    "Features",
                    "Workloads",
                    "V2",
                    "Runtime",
                    "Workload2Backend.cs"),
                "workload read catalog contracts");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string component = Read(root, "Source", "Features", "Workloads", "GameComponent_BWTWorldSettings.cs");
            string list = MethodBody(backend, "internal IReadOnlyList<WorkloadDescriptor> List()");
            string current = MethodBody(backend, "internal WorkloadOperationResult<WorkloadDescriptor> Current()");
            string ensure = MethodBody(component, "internal WorkloadV2PersistenceEnvelope EnsureWorkloadV2Persistence()");
            string notify = MethodBody(component, "internal void NotifyWorkloadV2Changed(");
            string receipt = MethodBody(
                backend,
                "private static WorkloadOperationResult<WorkloadPersistenceReceipt> BuildPersistenceReceipt(");

            TestAssert.Contains(list, "_descriptorCatalog.EnsureCurrent(store);",
                "workload listing must use the validated read model");
            TestAssert.Contains(current, "return _descriptorCatalog.Current;",
                "current-workload reads must return the cached result");
            TestAssert.False(list.IndexOf("RefreshDiagnostics", StringComparison.Ordinal) >= 0,
                "workload listing must not validate persistence in the UI read path");
            TestAssert.False(current.IndexOf("RefreshDiagnostics", StringComparison.Ordinal) >= 0,
                "current-workload reads must not validate persistence in the UI read path");
            TestAssert.Contains(ensure, "WorkloadsV2.EnsureDiagnosticsCurrent();",
                "the common store accessor must use the cached diagnostic state");
            TestAssert.False(ensure.IndexOf("RefreshDiagnostics", StringComparison.Ordinal) >= 0,
                "the common store accessor must not force full validation");
            TestAssert.Contains(notify, "if (diagnosticsAlreadyVerified)",
                "only a controlled writer may publish an already-verified catalog");
            TestAssert.Contains(notify, "WorkloadsV2?.RefreshDiagnostics();",
                "unverified, external, and recovery notifications must still validate the document");
            TestAssert.Contains(notify, "WorkloadsV2?.MarkVerifiedMutationDiagnosticsCurrent();",
                "the final local or confirmed publication must publish the verified catalog revision");
            TestAssert.Contains(receipt, "if (mutation?.WasApplied == true)",
                "only an applied controlled persistence write may mark diagnostics verified");
            TestAssert.Contains(receipt, "mutation.DiagnosticsVerified = true;",
                "the post-write receipt must attest that final publication can skip diagnostics");
            TestAssert.True(
                receipt.IndexOf("store.ComputeContentFingerprint();", StringComparison.Ordinal) >= 0 &&
                receipt.IndexOf("mutation.DiagnosticsVerified = true;", StringComparison.Ordinal) >
                    receipt.IndexOf("store.ComputeContentFingerprint();", StringComparison.Ordinal),
                "the verified-publication attestation must follow receipt fingerprint verification");
        }

        private static void VerifiedMutationPublishesTheCatalogWithoutASecondScan()
        {
            WorkloadV2PersistenceEnvelope store = CreateStore();
            var catalog = new WorkloadV2DescriptorCatalog();
            catalog.EnsureCurrent(store);
            IReadOnlyList<WorkloadDescriptor> before = catalog.Descriptors;
            long diagnosticsRevision = store.DiagnosticsRevision;

            // This simulates the state after the controlled writer's exact
            // CAS and receipt verification. The production entry point that
            // makes this call is source-locked below.
            store.Records[0].Label = "Verified Late Shift";
            store.MarkVerifiedMutationDiagnosticsCurrent();
            catalog.EnsureCurrent(store);

            TestAssert.True(
                store.DiagnosticsRevision > diagnosticsRevision,
                "a verified mutation must advance the catalog revision");
            TestAssert.False(
                ReferenceEquals(before, catalog.Descriptors),
                "the descriptor catalog must rebuild from a verified mutation revision");
            TestAssert.Equal("Verified Late Shift", catalog.Current.Value.Label,
                "the verified mutation catalog must expose the post-write record");
        }

        private static void DefensiveAuditStaysOutOfTheWorkTabUi()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine(
                    "Source",
                    "Features",
                    "Workloads",
                    "GameComponent_BWTWorldSettings.cs"),
                "workload diagnostic audit contracts");
            string component = Read(root, "Source", "Features", "Workloads", "GameComponent_BWTWorldSettings.cs");
            string persistence = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "WorkloadV2Persistence.cs");
            string update = MethodBody(component, "public override void GameComponentUpdate()");
            string audit = MethodBody(component, "private void AuditWorkloadV2DiagnosticsIfDue()");
            string refresh = MethodBody(persistence, "public void RefreshDiagnostics()");

            TestAssert.Contains(update, "AuditWorkloadV2DiagnosticsIfDue();",
                "the component update loop must own the defensive validation audit");
            TestAssert.Contains(component, "WorkloadDiagnosticsAuditFrameInterval = 3600",
                "the defensive audit must remain infrequent");
            int workTabGuard = audit.IndexOf(
                "TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork",
                StringComparison.Ordinal);
            int validation = audit.IndexOf("WorkloadsV2?.RefreshDiagnostics();", StringComparison.Ordinal);
            TestAssert.True(workTabGuard >= 0 && validation > workTabGuard,
                "the defensive audit must defer validation while the BWT window is open");
            TestAssert.False(update.IndexOf("EnsureWorkloadV2Persistence()", StringComparison.Ordinal) >= 0,
                "the per-frame update path must not fetch or validate the workload store before the audit is due");

            int core = refresh.IndexOf("RefreshDiagnosticsCore();", StringComparison.Ordinal);
            int publishCurrent = refresh.IndexOf("_diagnosticsCurrent = true;", StringComparison.Ordinal);
            int publishRevision = refresh.IndexOf("_diagnosticsRevision++;", StringComparison.Ordinal);
            TestAssert.True(core >= 0 && publishCurrent > core && publishRevision > publishCurrent,
                "diagnostic state and revision must publish only after validation completes");
            TestAssert.False(refresh.IndexOf("finally", StringComparison.Ordinal) >= 0,
                "an exception during validation must leave diagnostics stale and fail closed");
        }

        private static void FooterLegacyPayloadCheckIsRevisionCached()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadGateway.cs"),
                "workload footer legacy-payload cache contracts");
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string property = MethodBody(
                gateway,
                "internal bool HasUnsupportedOwnedPresentationState");
            string compute = MethodBody(
                gateway,
                "private bool ComputeUnsupportedOwnedPresentationState()");

            TestAssert.Contains(
                property,
                "_unsupportedPresentationSessionRevision == _session.SessionRevision",
                "footer capability reads must reuse the legacy-payload result for one session revision");
            TestAssert.Contains(
                property,
                "ComputeUnsupportedOwnedPresentationState()",
                "a changed session revision must recompute the fail-closed result");
            TestAssert.Contains(
                compute,
                "WorkloadV2OwnershipResolver.HasLegacyPayload",
                "the revision cache must retain legacy payload validation rather than bypass it");
            TestAssert.False(
                property.IndexOf("HasLegacyPayload", StringComparison.Ordinal) >= 0,
                "the per-button property path must not rescan workload payload collections");
        }

        private static void InspectionClearingDoesNotEvictFooterDiffs()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadGateway.cs"),
                "workload footer semantic-diff cache contracts");
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string clearInspection = MethodBody(
                gateway,
                "private void ClearInspectionIndex()");
            string clearSemantic = MethodBody(
                gateway,
                "private void ClearSemanticDiffCache()");
            string clearSession = MethodBody(
                gateway,
                "private void ClearLocalSession()");

            TestAssert.False(
                clearInspection.IndexOf("_semanticDiffSession", StringComparison.Ordinal) >= 0 ||
                clearInspection.IndexOf("_cachedTemplateDiff", StringComparison.Ordinal) >= 0 ||
                clearInspection.IndexOf("_cachedLiveDiff", StringComparison.Ordinal) >= 0,
                "turning inspection off must not evict the independent footer semantic-diff cache");
            TestAssert.Contains(
                clearSemantic,
                "_semanticDiffSessionRevision = long.MinValue;",
                "semantic-diff cache invalidation must remain explicit");
            TestAssert.Contains(
                clearSession,
                "ClearSemanticDiffCache();",
                "closing a preview must release its cached semantic diffs");
        }

        private static void RuntimeScopeMembershipIsConstantTime()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs"),
                "workload runtime scope performance contracts");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string context = MethodBody(backend, "private sealed class RuntimeContext");

            TestAssert.Contains(
                context,
                "private readonly HashSet<Pawn> _currentMapFreeColonists;",
                "runtime capture must index the current-map free-colonist roster once");
            TestAssert.Contains(
                context,
                "_currentMapFreeColonists.Contains(pawn)",
                "per-entry scope checks must use constant-time roster membership");
            TestAssert.False(
                context.IndexOf("mapPawns.FreeColonists.Contains(pawn)", StringComparison.Ordinal) >= 0,
                "per-entry workload capture must not linearly rescan the colony roster");
        }

        private static WorkloadV2PersistenceEnvelope CreateStore()
        {
            WorkloadV2PersistenceEnvelope store = WorkloadV2PersistenceEnvelope.CreateEmpty();
            store.Records.Add(TestSupport.CurrentRecord("night-shift"));
            store.Records.Add(new WorkloadV2PersistenceRecord
            {
                StableId = "day-shift",
                Label = "Day Shift",
                SchemaVersion = WorkloadSchema.CurrentVersion,
                OwnershipDimensions = (int)WorkloadOwnershipDimensions.All,
                ScopeMode = (int)WorkloadScopeMode.ExplicitPawnIds,
                ExplicitPawnIds = new List<string> { "p1" }
            });
            store.CurrentWorkloadId = "night-shift";
            store.RefreshDiagnostics();
            return store;
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++) path = Path.Combine(path, parts[i]);
            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "expected production method is missing: " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "expected production method body is missing: " + signature);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                if (source[i] != '}') continue;
                depth--;
                if (depth == 0) return source.Substring(open, i - open + 1);
            }

            throw new InvalidOperationException("expected production method end is missing: " + signature);
        }
    }
}
