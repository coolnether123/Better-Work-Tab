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
        internal BwtRaisedPriorityPatchDefinition(
            string id,
            MethodBase target,
            MethodInfo transpiler)
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
            string id,
            PatchOutcome outcome,
            IEnumerable<PatchDiagnostic> diagnostics,
            string detail)
        {
            Id = id;
            Outcome = outcome;
            Diagnostics = new ReadOnlyCollection<PatchDiagnostic>(
                (diagnostics ?? Enumerable.Empty<PatchDiagnostic>())
                    .Where(value => value != null)
                    .ToList());
            Detail = detail ?? String.Empty;
        }

        internal string Id { get; private set; }
        internal PatchOutcome Outcome { get; private set; }
        internal IReadOnlyList<PatchDiagnostic> Diagnostics { get; private set; }
        internal string Detail { get; private set; }

        internal bool Succeeded
        {
            get
            {
                return Outcome == PatchOutcome.Applied ||
                    Outcome == PatchOutcome.AlreadyApplied;
            }
        }

        internal static BwtRaisedPriorityPatchAttempt FromResult(
            BwtRaisedPriorityPatchDefinition definition,
            BwtPatchResult result)
        {
            return result == null
                ? Failed(definition, "The transpiler returned no BWT exact-profile result.")
                : new BwtRaisedPriorityPatchAttempt(
                    definition.Id,
                    result.Outcome,
                    result.Diagnostics,
                    result.TargetMethodId);
        }

        internal static BwtRaisedPriorityPatchAttempt Failed(
            BwtRaisedPriorityPatchDefinition definition,
            string detail)
        {
            return new BwtRaisedPriorityPatchAttempt(
                definition.Id,
                PatchOutcome.Failed,
                null,
                detail);
        }

        public override string ToString()
        {
            string codes = String.Join(",", Diagnostics.Select(value => value.Code.ToString()).ToArray());
            return Id + "=" + Outcome +
                (codes.Length == 0 ? String.Empty : "[" + codes + "]") +
                (String.IsNullOrEmpty(Detail) ? String.Empty : ":" + Detail);
        }
    }

    internal sealed class BwtRaisedPriorityInstallReport
    {
        internal BwtRaisedPriorityInstallReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked,
            bool rollbackAttempted,
            bool rollbackSucceeded,
            IEnumerable<string> diagnostics,
            int featurePatchCount = 0,
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
                (diagnostics ?? Enumerable.Empty<string>())
                    .Where(value => !String.IsNullOrEmpty(value))
                    .ToList());
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
            return "state=" + State +
                ";featureGate=" + FeatureGateState +
                ";preflight=" + preflight +
                ";installation=" + installation +
                ";patchAll=" + PatchAllInvoked +
                ";rollback=" + RollbackAttempted + "/" + RollbackSucceeded +
                ";featurePatches=" + FeaturePatchCount +
                (diagnostics.Length == 0 ? String.Empty : ";diagnostics=" + diagnostics);
        }
    }

    /// <summary>
    /// Installs BWT's six raised-priority transpilers as one owner-isolated feature.
    /// The dedicated owner is all-or-nothing: unexpected records under that owner reject an
    /// install, and a failed install removes all records under that owner.
    /// </summary>
    internal sealed class BwtRaisedPriorityFeatureInstaller
    {
        internal const string HarmonyOwnerId = "Coolnether123.betterworktab";
        internal const string RaisedPriorityHarmonyOwnerId =
            "Coolnether123.betterworktab.raised-priority";
        internal const string RaisedPriorityPatchCategory = "BetterWorkTab.RaisedPriority";

        private const int AllTranspilers = Int32.MaxValue;
        private static readonly object InstallationSync = new object();

        private readonly IReadOnlyList<BwtRaisedPriorityPatchDefinition> _definitions;
        private readonly string _raisedPriorityOwnerId;

        private static volatile bool _isFeatureActive;
        private static IReadOnlyList<BwtRaisedPriorityPatchDefinition> _activeDefinitions;
        private static string _activeOwnerId;
        private static BwtRaisedPriorityInstallReport _lastReport;

        private sealed class MethodPair : IEquatable<MethodPair>
        {
            private readonly MethodBase _target;
            private readonly MethodInfo _transpiler;

            internal MethodPair(MethodBase target, MethodInfo transpiler)
            {
                _target = target;
                _transpiler = transpiler;
            }

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
                RefreshFeatureState();

                if (!TryValidatePlan(diagnostics))
                {
                    return CreateReport(
                        BwtRaisedPriorityInstallState.PreflightRejected,
                        null,
                        null,
                        false,
                        false,
                        false,
                        diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                if (harmony == null)
                {
                    diagnostics.Add("No Harmony instance was supplied.");
                    return CreateReport(
                        BwtRaisedPriorityInstallState.PreflightRejected,
                        null,
                        null,
                        false,
                        false,
                        false,
                        diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                bool alreadyInstalled;
                if (!TryInspectDedicatedOwner(out alreadyInstalled, diagnostics))
                {
                    return CreateReport(
                        BwtRaisedPriorityInstallState.PreflightRejected,
                        null,
                        null,
                        false,
                        false,
                        false,
                        diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                // Validate the whole plan before this coordinator changes Harmony state.
                List<BwtRaisedPriorityPatchAttempt> preflight = Preflight();
                if (preflight.Count != _definitions.Count || preflight.Any(value => !value.Succeeded))
                {
                    diagnostics.Add("At least one required raised-priority transpiler failed preflight.");
                    return CreateReport(
                        BwtRaisedPriorityInstallState.PreflightRejected,
                        preflight,
                        null,
                        false,
                        false,
                        false,
                        diagnostics,
                        featureGateState: CurrentFeatureGateStateAfterRejectedReconfiguration());
                }

                if (alreadyInstalled)
                {
                    ActivateFeature();
                    return CreateReport(
                        BwtRaisedPriorityInstallState.AlreadyInstalled,
                        preflight,
                        null,
                        false,
                        false,
                        true,
                        diagnostics,
                        _definitions.Count);
                }

                Harmony raisedPriorityHarmony = new Harmony(_raisedPriorityOwnerId);
                if (runPatchAll)
                {
                    patchAllInvoked = true;
                    if (!TryPatchUncategorized(harmony, diagnostics))
                    {
                        return RollBackFailedInstallation(
                            raisedPriorityHarmony,
                            preflight,
                            null,
                            patchAllInvoked,
                            diagnostics);
                    }

                    if (!TryInspectDedicatedOwner(out alreadyInstalled, diagnostics) || alreadyInstalled)
                    {
                        diagnostics.Add(
                            "The dedicated raised-priority owner changed during uncategorized PatchAll.");
                        return RollBackFailedInstallation(
                            raisedPriorityHarmony,
                            preflight,
                            null,
                            patchAllInvoked,
                            diagnostics);
                    }
                }

                try
                {
                    InstallDefinitionsIndividually(raisedPriorityHarmony);
                }
                catch (Exception exception)
                {
                    diagnostics.Add("Harmony installation threw " + exception.GetType().FullName + ": " +
                        Trim(exception.Message));
                }

                List<BwtRaisedPriorityPatchAttempt> installation =
                    CollectInstallationAttempts();
                bool installed = installation.Count == _definitions.Count &&
                    installation.All(value => value.Succeeded) &&
                    TryInspectDedicatedOwner(out alreadyInstalled, diagnostics) &&
                    alreadyInstalled;
                if (installed)
                {
                    ActivateFeature();
                    return CreateReport(
                        BwtRaisedPriorityInstallState.Installed,
                        preflight,
                        installation,
                        patchAllInvoked,
                        false,
                        true,
                        diagnostics,
                        _definitions.Count);
                }

                diagnostics.Add("The six required transpilers were not installed as one complete feature.");
                return RollBackFailedInstallation(
                    raisedPriorityHarmony,
                    preflight,
                    installation,
                    patchAllInvoked,
                    diagnostics);
            }
        }

        private BwtRaisedPriorityInstallReport RollBackFailedInstallation(
            Harmony harmony,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked,
            IList<string> diagnostics)
        {
            bool rollbackSucceeded = TryRemoveDedicatedOwnerPatches(harmony, diagnostics);
            DeactivateFeature();
            return CreateReport(
                rollbackSucceeded
                    ? BwtRaisedPriorityInstallState.RolledBack
                    : BwtRaisedPriorityInstallState.RollbackFailed,
                preflight,
                installation,
                patchAllInvoked,
                true,
                rollbackSucceeded,
                diagnostics);
        }

        private bool TryPatchUncategorized(Harmony harmony, IList<string> diagnostics)
        {
            try
            {
                harmony.PatchAllUncategorized(typeof(BwtRaisedPriorityFeatureInstaller).Assembly);
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
                            definition,
                            "The target or transpiler method could not be resolved."));
                        continue;
                    }

                    ILGenerator generator;
                    List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(
                        definition.Target,
                        out generator,
                        AllTranspilers);
                    if (generator == null)
                    {
                        generator = new DynamicMethod(
                            "bwt-raised-priority-preflight",
                            typeof(void),
                            Type.EmptyTypes).GetILGenerator();
                    }

                    results.Add(BwtRaisedPriorityPatchAttempt.FromResult(
                        definition,
                        InvokeTranspiler(definition, current, generator)));
                }
                catch (Exception exception)
                {
                    results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition,
                        exception.GetType().FullName + ": " + Trim(exception.Message)));
                }
            }

            return results;
        }

        private void InstallDefinitionsIndividually(Harmony harmony)
        {
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                harmony.CreateProcessor(definition.Target)
                    .AddTranspiler(new HarmonyMethod(definition.Transpiler))
                    .Patch();
            }
        }

        private List<BwtRaisedPriorityPatchAttempt> CollectInstallationAttempts()
        {
            var attempts = new List<BwtRaisedPriorityPatchAttempt>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (HasExpectedDedicatedOwnerRecord(definition))
                {
                    attempts.Add(new BwtRaisedPriorityPatchAttempt(
                        definition.Id,
                        PatchOutcome.Applied,
                        null,
                        "The dedicated-owner transpiler record was registered."));
                }
                else
                {
                    attempts.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition,
                        "No dedicated-owner transpiler record was registered."));
                }
            }

            return attempts;
        }

        private bool HasExpectedDedicatedOwnerRecord(
            BwtRaisedPriorityPatchDefinition definition)
        {
            if (definition == null || definition.Target == null || definition.Transpiler == null)
                return false;

            HarmonyLib.Patches patches = Harmony.GetPatchInfo(definition.Target);
            return patches != null && patches.Transpilers.Any(patch =>
                patch != null &&
                String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal) &&
                patch.PatchMethod == definition.Transpiler);
        }

        private bool TryInspectDedicatedOwner(out bool alreadyInstalled, IList<string> diagnostics)
        {
            alreadyInstalled = false;
            var expectedCounts = _definitions.ToDictionary(definition => definition, definition => 0);
            bool hasOwnerPatch = false;
            try
            {
                foreach (MethodBase target in Harmony.GetAllPatchedMethods())
                {
                    HarmonyLib.Patches patches = Harmony.GetPatchInfo(target);
                    foreach (HarmonyLib.Patch patch in GetPatches(patches))
                    {
                        if (!String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal))
                            continue;

                        hasOwnerPatch = true;
                        BwtRaisedPriorityPatchDefinition definition = _definitions.FirstOrDefault(value =>
                            value.Target.Equals(target) &&
                            value.Transpiler == patch.PatchMethod &&
                            patches.Transpilers.Contains(patch));
                        if (definition == null)
                        {
                            diagnostics.Add(
                                "The dedicated raised-priority owner has an unexpected patch on " +
                                DescribeTarget(target) + "; installation is rejected before mutation.");
                            return false;
                        }

                        expectedCounts[definition]++;
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add("Harmony owner inspection failed: " +
                    exception.GetType().FullName + ": " + Trim(exception.Message));
                return false;
            }

            if (!hasOwnerPatch)
                return true;

            if (expectedCounts.All(value => value.Value == 1))
            {
                alreadyInstalled = true;
                return true;
            }

            diagnostics.Add("The dedicated raised-priority owner has a partial installation; " +
                "installation is rejected before mutation.");
            return false;
        }

        private static IEnumerable<HarmonyLib.Patch> GetPatches(HarmonyLib.Patches patches)
        {
            if (patches == null)
                return Enumerable.Empty<HarmonyLib.Patch>();

            return (patches.Prefixes ?? Enumerable.Empty<HarmonyLib.Patch>())
                .Concat(patches.Postfixes ?? Enumerable.Empty<HarmonyLib.Patch>())
                .Concat(patches.Transpilers ?? Enumerable.Empty<HarmonyLib.Patch>())
                .Concat(patches.Finalizers ?? Enumerable.Empty<HarmonyLib.Patch>())
                .Concat(patches.InnerPrefixes ?? Enumerable.Empty<HarmonyLib.Patch>())
                .Concat(patches.InnerPostfixes ?? Enumerable.Empty<HarmonyLib.Patch>());
        }

        private bool TryRemoveDedicatedOwnerPatches(Harmony harmony, IList<string> diagnostics)
        {
            try
            {
                foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
                {
                    if (GetPatches(Harmony.GetPatchInfo(target)).Any(patch =>
                        String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal)))
                    {
                        harmony.Unpatch(target, HarmonyPatchType.All, _raisedPriorityOwnerId);
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add("Dedicated-owner cleanup failed: " +
                    exception.GetType().FullName + ": " + Trim(exception.Message));
                return false;
            }

            try
            {
                bool remain = Harmony.GetAllPatchedMethods().Any(target =>
                    GetPatches(Harmony.GetPatchInfo(target)).Any(patch =>
                        String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal)));
                if (!remain)
                    return true;

                diagnostics.Add("Dedicated-owner cleanup completed with owner patches still present.");
                return false;
            }
            catch (Exception exception)
            {
                diagnostics.Add("Dedicated-owner cleanup could not be verified: " +
                    exception.GetType().FullName + ": " + Trim(exception.Message));
                return false;
            }
        }

        private void RefreshFeatureState()
        {
            if (!_isFeatureActive || _activeDefinitions == null ||
                !String.Equals(_activeOwnerId, _raisedPriorityOwnerId, StringComparison.Ordinal))
            {
                return;
            }

            bool complete;
            var inspector = new BwtRaisedPriorityFeatureInstaller(
                _activeDefinitions,
                _activeOwnerId);
            if (!inspector.TryInspectDedicatedOwner(out complete, new List<string>()) || !complete)
                DeactivateFeature();
        }

        private void ActivateFeature()
        {
            _activeDefinitions = _definitions;
            _activeOwnerId = _raisedPriorityOwnerId;
            _isFeatureActive = true;
        }

        private static void DeactivateFeature()
        {
            _activeDefinitions = null;
            _activeOwnerId = null;
            _isFeatureActive = false;
        }

        private bool TryValidatePlan(IList<string> diagnostics)
        {
            bool valid = true;
            if (_definitions == null || _definitions.Count != 6)
            {
                AddDiagnostic(diagnostics, "The raised-priority plan must contain exactly six definitions.");
                valid = false;
            }

            if (String.IsNullOrWhiteSpace(_raisedPriorityOwnerId))
            {
                AddDiagnostic(diagnostics, "The dedicated raised-priority owner is missing.");
                valid = false;
            }
            else if (String.Equals(_raisedPriorityOwnerId, HarmonyOwnerId, StringComparison.Ordinal))
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
            MethodInfo transpiler,
            out string error)
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
            if (categories.Length != 1 ||
                categories[0].ConstructorArguments.Count != 1 ||
                !String.Equals(
                    categories[0].ConstructorArguments[0].Value as string,
                    RaisedPriorityPatchCategory,
                    StringComparison.Ordinal))
            {
                error = "the declaring patch type must have exactly one raised-priority category";
                return false;
            }

            return true;
        }

        private BwtRaisedPriorityInstallReport CreateReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked,
            bool rollbackAttempted,
            bool rollbackSucceeded,
            IEnumerable<string> diagnostics,
            int featurePatchCount = 0,
            BwtRaisedPriorityFeatureGateState? featureGateState = null)
        {
            BwtRaisedPriorityInstallReport report = new BwtRaisedPriorityInstallReport(
                state,
                preflight ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                installation ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                patchAllInvoked,
                rollbackAttempted,
                rollbackSucceeded,
                diagnostics,
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
            return _isFeatureActive
                ? BwtRaisedPriorityFeatureGateState.PreservedAfterRejectedReconfiguration
                : BwtRaisedPriorityFeatureGateState.Inactive;
        }

        private static BwtPatchResult InvokeTranspiler(
            BwtRaisedPriorityPatchDefinition definition,
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
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
                {
                    throw new InvalidOperationException(
                        "Unsupported BWT transpiler parameter " + parameterType.FullName + ".");
                }
            }

            return definition.Transpiler.Invoke(null, arguments) as BwtPatchResult;
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
                    AccessTools.Method(
                        typeof(PawnColumnWorker_WorkPriority),
                        nameof(PawnColumnWorker_WorkPriority.HeaderClicked)),
                    AccessTools.Method(
                        typeof(Patch_PawnColumnWorker_WorkPriority_HeaderClicked),
                        "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.SetPriority,
                    AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority)),
                    AccessTools.Method(typeof(Patch_Pawn_WorkSettings_SetPriority), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.DoHeader,
                    AccessTools.Method(
                        typeof(PawnColumnWorker),
                        nameof(PawnColumnWorker.DoHeader),
                        new[] { typeof(Rect), typeof(PawnTable) }),
                    AccessTools.Method(
                        typeof(Better_Work_Tab.UI.Headers.Patch_PawnColumnWorker_DoHeader_DisableHighlight),
                        "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.LabelDoCell,
                    AccessTools.Method(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell)),
                    AccessTools.Method(
                        typeof(Better_Work_Tab.Patches.Patch_PawnColumnWorker_Label_DoCell_Transpiler),
                        "Transpiler"))
            });
        }

        private static string DescribeTarget(MethodBase target)
        {
            return target == null
                ? "<missing-target>"
                : (target.DeclaringType == null ? "<missing-type>" : target.DeclaringType.FullName) +
                    "." + target.Name;
        }

        private static void AddDiagnostic(IList<string> diagnostics, string message)
        {
            if (diagnostics != null && !String.IsNullOrEmpty(message))
                diagnostics.Add(message);
        }

        private static string Trim(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            return value.Length <= 240 ? value : value.Substring(0, 240);
        }
    }
}
