using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Better_Work_Tab.Transpilers.BwtExactProfile;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal enum BwtRaisedPriorityInstallState
    {
        NotAttempted,
        PreflightRejected,
        Installing,
        Installed,
        AlreadyInstalled,
        RolledBack,
        RollbackFailed
    }

    internal enum BwtRaisedPriorityFeatureGateState
    {
        Inactive,
        Active,
        PreservedAfterRejectedReconfiguration
    }

    internal static class BwtRaisedPriorityPatchIds
    {
        internal const string TipForPawnWorker = "TipForPawnWorker";
        internal const string DrawWorkBoxFor = "DrawWorkBoxFor";
        internal const string HeaderClicked = "HeaderClicked";
        internal const string SetPriority = "SetPriority";
        internal const string DoHeader = "DoHeader";
        internal const string LabelDoCell = "Label.DoCell";
    }

    internal sealed class BwtRaisedPriorityPatchDefinition
    {
        internal BwtRaisedPriorityPatchDefinition(string id, MethodBase target, MethodInfo transpiler)
        {
            Id = id;
            Target = target;
            Transpiler = transpiler;
        }

        internal string Id { get; private set; }
        internal MethodBase Target { get; private set; }
        internal MethodInfo Transpiler { get; private set; }
    }

    internal sealed class BwtRaisedPriorityPatchAttempt
    {
        internal BwtRaisedPriorityPatchAttempt(
            string id, PatchOutcome outcome, IEnumerable<PatchDiagnostic> diagnostics, string detail)
        {
            Id = id;
            Outcome = outcome;
            Diagnostics = new ReadOnlyCollection<PatchDiagnostic>(
                (diagnostics ?? Enumerable.Empty<PatchDiagnostic>()).Where(value => value != null).ToList());
            Detail = detail ?? String.Empty;
        }

        internal string Id { get; private set; }
        internal PatchOutcome Outcome { get; private set; }
        internal IReadOnlyList<PatchDiagnostic> Diagnostics { get; private set; }
        internal string Detail { get; private set; }

        internal bool Succeeded
        {
            get { return Outcome == PatchOutcome.Applied || Outcome == PatchOutcome.AlreadyApplied; }
        }

        internal static BwtRaisedPriorityPatchAttempt FromResult(
            BwtRaisedPriorityPatchDefinition definition, BwtPatchResult result)
        {
            if (result == null)
            {
                return new BwtRaisedPriorityPatchAttempt(
                    definition.Id, PatchOutcome.Failed, null,
                    "The transpiler returned no BWT exact-profile result.");
            }

            return new BwtRaisedPriorityPatchAttempt(
                definition.Id, result.Outcome, result.Diagnostics, result.TargetMethodId);
        }

        internal static BwtRaisedPriorityPatchAttempt Failed(
            BwtRaisedPriorityPatchDefinition definition, string detail)
        {
            return new BwtRaisedPriorityPatchAttempt(
                definition.Id, PatchOutcome.Failed, null, detail);
        }

        public override string ToString()
        {
            string codes = String.Join(",", Diagnostics.Select(value => value.Code.ToString()).ToArray());
            return Id + "=" + Outcome + (codes.Length == 0 ? String.Empty : "[" + codes + "]") +
                (String.IsNullOrEmpty(Detail) ? String.Empty : ":" + Detail);
        }
    }

    internal sealed class BwtRaisedPriorityInstallReport
    {
        internal BwtRaisedPriorityInstallReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked, bool rollbackAttempted, bool rollbackSucceeded,
            IEnumerable<string> diagnostics, int featurePatchCount = 0,
            BwtRaisedPriorityFeatureGateState featureGateState =
                BwtRaisedPriorityFeatureGateState.Inactive)
        {
            State = state;
            Preflight = new ReadOnlyCollection<BwtRaisedPriorityPatchAttempt>(
                (preflight ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>()).ToList());
            Installation = new ReadOnlyCollection<BwtRaisedPriorityPatchAttempt>(
                (installation ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>()).ToList());
            PatchAllInvoked = patchAllInvoked;
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
            FeaturePatchCount = featurePatchCount;
            FeatureGateState = featureGateState;
            Diagnostics = new ReadOnlyCollection<string>(
                (diagnostics ?? Enumerable.Empty<string>()).Where(value => !String.IsNullOrEmpty(value)).ToList());
        }

        internal BwtRaisedPriorityInstallState State { get; private set; }
        internal IReadOnlyList<BwtRaisedPriorityPatchAttempt> Preflight { get; private set; }
        internal IReadOnlyList<BwtRaisedPriorityPatchAttempt> Installation { get; private set; }
        internal IReadOnlyList<string> Diagnostics { get; private set; }
        internal bool PatchAllInvoked { get; private set; }
        internal bool RollbackAttempted { get; private set; }
        internal bool RollbackSucceeded { get; private set; }
        internal int FeaturePatchCount { get; private set; }
        internal BwtRaisedPriorityFeatureGateState FeatureGateState { get; private set; }
        internal bool FeatureActive
        {
            get { return FeatureGateState != BwtRaisedPriorityFeatureGateState.Inactive; }
        }

        internal string Format()
        {
            string preflight = String.Join(";", Preflight.Select(value => value.ToString()).ToArray());
            string installation = String.Join(";", Installation.Select(value => value.ToString()).ToArray());
            string diagnostics = String.Join(";", Diagnostics.ToArray());
            return "state=" + State + ";featureGate=" + FeatureGateState + ";preflight=" + preflight + ";installation=" + installation +
                ";patchAll=" + PatchAllInvoked + ";rollback=" + RollbackAttempted + "/" +
                RollbackSucceeded + ";featurePatches=" + FeaturePatchCount +
                (diagnostics.Length == 0 ? String.Empty : ";diagnostics=" + diagnostics);
        }

    }

    /// <summary>
    /// Captures exact-profile outcomes while Harmony is installing BWT's raised-priority patches.
    /// Harmony invokes transpilers synchronously, so a thread-local session is sufficient and does
    /// not make the runtime patch surface depend on a global mutable result slot.
    /// </summary>
    internal sealed class BwtRaisedPriorityInstallationSession : IDisposable
    {
        [ThreadStatic]
        private static BwtRaisedPriorityInstallationSession _current;

        private readonly BwtRaisedPriorityInstallationSession _previous;
        private readonly Dictionary<string, BwtPatchResult> _results =
            new Dictionary<string, BwtPatchResult>(StringComparer.Ordinal);
        private bool _disposed;

        private BwtRaisedPriorityInstallationSession()
        {
            _previous = _current;
            _current = this;
        }

        internal static BwtRaisedPriorityInstallationSession Begin()
        {
            return new BwtRaisedPriorityInstallationSession();
        }

        internal static void Record(string id, BwtPatchResult result)
        {
            if (_current == null || String.IsNullOrEmpty(id))
                return;

            _current._results[id] = result;
        }

        internal bool TryGet(string id, out BwtPatchResult result)
        {
            return _results.TryGetValue(id, out result);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _current = _previous;
        }
    }

    /// <summary>
    /// Owns the only multi-method installation boundary for the BWT raised-priority feature.
    /// It deliberately stays BWT-local and uses the exact-profile engine through the six existing
    /// Harmony entrypoints. The generalized Fluent Transpiler project is not involved.
    /// </summary>
    internal sealed class BwtRaisedPriorityFeatureInstaller
    {
        internal const string HarmonyOwnerId = "Coolnether123.betterworktab";
        internal const string RaisedPriorityHarmonyOwnerId =
            "Coolnether123.betterworktab.raised-priority";
        internal const string RaisedPriorityPatchCategory =
            "BetterWorkTab.RaisedPriority";

        private const int AllTranspilers = Int32.MaxValue;
        private static readonly object InstallationSync = new object();

        private readonly IReadOnlyList<BwtRaisedPriorityPatchDefinition> _definitions;
        private readonly string _raisedPriorityOwnerId;
        private static volatile bool _isFeatureActive;
        private static BwtRaisedPriorityInstallReport _lastReport;
        private static BwtRaisedPriorityInstallationLedger _installationLedger;

        private enum PatchCollectionKind
        {
            Prefix,
            Postfix,
            Transpiler,
            Finalizer,
            InnerPrefix,
            InnerPostfix
        }

        /// <summary>
        /// Immutable identity and ordering snapshot for one Harmony patch record. Harmony 2.4.2
        /// exposes no public remove-by-record operation: MethodInfo removal is global to that
        /// original and removes every record using the method. The index and ordering metadata
        /// therefore remain part of the snapshot used to verify exact cleanup.
        /// </summary>
        private sealed class PatchRecordSnapshot
        {
            internal PatchRecordSnapshot(PatchCollectionKind kind, HarmonyLib.Patch patch)
            {
                if (patch == null)
                    throw new ArgumentNullException("patch");

                Kind = kind;
                Index = patch.index;
                Owner = patch.owner;
                Priority = patch.priority;
                Before = Copy(patch.before);
                After = Copy(patch.after);
                Debug = patch.debug;
                PatchMethod = patch.PatchMethod;
                InnerMethod = patch.innerMethod;
            }

            internal PatchCollectionKind Kind { get; private set; }
            internal int Index { get; private set; }
            internal string Owner { get; private set; }
            internal int Priority { get; private set; }
            internal string[] Before { get; private set; }
            internal string[] After { get; private set; }
            internal bool Debug { get; private set; }
            internal MethodInfo PatchMethod { get; private set; }
            internal InnerMethod InnerMethod { get; private set; }

            private static string[] Copy(string[] values)
            {
                return values == null ? null : (string[])values.Clone();
            }
        }

        private sealed class TargetPatchSnapshot
        {
            internal TargetPatchSnapshot(MethodBase target, IEnumerable<PatchRecordSnapshot> records)
            {
                Target = target;
                Records = new ReadOnlyCollection<PatchRecordSnapshot>(
                    (records ?? Enumerable.Empty<PatchRecordSnapshot>()).ToList());
            }

            internal MethodBase Target { get; private set; }
            internal IReadOnlyList<PatchRecordSnapshot> Records { get; private set; }
        }

        private sealed class RaisedPriorityPatchRecord
        {
            internal RaisedPriorityPatchRecord(
                BwtRaisedPriorityPatchDefinition definition, PatchRecordSnapshot record)
            {
                DefinitionId = definition.Id;
                Target = definition.Target;
                Transpiler = definition.Transpiler;
                Record = record;
            }

            internal string DefinitionId { get; private set; }
            internal MethodBase Target { get; private set; }
            internal MethodInfo Transpiler { get; private set; }
            internal PatchRecordSnapshot Record { get; private set; }
        }

        private sealed class BwtRaisedPriorityInstallationLedger
        {
            internal BwtRaisedPriorityInstallationLedger(
                string owner, IEnumerable<TargetPatchSnapshot> baseline,
                IEnumerable<RaisedPriorityPatchRecord> records)
            {
                Owner = owner;
                Baseline = new ReadOnlyCollection<TargetPatchSnapshot>(
                    (baseline ?? Enumerable.Empty<TargetPatchSnapshot>()).ToList());
                Records = new ReadOnlyCollection<RaisedPriorityPatchRecord>(
                    (records ?? Enumerable.Empty<RaisedPriorityPatchRecord>()).ToList());
            }

            internal string Owner { get; private set; }
            internal IReadOnlyList<TargetPatchSnapshot> Baseline { get; private set; }
            internal IReadOnlyList<RaisedPriorityPatchRecord> Records { get; private set; }

            internal bool Matches(
                IReadOnlyList<BwtRaisedPriorityPatchDefinition> definitions, string owner)
            {
                if (!String.Equals(Owner, owner, StringComparison.Ordinal) ||
                    definitions == null || definitions.Count != Records.Count)
                    return false;

                foreach (BwtRaisedPriorityPatchDefinition definition in definitions)
                {
                    RaisedPriorityPatchRecord record = Records.SingleOrDefault(value =>
                        String.Equals(value.DefinitionId, definition.Id, StringComparison.Ordinal));
                    if (record == null || !record.Target.Equals(definition.Target) ||
                        record.Transpiler != definition.Transpiler)
                        return false;
                }
                return true;
            }
        }

        private sealed class MethodPair : IEquatable<MethodPair>
        {
            internal MethodPair(MethodBase target, MethodInfo transpiler)
            {
                _target = target;
                _transpiler = transpiler;
            }

            private readonly MethodBase _target;
            private readonly MethodInfo _transpiler;

            public bool Equals(MethodPair other)
            {
                return other != null && _target.Equals(other._target) &&
                    _transpiler.Equals(other._transpiler);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as MethodPair);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_target == null ? 0 : _target.GetHashCode()) * 397 +
                        (_transpiler == null ? 0 : _transpiler.GetHashCode());
                }
            }
        }

        internal BwtRaisedPriorityFeatureInstaller(
            IEnumerable<BwtRaisedPriorityPatchDefinition> definitions,
            string raisedPriorityOwnerId = RaisedPriorityHarmonyOwnerId)
        {
            if (definitions == null)
                throw new ArgumentNullException("definitions");

            _definitions = new ReadOnlyCollection<BwtRaisedPriorityPatchDefinition>(
                definitions.ToList());
            _raisedPriorityOwnerId = raisedPriorityOwnerId;
        }

        internal static bool IsFeatureActive
        {
            get { return _isFeatureActive; }
        }

        internal static BwtRaisedPriorityInstallReport LastReport
        {
            get
            {
                lock (InstallationSync)
                    return _lastReport;
            }
        }

        internal static BwtRaisedPriorityInstallReport InstallProduction(Harmony harmony)
        {
            return new BwtRaisedPriorityFeatureInstaller(CreateProductionDefinitions())
                .Install(harmony, runPatchAll: true);
        }

        internal BwtRaisedPriorityInstallReport Install(Harmony harmony, bool runPatchAll)
        {
            lock (InstallationSync)
            {
                var diagnostics = new List<string>();
                bool patchAllInvoked = false;

                RefreshRecordedFeatureState();

                if (!TryValidatePlan(diagnostics))
                {
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, false, false, false, diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                if (harmony == null)
                {
                    diagnostics.Add("No Harmony instance was supplied.");
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, false, false, false, diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                Harmony raisedPriorityHarmony = new Harmony(_raisedPriorityOwnerId);
                bool cleanupAttempted = false;
                bool cleanupSucceeded = true;

                bool alreadyInstalled;
                if (!TryPrepareOwnership(out alreadyInstalled, diagnostics))
                {
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, false, false, false, diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                if (alreadyInstalled)
                {
                    List<BwtRaisedPriorityPatchAttempt> revalidation = Preflight();
                    if (revalidation.Count == _definitions.Count &&
                        revalidation.All(value => value.Succeeded) &&
                        revalidation.All(value => value.Outcome == PatchOutcome.AlreadyApplied))
                    {
                        _isFeatureActive = true;
                        return CreateReport(BwtRaisedPriorityInstallState.AlreadyInstalled,
                            revalidation, null, false, false, true, diagnostics,
                            _installationLedger.Records.Count);
                    }

                    diagnostics.Add(
                        "The existing raised-priority installation failed full-composition revalidation; " +
                        "it will be removed and evaluated again.");
                    cleanupAttempted = true;
                    cleanupSucceeded = TryRemoveInstallationLedger(
                        raisedPriorityHarmony, _installationLedger, diagnostics);
                    if (!cleanupSucceeded)
                    {
                        return CreateReport(BwtRaisedPriorityInstallState.RollbackFailed,
                            revalidation, null, false, true, false, diagnostics);
                    }

                    _isFeatureActive = false;
                    _installationLedger = null;
                }

                if (runPatchAll)
                {
                    patchAllInvoked = true;
                    if (!TryPatchUncategorized(harmony, diagnostics))
                    {
                        // Ownership preflight proved that no dedicated-owner record existed before
                        // PatchAll. If one appeared during PatchAll, it is not attributable to a
                        // feature installation and must be preserved fail-closed.
                        if (HasAnyOwnedTranspilers())
                        {
                            diagnostics.Add(
                                "The dedicated raised-priority owner changed during uncategorized " +
                                "PatchAll; cleanup cannot attribute those records safely.");
                            cleanupAttempted = true;
                            cleanupSucceeded = false;
                        }

                        return CreateReport(
                            cleanupSucceeded
                                ? BwtRaisedPriorityInstallState.RolledBack
                                : BwtRaisedPriorityInstallState.RollbackFailed,
                            null, null, patchAllInvoked, cleanupAttempted,
                            cleanupSucceeded, diagnostics);
                    }
                }

                List<TargetPatchSnapshot> installationBaseline;
                if (!TryCaptureSnapshots(out installationBaseline, diagnostics))
                {
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, patchAllInvoked, cleanupAttempted, cleanupSucceeded, diagnostics);
                }

                if (HasAnyOwnedTranspilers(installationBaseline))
                {
                    diagnostics.Add(
                        "The dedicated raised-priority owner changed before manual installation; " +
                        "the feature will remain inactive.");
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, patchAllInvoked, cleanupAttempted, cleanupSucceeded, diagnostics);
                }

                List<BwtRaisedPriorityPatchAttempt> preflight = Preflight();
                if (preflight.Count != _definitions.Count ||
                    preflight.Any(value => !value.Succeeded))
                {
                    diagnostics.Add("At least one required raised-priority transpiler failed preflight " +
                        "against the fully composed IL.");
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        preflight, null, patchAllInvoked,
                        cleanupAttempted, cleanupSucceeded, diagnostics);
                }

                List<BwtRaisedPriorityPatchAttempt> installation;
                var attemptedDefinitionIds = new List<string>();
                using (BwtRaisedPriorityInstallationSession session =
                    BwtRaisedPriorityInstallationSession.Begin())
                {
                    try
                    {
                        InstallDefinitionsIndividually(
                            raisedPriorityHarmony, attemptedDefinitionIds);
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add("Harmony installation threw " + exception.GetType().FullName + ": " +
                            Trim(exception.Message));
                    }

                    installation = CollectInstallationAttempts(session);
                }

                BwtRaisedPriorityInstallationLedger installationLedger;
                bool ledgerSafe = TryCaptureInstallationLedger(
                    installationBaseline, attemptedDefinitionIds,
                    out installationLedger, diagnostics);
                bool installed = ledgerSafe &&
                    installation.Count == _definitions.Count &&
                    installation.All(value => value.Succeeded) &&
                    installationLedger != null &&
                    installationLedger.Records.Count == _definitions.Count;
                if (installed)
                {
                    _installationLedger = installationLedger;
                    _isFeatureActive = true;
                    return CreateReport(BwtRaisedPriorityInstallState.Installed,
                        preflight, installation, patchAllInvoked, cleanupAttempted, true,
                        diagnostics, installationLedger.Records.Count);
                }

                diagnostics.Add("The six required transpilers were not installed as one complete feature.");
                bool rollbackSucceeded = ledgerSafe && installationLedger != null &&
                    TryRemoveInstallationLedger(raisedPriorityHarmony, installationLedger, diagnostics);
                BwtRaisedPriorityInstallState state = rollbackSucceeded
                    ? BwtRaisedPriorityInstallState.RolledBack
                    : BwtRaisedPriorityInstallState.RollbackFailed;

                if (rollbackSucceeded)
                    _installationLedger = null;
                _isFeatureActive = false;
                return CreateReport(state, preflight, installation, patchAllInvoked,
                    true, rollbackSucceeded, diagnostics,
                    installationLedger == null ? 0 : installationLedger.Records.Count);
            }
        }

        private bool TryPatchUncategorized(Harmony harmony, IList<string> diagnostics)
        {
            try
            {
                harmony.PatchAllUncategorized(
                    typeof(BwtRaisedPriorityFeatureInstaller).Assembly);
                return true;
            }
            catch (Exception exception)
            {
                diagnostics.Add("Harmony uncategorized installation threw " +
                    exception.GetType().FullName + ": " + Trim(exception.Message));
                return false;
            }
        }

        private List<BwtRaisedPriorityPatchAttempt> Preflight()
        {
            var results = new List<BwtRaisedPriorityPatchAttempt>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                try
                {
                    if (definition.Target == null || definition.Transpiler == null)
                    {
                        results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                            definition, "The target or transpiler method could not be resolved."));
                        continue;
                    }

                    ILGenerator generator;
                    List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(
                        definition.Target, out generator, AllTranspilers);
                    if (generator == null)
                    {
                        generator = new DynamicMethod(
                            "bwt-raised-priority-preflight", typeof(void), Type.EmptyTypes)
                            .GetILGenerator();
                    }
                    BwtPatchResult result = InvokeTranspiler(definition, current, generator);
                    results.Add(BwtRaisedPriorityPatchAttempt.FromResult(definition, result));
                }
                catch (Exception exception)
                {
                    results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition, exception.GetType().FullName + ": " + Trim(exception.Message)));
                }
            }
            return results;
        }

        private void InstallDefinitionsIndividually(
            Harmony harmony, IList<string> attemptedDefinitionIds)
        {
            // Harmony recomputes the live, fully composed instruction stream for each patch.
            // Reusing the preflight list would hide a foreign change between the two phases;
            // the installation session records that live invocation and rolls back on failure.
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (definition.Target == null || definition.Transpiler == null)
                    throw new InvalidOperationException("A raised-priority patch definition is incomplete.");

                attemptedDefinitionIds.Add(definition.Id);

                harmony.CreateProcessor(definition.Target)
                    .AddTranspiler(new HarmonyMethod(definition.Transpiler))
                    .Patch();
            }
        }

        private List<BwtRaisedPriorityPatchAttempt> CollectInstallationAttempts(
            BwtRaisedPriorityInstallationSession session)
        {
            var results = new List<BwtRaisedPriorityPatchAttempt>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                BwtPatchResult result;
                if (session.TryGet(definition.Id, out result))
                    results.Add(BwtRaisedPriorityPatchAttempt.FromResult(definition, result));
                else
                    results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition, "Harmony did not report an outcome for this required transpiler."));
            }
            return results;
        }

        private bool HasAnyOwnedTranspilers()
        {
            List<TargetPatchSnapshot> snapshots;
            return TryCaptureSnapshots(out snapshots, null) &&
                HasAnyOwnedTranspilers(snapshots);
        }

        private bool HasAnyOwnedTranspilers(IEnumerable<TargetPatchSnapshot> snapshots)
        {
            return (snapshots ?? Enumerable.Empty<TargetPatchSnapshot>())
                .SelectMany(value => value.Records)
                .Any(value => String.Equals(value.Owner, _raisedPriorityOwnerId,
                    StringComparison.Ordinal));
        }

        private static void RefreshRecordedFeatureState()
        {
            if (_installationLedger == null)
            {
                _isFeatureActive = false;
                return;
            }

            int present = 0;
            try
            {
                foreach (RaisedPriorityPatchRecord record in _installationLedger.Records)
                {
                    List<PatchRecordSnapshot> records = GetPatchRecords(record.Target);
                    if (records.Count(value => SamePatchRecord(value, record.Record)) == 1)
                        present++;
                }
            }
            catch
            {
                _isFeatureActive = false;
                return;
            }

            if (present == 0)
            {
                _installationLedger = null;
                _isFeatureActive = false;
            }
            else if (present != _installationLedger.Records.Count)
            {
                _isFeatureActive = false;
            }
        }

        private bool TryValidatePlan(IList<string> diagnostics)
        {
            bool valid = true;
            if (_definitions == null || _definitions.Count != 6)
            {
                AddDiagnostic(diagnostics,
                    "The raised-priority plan must contain exactly six definitions.");
                valid = false;
            }

            if (String.IsNullOrWhiteSpace(_raisedPriorityOwnerId))
            {
                AddDiagnostic(diagnostics, "The dedicated raised-priority owner is missing.");
                valid = false;
            }
            else if (String.Equals(_raisedPriorityOwnerId, HarmonyOwnerId,
                StringComparison.Ordinal))
            {
                AddDiagnostic(diagnostics,
                    "The dedicated raised-priority owner must differ from the normal BWT owner.");
                valid = false;
            }

            if (_definitions == null)
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<MethodBase>();
            var pairs = new HashSet<MethodPair>();
            var references = new List<BwtRaisedPriorityPatchDefinition>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (definition == null)
                {
                    AddDiagnostic(diagnostics, "The raised-priority plan contains a null definition.");
                    valid = false;
                    continue;
                }

                if (references.Any(value => object.ReferenceEquals(value, definition)))
                {
                    AddDiagnostic(diagnostics,
                        "The raised-priority plan contains a duplicate definition object.");
                    valid = false;
                }
                references.Add(definition);

                if (String.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority definition IDs must be non-empty and unique.");
                    valid = false;
                }

                if (definition.Target == null)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority definition " + (definition.Id ?? "<missing-id>") +
                        " has no target.");
                    valid = false;
                }
                else if (!targets.Add(definition.Target))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority targets must be unique; duplicate target in " +
                        (definition.Id ?? "<missing-id>") + ".");
                    valid = false;
                }

                if (definition.Transpiler == null)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority definition " + (definition.Id ?? "<missing-id>") +
                        " has no transpiler.");
                    valid = false;
                }
                else if (definition.Target != null &&
                    !pairs.Add(new MethodPair(definition.Target, definition.Transpiler)))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority target/transpiler pairs must be unique; duplicate pair in " +
                        (definition.Id ?? "<missing-id>") + ".");
                    valid = false;
                }

                string categoryError;
                if (definition.Transpiler != null &&
                    !TryValidateCategoryIsolation(definition.Transpiler, out categoryError))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority definition " + (definition.Id ?? "<missing-id>") +
                        " category isolation is invalid: " + categoryError + ".");
                    valid = false;
                }
            }

            return valid;
        }

        private static bool TryValidateCategoryIsolation(
            MethodInfo transpiler, out string error)
        {
            error = null;
            if (!transpiler.IsStatic)
            {
                error = "the transpiler method must be static";
                return false;
            }

            if (!typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(transpiler.ReturnType))
            {
                error = "the transpiler must return IEnumerable<CodeInstruction>";
                return false;
            }

            if (!transpiler.GetCustomAttributes(typeof(HarmonyTranspiler), false).Any())
            {
                error = "the method is not marked as a Harmony transpiler";
                return false;
            }

            Type declaringType = transpiler.DeclaringType;
            CustomAttributeData[] categories = declaringType == null
                ? new CustomAttributeData[0]
                : CustomAttributeData.GetCustomAttributes(declaringType)
                    .Where(value => value.AttributeType == typeof(HarmonyPatchCategory))
                    .ToArray();
            if (categories.Length != 1 || categories[0].ConstructorArguments.Count != 1 ||
                !String.Equals(
                    categories[0].ConstructorArguments[0].Value as string,
                    RaisedPriorityPatchCategory, StringComparison.Ordinal))
            {
                error = "the declaring patch type must have exactly one raised-priority category";
                return false;
            }

            return true;
        }

        private bool TryPrepareOwnership(
            out bool alreadyInstalled, IList<string> diagnostics)
        {
            alreadyInstalled = false;
            List<TargetPatchSnapshot> snapshots;
            if (!TryCaptureSnapshots(out snapshots, diagnostics))
                return false;
            List<TargetPatchSnapshot> capturedSnapshots = snapshots;

            if (_installationLedger != null)
            {
                bool anyLedgerRecord = capturedSnapshots.Any(snapshot => snapshot.Records.Any(record =>
                    _installationLedger.Records.Any(ledgerRecord =>
                        ledgerRecord.Target.Equals(snapshot.Target) &&
                        SamePatchRecord(ledgerRecord.Record, record))));
                bool allLedgerRecords = _installationLedger.Records.Count == 6 &&
                    _installationLedger.Records.All(ledgerRecord => capturedSnapshots.Any(snapshot =>
                        ledgerRecord.Target.Equals(snapshot.Target) &&
                        snapshot.Records.Count(record => SamePatchRecord(
                            ledgerRecord.Record, record)) == 1));
                if (anyLedgerRecord && !allLedgerRecords)
                {
                    AddDiagnostic(diagnostics,
                        "The previous raised-priority installation is only partially present; " +
                        "ownership cannot be recovered safely.");
                    return false;
                }

                if (!anyLedgerRecord)
                    _installationLedger = null;
                else if (!_installationLedger.Matches(_definitions, _raisedPriorityOwnerId))
                {
                    AddDiagnostic(diagnostics,
                        "A different raised-priority plan still owns the dedicated Harmony records.");
                    return false;
                }
            }

            bool hasExpectedRecord = false;
            bool hasUnexpectedOwnerRecord = false;
            bool hasCompleteLedger = _installationLedger != null &&
                _installationLedger.Records.Count == _definitions.Count &&
                _definitions.All(definition =>
                {
                    TargetPatchSnapshot snapshot = capturedSnapshots.Single(value =>
                        value.Target.Equals(definition.Target));
                    return FindFeatureRecords(snapshot, definition).Count == 1;
                });
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                TargetPatchSnapshot snapshot = capturedSnapshots.Single(value =>
                    value.Target.Equals(definition.Target));
                List<PatchRecordSnapshot> expected = FindFeatureRecords(snapshot, definition);
                if (expected.Count > 0)
                    hasExpectedRecord = true;
                if (expected.Count > 1)
                {
                    AddDiagnostic(diagnostics,
                        "Multiple dedicated-owner records use the same target and transpiler for " +
                        definition.Id + "; cleanup is ambiguous.");
                    hasUnexpectedOwnerRecord = true;
                }

                if (snapshot.Records.Any(record =>
                    String.Equals(record.Owner, _raisedPriorityOwnerId,
                        StringComparison.Ordinal) &&
                    !IsExpectedFeatureRecord(record, definition) &&
                    !IsSafeLateOwnerContamination(snapshot, definition,
                        hasCompleteLedger)))
                {
                    AddDiagnostic(diagnostics,
                        "The dedicated raised-priority owner has an unexpected patch on " +
                        definition.Id + "; installation is rejected before mutation.");
                    hasUnexpectedOwnerRecord = true;
                }
            }

            if (hasUnexpectedOwnerRecord)
                return false;

            if (!hasExpectedRecord)
                return true;

            if (_installationLedger != null &&
                _installationLedger.Records.Count == _definitions.Count &&
                _definitions.All(definition => FindFeatureRecords(
                    capturedSnapshots.Single(value => value.Target.Equals(definition.Target)), definition).Count == 1))
            {
                alreadyInstalled = true;
                return true;
            }

            AddDiagnostic(diagnostics,
                "Dedicated-owner records are present without a complete coordinator ledger; " +
                "installation is rejected before mutation.");
            return false;
        }

        private bool IsSafeLateOwnerContamination(
            TargetPatchSnapshot snapshot, BwtRaisedPriorityPatchDefinition definition,
            bool hasCompleteLedger)
        {
            if (!hasCompleteLedger || definition == null || definition.Transpiler == null)
                return false;

            // A late same-owner patch with a different method is safe only while the
            // feature method remains unique on this target. Cleanup can then remove the
            // feature method exactly and preserve the late record. A same-method collision
            // is deliberately fail-closed because Harmony has no remove-by-record API.
            return snapshot.Records.Count(record => record.PatchMethod == definition.Transpiler) == 1;
        }

        private bool TryCaptureSnapshots(
            out List<TargetPatchSnapshot> snapshots, IList<string> diagnostics)
        {
            snapshots = new List<TargetPatchSnapshot>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions ??
                Enumerable.Empty<BwtRaisedPriorityPatchDefinition>())
            {
                if (definition == null || definition.Target == null)
                    continue;
                try
                {
                    snapshots.Add(new TargetPatchSnapshot(
                        definition.Target, GetPatchRecords(definition.Target)));
                }
                catch (Exception exception)
                {
                    AddDiagnostic(diagnostics,
                        "Harmony patch snapshot failed for " + definition.Id + ": " +
                        exception.GetType().FullName + ": " + Trim(exception.Message));
                    return false;
                }
            }
            return snapshots.Count == _definitions.Count;
        }

        private static List<PatchRecordSnapshot> GetPatchRecords(MethodBase target)
        {
            var result = new List<PatchRecordSnapshot>();
            HarmonyLib.Patches patches = HarmonyLib.Harmony.GetPatchInfo(target);
            if (patches == null)
                return result;

            AddPatchRecords(result, PatchCollectionKind.Prefix, patches.Prefixes);
            AddPatchRecords(result, PatchCollectionKind.Postfix, patches.Postfixes);
            AddPatchRecords(result, PatchCollectionKind.Transpiler, patches.Transpilers);
            AddPatchRecords(result, PatchCollectionKind.Finalizer, patches.Finalizers);
            AddPatchRecords(result, PatchCollectionKind.InnerPrefix, patches.InnerPrefixes);
            AddPatchRecords(result, PatchCollectionKind.InnerPostfix, patches.InnerPostfixes);
            return result;
        }

        private static void AddPatchRecords(
            IList<PatchRecordSnapshot> target, PatchCollectionKind kind,
            IEnumerable<HarmonyLib.Patch> patches)
        {
            foreach (HarmonyLib.Patch patch in patches ?? Enumerable.Empty<HarmonyLib.Patch>())
                target.Add(new PatchRecordSnapshot(kind, patch));
        }

        private List<PatchRecordSnapshot> FindFeatureRecords(
            TargetPatchSnapshot snapshot, BwtRaisedPriorityPatchDefinition definition)
        {
            return snapshot.Records.Where(record => IsExpectedFeatureRecord(record, definition)).ToList();
        }

        private bool IsExpectedFeatureRecord(
            PatchRecordSnapshot record, BwtRaisedPriorityPatchDefinition definition)
        {
            return record != null && record.Kind == PatchCollectionKind.Transpiler &&
                String.Equals(record.Owner, _raisedPriorityOwnerId, StringComparison.Ordinal) &&
                record.PatchMethod == definition.Transpiler;
        }

        private bool TryCaptureInstallationLedger(
            IEnumerable<TargetPatchSnapshot> baseline,
            IEnumerable<string> attemptedDefinitionIds,
            out BwtRaisedPriorityInstallationLedger ledger,
            IList<string> diagnostics)
        {
            ledger = null;
            List<TargetPatchSnapshot> current;
            if (!TryCaptureSnapshots(out current, diagnostics))
                return false;

            var attempted = new HashSet<string>(
                attemptedDefinitionIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var records = new List<RaisedPriorityPatchRecord>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                TargetPatchSnapshot baselineSnapshot;
                TargetPatchSnapshot snapshot;
                if (!TryFindSnapshot(baseline, definition.Target, out baselineSnapshot) ||
                    !TryFindSnapshot(current, definition.Target, out snapshot))
                {
                    AddDiagnostic(diagnostics,
                        "Installation lost the snapshot for " + definition.Id + ".");
                    return false;
                }

                if (FindFeatureRecords(baselineSnapshot, definition).Count != 0)
                {
                    AddDiagnostic(diagnostics,
                        "The installation baseline already contained the feature record for " +
                        definition.Id + ".");
                    return false;
                }

                List<PatchRecordSnapshot> matches = FindFeatureRecords(snapshot, definition);
                if (matches.Count > 1)
                {
                    AddDiagnostic(diagnostics,
                        "Installation produced duplicate dedicated-owner records for " +
                        definition.Id + "; exact cleanup is not guaranteed.");
                    return false;
                }

                if (attempted.Contains(definition.Id))
                {
                    if (matches.Count == 1)
                        records.Add(new RaisedPriorityPatchRecord(definition, matches[0]));
                }
                else if (matches.Count != 0)
                {
                    AddDiagnostic(diagnostics,
                        "A dedicated-owner feature record appeared without an installation attempt for " +
                        definition.Id + ".");
                    return false;
                }
            }

            if (records.Count == 0)
                return false;

            ledger = new BwtRaisedPriorityInstallationLedger(
                _raisedPriorityOwnerId, baseline, records);
            return true;
        }

        private bool TryRemoveInstallationLedger(
            Harmony harmony, BwtRaisedPriorityInstallationLedger ledger,
            IList<string> diagnostics)
        {
            if (ledger == null || ledger.Records.Count == 0)
            {
                AddDiagnostic(diagnostics, "No exact raised-priority installation ledger is available.");
                return false;
            }

            List<TargetPatchSnapshot> current;
            if (!TryCaptureSnapshots(out current, diagnostics))
                return false;

            var preserved = current.ToDictionary(
                snapshot => snapshot.Target,
                snapshot => snapshot.Records.ToList());
            var ownerScopedRemoval = new Dictionary<RaisedPriorityPatchRecord, bool>();
            foreach (RaisedPriorityPatchRecord ledgerRecord in ledger.Records)
            {
                TargetPatchSnapshot snapshot;
                if (!TryFindSnapshot(current, ledgerRecord.Target, out snapshot))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup lost target " + ledgerRecord.DefinitionId + ".");
                    return false;
                }

                List<PatchRecordSnapshot> exact = snapshot.Records.Where(record =>
                    SamePatchRecord(record, ledgerRecord.Record)).ToList();
                if (exact.Count != 1)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup could not identify exactly one record for " +
                        ledgerRecord.DefinitionId + ".");
                    return false;
                }

                int sameMethodCount = snapshot.Records.Count(record =>
                    record.PatchMethod == ledgerRecord.Transpiler);
                bool canRemoveByOwner = snapshot.Records.Count(record =>
                    record.Kind == PatchCollectionKind.Transpiler &&
                    String.Equals(record.Owner, ledger.Owner, StringComparison.Ordinal)) == 1 &&
                    snapshot.Records.Any(record =>
                        record.Kind == PatchCollectionKind.Transpiler &&
                        String.Equals(record.Owner, ledger.Owner, StringComparison.Ordinal) &&
                        SamePatchRecord(record, ledgerRecord.Record));
                if (sameMethodCount != 1 && !canRemoveByOwner)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup found a MethodInfo collision for " +
                        ledgerRecord.DefinitionId +
                        "; no exact owner-scoped fallback is safe.");
                    return false;
                }

                // Harmony's MethodInfo overload is exact only when the method occurs once
                // on this target. If a foreign owner reused that method, the guarded
                // owner/type fallback removes the one ledger record while preserving the
                // foreign record. It is never used without the snapshot identity checks
                // above and the post-cleanup multiset verification below.
                ownerScopedRemoval[ledgerRecord] = sameMethodCount != 1;

                List<PatchRecordSnapshot> remaining = preserved[ledgerRecord.Target];
                int removeIndex = remaining.FindIndex(record =>
                    SamePatchRecord(record, ledgerRecord.Record));
                if (removeIndex < 0)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup could not snapshot the record for " +
                        ledgerRecord.DefinitionId + ".");
                    return false;
                }
                remaining.RemoveAt(removeIndex);
            }

            for (int index = ledger.Records.Count - 1; index >= 0; index--)
            {
                RaisedPriorityPatchRecord ledgerRecord = ledger.Records[index];
                try
                {
                    if (ownerScopedRemoval[ledgerRecord])
                    {
                        harmony.Unpatch(ledgerRecord.Target, HarmonyPatchType.Transpiler,
                            ledger.Owner);
                    }
                    else
                    {
                        harmony.Unpatch(ledgerRecord.Target, ledgerRecord.Transpiler);
                    }
                }
                catch (Exception exception)
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup failed for " + ledgerRecord.DefinitionId + ": " +
                        exception.GetType().FullName + ": " + Trim(exception.Message));
                    return false;
                }
            }

            List<TargetPatchSnapshot> after;
            if (!TryCaptureSnapshots(out after, diagnostics))
                return false;
            foreach (KeyValuePair<MethodBase, List<PatchRecordSnapshot>> expected in preserved)
            {
                TargetPatchSnapshot actual;
                if (!TryFindSnapshot(after, expected.Key, out actual) ||
                    !SamePatchMultiset(actual.Records, expected.Value))
                {
                    AddDiagnostic(diagnostics,
                        "Raised-priority cleanup changed an unowned Harmony record or ordering for " +
                        DescribeTarget(expected.Key) + ".");
                    return false;
                }
            }

            return true;
        }

        private static bool SamePatchMultiset(
            IEnumerable<PatchRecordSnapshot> left,
            IEnumerable<PatchRecordSnapshot> right)
        {
            List<PatchRecordSnapshot> remaining = (right ??
                Enumerable.Empty<PatchRecordSnapshot>()).ToList();
            foreach (PatchRecordSnapshot record in left ?? Enumerable.Empty<PatchRecordSnapshot>())
            {
                int index = remaining.FindIndex(value => SamePatchRecord(record, value));
                if (index < 0)
                    return false;
                remaining.RemoveAt(index);
            }
            return remaining.Count == 0;
        }

        private static bool SamePatchRecord(
            PatchRecordSnapshot left, PatchRecordSnapshot right)
        {
            return left != null && right != null && left.Kind == right.Kind &&
                left.Index == right.Index &&
                String.Equals(left.Owner, right.Owner, StringComparison.Ordinal) &&
                left.Priority == right.Priority && left.Debug == right.Debug &&
                left.PatchMethod == right.PatchMethod &&
                SameOrdering(left.Before, right.Before) &&
                SameOrdering(left.After, right.After) &&
                SameInnerMethod(left.InnerMethod, right.InnerMethod);
        }

        private static bool SameInnerMethod(InnerMethod left, InnerMethod right)
        {
            if (object.ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Method != right.Method)
                return false;
            return SameIndexes(left.positions, right.positions);
        }

        private static bool SameIndexes(int[] left, int[] right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            if (left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index])
                    return false;
            return true;
        }

        private static bool SameOrdering(string[] left, string[] right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            if (left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
                if (!String.Equals(left[index], right[index], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool TryFindSnapshot(
            IEnumerable<TargetPatchSnapshot> snapshots, MethodBase target,
            out TargetPatchSnapshot snapshot)
        {
            snapshot = (snapshots ?? Enumerable.Empty<TargetPatchSnapshot>())
                .FirstOrDefault(value => value.Target.Equals(target));
            return snapshot != null;
        }

        private static string DescribeTarget(MethodBase target)
        {
            return target == null ? "<missing-target>" :
                (target.DeclaringType == null ? "<missing-type>" : target.DeclaringType.FullName) +
                "." + target.Name;
        }

        private static void AddDiagnostic(IList<string> diagnostics, string message)
        {
            if (diagnostics != null && !String.IsNullOrEmpty(message))
                diagnostics.Add(message);
        }

        private BwtRaisedPriorityInstallReport CreateReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked, bool rollbackAttempted, bool rollbackSucceeded,
            IEnumerable<string> diagnostics, int featurePatchCount = 0,
            BwtRaisedPriorityFeatureGateState? featureGateState = null)
        {
            BwtRaisedPriorityInstallReport report = new BwtRaisedPriorityInstallReport(
                state,
                preflight ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                installation ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                patchAllInvoked, rollbackAttempted, rollbackSucceeded, diagnostics,
                featurePatchCount,
                featureGateState ?? ResolveFeatureGateState(state));
            _lastReport = report;
            return report;
        }

        private static BwtRaisedPriorityFeatureGateState ResolveFeatureGateState(
            BwtRaisedPriorityInstallState state)
        {
            return state == BwtRaisedPriorityInstallState.Installed ||
                state == BwtRaisedPriorityInstallState.AlreadyInstalled
                ? BwtRaisedPriorityFeatureGateState.Active
                : BwtRaisedPriorityFeatureGateState.Inactive;
        }

        private static BwtRaisedPriorityFeatureGateState CurrentFeatureGateStateAfterRejectedReconfiguration()
        {
            return _isFeatureActive && _installationLedger != null
                ? BwtRaisedPriorityFeatureGateState.PreservedAfterRejectedReconfiguration
                : BwtRaisedPriorityFeatureGateState.Inactive;
        }

        private static BwtPatchResult InvokeTranspiler(
            BwtRaisedPriorityPatchDefinition definition,
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            ParameterInfo[] parameters = definition.Transpiler.GetParameters();
            var arguments = new object[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                if (index == 0)
                    arguments[index] = instructions;
                else if (parameterType == typeof(ILGenerator))
                    arguments[index] = generator;
                else if (typeof(MethodBase).IsAssignableFrom(parameterType))
                    arguments[index] = definition.Target;
                else
                    throw new InvalidOperationException(
                        "Unsupported BWT transpiler parameter " + parameterType.FullName + ".");
            }

            object value = definition.Transpiler.Invoke(null, arguments);
            return value as BwtPatchResult;
        }

        private static IReadOnlyList<BwtRaisedPriorityPatchDefinition> CreateProductionDefinitions()
        {
            return new ReadOnlyCollection<BwtRaisedPriorityPatchDefinition>(new[]
            {
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.TipForPawnWorker,
                    AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker)),
                    AccessTools.Method(typeof(Patch_WidgetsWork_TipForPawnWorker), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.DrawWorkBoxFor,
                    AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor)),
                    AccessTools.Method(typeof(Patch_WidgetsWork_DrawWorkBoxFor), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.HeaderClicked,
                    AccessTools.Method(typeof(PawnColumnWorker_WorkPriority),
                        nameof(PawnColumnWorker_WorkPriority.HeaderClicked)),
                    AccessTools.Method(typeof(Patch_PawnColumnWorker_WorkPriority_HeaderClicked), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.SetPriority,
                    AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority)),
                    AccessTools.Method(typeof(Patch_Pawn_WorkSettings_SetPriority), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.DoHeader,
                    AccessTools.Method(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader),
                        new[] { typeof(Rect), typeof(PawnTable) }),
                    AccessTools.Method(typeof(Better_Work_Tab.UI.Headers.Patch_PawnColumnWorker_DoHeader_DisableHighlight),
                        "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.LabelDoCell,
                    AccessTools.Method(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell)),
                    AccessTools.Method(
                        typeof(Better_Work_Tab.Patches.Patch_PawnColumnWorker_Label_DoCell_Transpiler),
                        "Transpiler"))
            });
        }

        private static string Trim(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            return value.Length <= 240 ? value : value.Substring(0, 240);
        }
    }
}
